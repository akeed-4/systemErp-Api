using Erp.Modules.Accounting.Contracts;
using Erp.Modules.Accounting.Domain;
using Erp.Modules.Accounting.Persistence;
using Erp.SharedKernel.Errors;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Accounting.Application;

/// <summary>Frontend Account shape; balance is natural-signed and rolled up to parents.</summary>
internal sealed record AccountDto(
    Guid Id,
    Guid TenantId,
    string Code,
    string NameAr,
    string NameEn,
    AccountType Type,
    string? ParentCode,
    int Level,
    decimal Balance,
    bool IsDebitNature,
    bool IsSystem,
    bool IsPostable,
    bool IsActive,
    string? Currency,
    string? LinkedEntityType,
    Guid? LinkedEntityId,
    string? Notes)
{
    public List<AccountDto>? Children { get; set; }
}

internal sealed record TrialBalanceRow(
    Guid AccountId,
    string Code,
    string NameAr,
    string NameEn,
    AccountType Type,
    int Level,
    string? ParentCode,
    bool IsPostable,
    decimal OpeningDebit,
    decimal OpeningCredit,
    decimal PeriodDebit,
    decimal PeriodCredit,
    decimal ClosingDebit,
    decimal ClosingCredit);

internal sealed record TrialBalanceReport(DateOnly From, DateOnly To, IReadOnlyList<TrialBalanceRow> Rows, decimal TotalDebit, decimal TotalCredit, bool IsBalanced);

internal sealed record StatementLine(string Code, string NameAr, string NameEn, decimal Amount);

internal sealed record IncomeStatementReport(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<StatementLine> Revenue,
    IReadOnlyList<StatementLine> Expenses,
    decimal TotalRevenue,
    decimal TotalExpenses,
    decimal NetProfit);

internal sealed record BalanceSheetReport(
    DateOnly AsOf,
    IReadOnlyList<StatementLine> Assets,
    IReadOnlyList<StatementLine> Liabilities,
    IReadOnlyList<StatementLine> Equity,
    decimal TotalAssets,
    decimal TotalLiabilities,
    decimal TotalEquity,
    decimal CurrentEarnings,
    bool IsBalanced);

internal sealed record VatReturnReport(DateOnly From, DateOnly To, decimal OutputVat, decimal InputVat, decimal NetVatPayable);

internal sealed record AccountStatementRow(DateOnly Date, string? EntryNumber, string Description, string? Reference, decimal Debit, decimal Credit, decimal Balance);

internal sealed record AccountStatementReport(AccountDto Account, DateOnly From, DateOnly To, decimal OpeningBalance, IReadOnlyList<AccountStatementRow> Rows, decimal ClosingBalance);

internal sealed class AccountingQueries(AccountingDbContext db, IAccountLookup lookup)
{
    public async Task<List<AccountDto>> GetAccountsAsync(bool tree, CancellationToken ct)
    {
        var accounts = await db.Accounts.AsNoTracking().OrderBy(a => a.Code).ToListAsync(ct);
        var raw = await db.AccountBalances.AsNoTracking()
            .GroupBy(b => b.AccountId)
            .Select(g => new { g.Key, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) })
            .ToDictionaryAsync(x => x.Key, x => (x.Debit, x.Credit), ct);

        var totals = RollUp(accounts, raw);
        var byId = accounts.ToDictionary(a => a.Id);
        var dtos = accounts.Select(a =>
        {
            var (debit, credit) = totals.GetValueOrDefault(a.Id);
            return ToDto(a, a.ParentId is { } p ? byId[p].Code : null, a.NaturalBalance(debit, credit));
        }).ToList();

        if (!tree)
        {
            return dtos;
        }

        var dtoById = dtos.ToDictionary(d => d.Id);
        var roots = new List<AccountDto>();
        foreach (var account in accounts)
        {
            var dto = dtoById[account.Id];
            if (account.ParentId is { } parentId)
            {
                (dtoById[parentId].Children ??= []).Add(dto);
            }
            else
            {
                roots.Add(dto);
            }
        }

