using ERP.Service.Services.Shared;
using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

using DevExtreme.AspNet.Data.ResponseModel;

namespace ERP.Service.Services.Accounting;

public class AccountingReportService : IAccountingReportService
{
    private readonly ErpDbContext _db;
    public AccountingReportService(ErpDbContext db) => _db = db;

    public async Task<List<TrialBalanceItemDto>> GetTrialBalanceAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var lines = await (from l in _db.Set<JournalEntryLine>().AsNoTracking()
                           join e in _db.Set<JournalEntry>().AsNoTracking() on l.JournalEntryId equals e.Id
                           select new { l.AccountCode, l.Debit, l.Credit, e.Date }).ToListAsync(ct);
        var accounts = await _db.Set<Account>().AsNoTracking().ToDictionaryAsync(a => a.Code, ct);

        var rows = new List<TrialBalanceItemDto>();
        foreach (var g in lines.GroupBy(l => l.AccountCode).OrderBy(g => g.Key))
        {
            if (to.HasValue && g.All(l => l.Date > to)) continue;
            var opening = from.HasValue ? g.Where(l => l.Date < from).ToList() : new();
            var period = g.Where(l => (!from.HasValue || l.Date >= from) && (!to.HasValue || l.Date <= to)).ToList();
            var openNet = opening.Sum(l => l.Debit) - opening.Sum(l => l.Credit);
            var pd = period.Sum(l => l.Debit); var pc = period.Sum(l => l.Credit);
            var closeNet = openNet + pd - pc;
            accounts.TryGetValue(g.Key, out var acc);
            rows.Add(new TrialBalanceItemDto
            {
                AccountCode = g.Key, AccountNameAr = acc?.NameAr ?? g.Key, AccountCategory = acc?.Type ?? AccountCategory.Asset,
                OpeningDebit = openNet > 0 ? openNet : 0, OpeningCredit = openNet < 0 ? -openNet : 0,
                PeriodDebit = pd, PeriodCredit = pc,
                ClosingDebit = closeNet > 0 ? closeNet : 0, ClosingCredit = closeNet < 0 ? -closeNet : 0,
            });
        }
        return rows;
    }

    public async Task<AccountStatementDto> GetAccountStatementAsync(string accountCode, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var account = await _db.Set<Account>().AsNoTracking().FirstOrDefaultAsync(a => a.Code == accountCode, ct)
            ?? throw new NotFoundException("الحساب غير موجود");

        var q = from l in _db.Set<JournalEntryLine>().AsNoTracking()
                join e in _db.Set<JournalEntry>().AsNoTracking() on l.JournalEntryId equals e.Id
                where l.AccountCode == accountCode
                select new { l.Debit, l.Credit, e.Date, e.EntryNumber, e.Description, e.ReferenceNumber, l.Notes };
        var all = await q.OrderBy(x => x.Date).ThenBy(x => x.EntryNumber).ToListAsync(ct);

        decimal Signed(decimal d, decimal c) => account.IsDebitNature ? d - c : c - d;
        var opening = from.HasValue ? all.Where(x => x.Date < from).Sum(x => Signed(x.Debit, x.Credit)) : 0m;
        var running = opening;
        var dto = new AccountStatementDto { AccountCode = account.Code, AccountNameAr = account.NameAr, OpeningBalance = opening };
        foreach (var x in all.Where(x => (!from.HasValue || x.Date >= from) && (!to.HasValue || x.Date <= to)))
        {
            running += Signed(x.Debit, x.Credit);
            dto.Entries.Add(new AccountLedgerEntryDto
            {
                Date = x.Date, EntryNumber = x.EntryNumber, Description = string.IsNullOrWhiteSpace(x.Notes) ? x.Description : x.Notes!,
                Debit = x.Debit, Credit = x.Credit, RunningBalance = running, ReferenceNumber = x.ReferenceNumber,
            });
        }
        dto.ClosingBalance = running;
        return dto;
    }

    private async Task<List<Account>> LeavesAsync(CancellationToken ct)
    {
        var all = await _db.Set<Account>().AsNoTracking().ToListAsync(ct);
        var parents = all.Where(a => a.ParentCode != null).Select(a => a.ParentCode!).ToHashSet();
        return all.Where(a => !parents.Contains(a.Code)).ToList();
    }

    public async Task<FinancialSummaryDto> GetFinancialSummaryAsync(CancellationToken ct = default)
    {
        var leaves = await LeavesAsync(ct);
        decimal Sum(AccountCategory t) => leaves.Where(a => a.Type == t).Sum(a => a.Balance);
        var assets = Sum(AccountCategory.Asset); var liab = Sum(AccountCategory.Liability); var eq = Sum(AccountCategory.Equity);
        var rev = Sum(AccountCategory.Revenue); var exp = Sum(AccountCategory.Expense);
        var net = rev - exp;
        return new FinancialSummaryDto
        {
            TotalAssets = assets, TotalLiabilities = liab, TotalEquity = eq, TotalRevenues = rev, TotalExpenses = exp,
            NetProfitOrLoss = net, IsBalanceSheetBalanced = Math.Abs(assets - (liab + eq + net)) < 0.01m,
        };
    }

    public async Task<FinancialStatsDto> GetFinancialStatsAsync(CancellationToken ct = default)
    {
        var invoices = await _db.Set<Invoice>().AsNoTracking().Where(i => i.Status == "posted")
            .Select(i => new { i.Kind, i.Subtotal, i.TotalCost }).ToListAsync(ct);
        var sales = invoices.Where(i => i.Kind == InvoiceKind.Sales).Sum(i => i.Subtotal) - invoices.Where(i => i.Kind == InvoiceKind.SalesReturn).Sum(i => i.Subtotal);
        var purchases = invoices.Where(i => i.Kind == InvoiceKind.Purchase).Sum(i => i.Subtotal) - invoices.Where(i => i.Kind == InvoiceKind.PurchaseReturn).Sum(i => i.Subtotal);
        var cogs = invoices.Where(i => i.Kind == InvoiceKind.Sales).Sum(i => i.TotalCost) - invoices.Where(i => i.Kind == InvoiceKind.SalesReturn).Sum(i => i.TotalCost);

        var vouchers = await _db.Set<Voucher>().AsNoTracking().GroupBy(v => v.Type).Select(g => new { g.Key, Sum = g.Sum(v => v.Amount) }).ToListAsync(ct);
        var leaves = await LeavesAsync(ct);
        decimal Under(string prefix) => leaves.Where(a => a.Code.StartsWith(prefix)).Sum(a => a.Balance);

        var opex = Under("52");
        var outputVat = Under(DefaultAccounts.OutputVat); var inputVat = Under(DefaultAccounts.InputVat);
        var inventory = await _db.Set<Product>().AsNoTracking().SumAsync(p => p.CurrentStock * p.AverageCost, ct);

        return new FinancialStatsDto
        {
            TotalSales = sales, TotalPurchases = purchases,
            TotalReceipts = vouchers.FirstOrDefault(v => v.Key == VoucherType.Receipt)?.Sum ?? 0,
            TotalPayments = vouchers.FirstOrDefault(v => v.Key == VoucherType.Payment)?.Sum ?? 0,
            CogsTotal = cogs, OperatingExpenses = opex, NetProfit = sales - cogs - opex,
            InventoryValuation = inventory, OutputVat = outputVat, InputVat = inputVat, NetVatPayable = outputVat - inputVat,
            CashAndBankBalance = Under(DefaultAccounts.Banks), ReceivablesBalance = Under(DefaultAccounts.Receivables), PayablesBalance = Under(DefaultAccounts.Payables),
        };
    }

    public async Task<VatReturnDto> GetVatReturnAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        if (to < from) throw new ValidationFailedException("نهاية الفترة قبل بدايتها.");
        var inv = await _db.Set<Invoice>().AsNoTracking()
            .Where(i => i.Status == "posted" && i.IssueDate >= from && i.IssueDate <= to)
            .Select(i => new { i.Kind, i.Subtotal, i.VatTotal }).ToListAsync(ct);
        var sSales = inv.Where(i => i.Kind == InvoiceKind.Sales).ToList(); var sRet = inv.Where(i => i.Kind == InvoiceKind.SalesReturn).ToList();
        var pPur = inv.Where(i => i.Kind == InvoiceKind.Purchase).ToList(); var pRet = inv.Where(i => i.Kind == InvoiceKind.PurchaseReturn).ToList();
        var outVat = sSales.Sum(i => i.VatTotal) - sRet.Sum(i => i.VatTotal);
        var inVat = pPur.Sum(i => i.VatTotal) - pRet.Sum(i => i.VatTotal);
        return new VatReturnDto
        {
            FromDate = from, ToDate = to,
            StandardRatedSales = sSales.Sum(i => i.Subtotal) - sRet.Sum(i => i.Subtotal), OutputVat = outVat,
            StandardRatedPurchases = pPur.Sum(i => i.Subtotal) - pRet.Sum(i => i.Subtotal), InputVat = inVat,
            NetVatPayable = outVat - inVat,
        };
    }

    public async Task<LoadResult> LoadTrialBalanceAsync(DateTime? from, DateTime? to, DataSourceLoadOptions options, CancellationToken ct = default)
        => await ReportLoader.LoadAsync(await GetTrialBalanceAsync(from, to, ct), options, ct);

    public async Task<LoadResult> LoadAccountStatementEntriesAsync(string accountCode, DateTime? from, DateTime? to, DataSourceLoadOptions options, CancellationToken ct = default)
        => await ReportLoader.LoadAsync((await GetAccountStatementAsync(accountCode, from, to, ct)).Entries, options, ct);
}