        return roots;
    }

    public async Task<TrialBalanceReport> TrialBalanceAsync(DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        var accounts = await db.Accounts.AsNoTracking().OrderBy(a => a.Code).ToListAsync(ct);
        var movements = await (
            from l in db.JournalEntryLines.AsNoTracking()
            join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
            where e.Status != JournalEntryStatus.Draft && e.Date <= toDate
            group new { l.Debit, l.Credit, e.Date } by l.AccountId into g
            select new
            {
                AccountId = g.Key,
                OpenDebit = g.Sum(x => x.Date < fromDate ? x.Debit : 0m),
                OpenCredit = g.Sum(x => x.Date < fromDate ? x.Credit : 0m),
                PeriodDebit = g.Sum(x => x.Date >= fromDate ? x.Debit : 0m),
                PeriodCredit = g.Sum(x => x.Date >= fromDate ? x.Credit : 0m),
            }).ToListAsync(ct);

        var opening = RollUp(accounts, movements.ToDictionary(m => m.AccountId, m => (m.OpenDebit, m.OpenCredit)));
        var period = RollUp(accounts, movements.ToDictionary(m => m.AccountId, m => (m.PeriodDebit, m.PeriodCredit)));
        var byId = accounts.ToDictionary(a => a.Id);

        var rows = accounts.Select(a =>
        {
            var (od, oc) = opening.GetValueOrDefault(a.Id);
            var (pd, pc) = period.GetValueOrDefault(a.Id);
            var (openDebit, openCredit) = Net(od, oc);
            var (closeDebit, closeCredit) = Net(od + pd, oc + pc);
            return new TrialBalanceRow(a.Id, a.Code, a.NameAr, a.NameEn, a.Type, a.Level, a.ParentId is { } p ? byId[p].Code : null, a.IsPostable,
                openDebit, openCredit, pd, pc, closeDebit, closeCredit);
        })
        .Where(r => r.OpeningDebit + r.OpeningCredit + r.PeriodDebit + r.PeriodCredit != 0)
        .ToList();

        var leaves = rows.Where(r => r.IsPostable).ToList();
        var totalDebit = leaves.Sum(r => r.ClosingDebit);
        var totalCredit = leaves.Sum(r => r.ClosingCredit);
        return new TrialBalanceReport(fromDate, toDate, rows, totalDebit, totalCredit, totalDebit == totalCredit);
    }

    public async Task<IncomeStatementReport> IncomeStatementAsync(DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        var lines = await LeafMovementsAsync(fromDate, toDate, [AccountType.Revenue, AccountType.Expense], ct);
        var revenue = lines.Where(l => l.Account.Type == AccountType.Revenue)
            .Select(l => new StatementLine(l.Account.Code, l.Account.NameAr, l.Account.NameEn, l.Credit - l.Debit)).ToList();
        var expenses = lines.Where(l => l.Account.Type == AccountType.Expense)
            .Select(l => new StatementLine(l.Account.Code, l.Account.NameAr, l.Account.NameEn, l.Debit - l.Credit)).ToList();
        var totalRevenue = revenue.Sum(r => r.Amount);
        var totalExpenses = expenses.Sum(e => e.Amount);
        return new IncomeStatementReport(fromDate, toDate, revenue, expenses, totalRevenue, totalExpenses, totalRevenue - totalExpenses);
    }

    public async Task<BalanceSheetReport> BalanceSheetAsync(DateOnly asOf, CancellationToken ct)
    {
        var lines = await LeafMovementsAsync(DateOnly.MinValue, asOf, [AccountType.Asset, AccountType.Liability, AccountType.Equity, AccountType.Revenue, AccountType.Expense], ct);

        List<StatementLine> Section(AccountType type, bool debitPositive) =>
            lines.Where(l => l.Account.Type == type)
                .Select(l => new StatementLine(l.Account.Code, l.Account.NameAr, l.Account.NameEn, debitPositive ? l.Debit - l.Credit : l.Credit - l.Debit))
                .ToList();

        var assets = Section(AccountType.Asset, debitPositive: true);
        var liabilities = Section(AccountType.Liability, debitPositive: false);
        var equity = Section(AccountType.Equity, debitPositive: false);
        var earnings = Section(AccountType.Revenue, false).Sum(x => x.Amount) - Section(AccountType.Expense, true).Sum(x => x.Amount);

        var totalAssets = assets.Sum(a => a.Amount);
        var totalLiabilities = liabilities.Sum(a => a.Amount);
        var totalEquity = equity.Sum(a => a.Amount) + earnings;
        return new BalanceSheetReport(asOf, assets, liabilities, equity, totalAssets, totalLiabilities, totalEquity, earnings,
            totalAssets == totalLiabilities + totalEquity);
    }

    public async Task<VatReturnReport> VatReturnAsync(DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        var output = await lookup.ResolveAsync(PostingPurpose.OutputVat, ct);
        var input = await lookup.ResolveAsync(PostingPurpose.InputVat, ct);
        var totals = await (
            from l in db.JournalEntryLines.AsNoTracking()
            join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
            where e.Status != JournalEntryStatus.Draft && e.Date >= fromDate && e.Date <= toDate && (l.AccountId == output.Id || l.AccountId == input.Id)
            group l by l.AccountId into g
            select new { g.Key, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) }).ToListAsync(ct);

        var outputVat = totals.Where(t => t.Key == output.Id).Sum(t => t.Credit - t.Debit);
        var inputVat = totals.Where(t => t.Key == input.Id).Sum(t => t.Debit - t.Credit);
        return new VatReturnReport(fromDate, toDate, outputVat, inputVat, outputVat - inputVat);
    }

    public async Task<AccountStatementReport> StatementAsync(Guid accountId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == accountId, ct)
            ?? throw ErpException.NotFound("Account", "الحساب");
        var ids = await DescendantIdsAsync(account, ct);

        var entries =
            from l in db.JournalEntryLines.AsNoTracking()
            join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
            where ids.Contains(l.AccountId) && e.Status != JournalEntryStatus.Draft && e.Date <= toDate
            select new { e.Date, e.EntryNumber, e.Description, e.SourceDocumentNumber, l.Debit, l.Credit, e.PostedAt, l.LineNo };

        var openingRaw = await entries.Where(x => x.Date < fromDate).GroupBy(_ => 1)
            .Select(g => new { Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) }).SingleOrDefaultAsync(ct);
        var opening = openingRaw is null ? 0 : account.NaturalBalance(openingRaw.Debit, openingRaw.Credit);

        var movements = await entries.Where(x => x.Date >= fromDate)
            .OrderBy(x => x.Date).ThenBy(x => x.PostedAt).ThenBy(x => x.EntryNumber).ThenBy(x => x.LineNo)
            .ToListAsync(ct);

        var running = opening;
        var rows = movements.Select(m =>
        {
            running += account.NaturalBalance(m.Debit, m.Credit);
            return new AccountStatementRow(m.Date, m.EntryNumber, m.Description, m.SourceDocumentNumber, m.Debit, m.Credit, running);
        }).ToList();

        var parentCode = account.ParentId is { } p ? await db.Accounts.Where(a => a.Id == p).Select(a => a.Code).SingleAsync(ct) : null;
        return new AccountStatementReport(ToDto(account, parentCode, running), fromDate, toDate, opening, rows, running);
    }

    public static AccountDto ToDto(Account a, string? parentCode, decimal balance) =>
        new(a.Id, a.TenantId, a.Code, a.NameAr, a.NameEn, a.Type, parentCode, a.Level, balance, a.IsDebitNature, a.IsSystem, a.IsPostable,
            a.IsActive, a.CurrencyCode, a.LinkedEntityType, a.LinkedEntityId, a.Notes);

    private async Task<List<(Account Account, decimal Debit, decimal Credit)>> LeafMovementsAsync(DateOnly fromDate, DateOnly toDate, AccountType[] types, CancellationToken ct)
    {
        var accounts = await db.Accounts.AsNoTracking().Where(a => a.IsPostable && types.Contains(a.Type)).OrderBy(a => a.Code).ToListAsync(ct);
        var ids = accounts.Select(a => a.Id).ToList();
        var totals = await (
            from l in db.JournalEntryLines.AsNoTracking()
            join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
            where ids.Contains(l.AccountId) && e.Status != JournalEntryStatus.Draft && e.Date >= fromDate && e.Date <= toDate
            group l by l.AccountId into g
            select new { g.Key, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) })
            .ToDictionaryAsync(x => x.Key, x => (x.Debit, x.Credit), ct);

        return accounts.Where(a => totals.ContainsKey(a.Id))
            .Select(a => (a, totals[a.Id].Debit, totals[a.Id].Credit))
            .ToList();
    }

    private async Task<List<Guid>> DescendantIdsAsync(Account root, CancellationToken ct)
    {
        var all = await db.Accounts.AsNoTracking().Select(a => new { a.Id, a.ParentId }).ToListAsync(ct);
        var result = new List<Guid> { root.Id };
        for (var i = 0; i < result.Count; i++)
        {
            result.AddRange(all.Where(a => a.ParentId == result[i]).Select(a => a.Id));
        }

        return result;
    }

    /// <summary>Adds each account's raw debit/credit to all its ancestors.</summary>
    private static Dictionary<Guid, (decimal Debit, decimal Credit)> RollUp(List<Account> accounts, Dictionary<Guid, (decimal Debit, decimal Credit)> raw)
    {
        var byId = accounts.ToDictionary(a => a.Id);
        var totals = new Dictionary<Guid, (decimal Debit, decimal Credit)>();
        foreach (var (accountId, (debit, credit)) in raw)
        {
            Guid? current = accountId;
            while (current is { } id && byId.TryGetValue(id, out var account))
            {
                var (d, c) = totals.GetValueOrDefault(id);
                totals[id] = (d + debit, c + credit);
                current = account.ParentId;
            }
        }

        return totals;
    }

    private static (decimal Debit, decimal Credit) Net(decimal debit, decimal credit) =>
        debit >= credit ? (debit - credit, 0m) : (0m, credit - debit);
}
