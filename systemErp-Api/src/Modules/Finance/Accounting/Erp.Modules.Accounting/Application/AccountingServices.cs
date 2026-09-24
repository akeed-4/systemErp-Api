using System.Globalization;
using Erp.Modules.Accounting.Contracts;
using Erp.Modules.Accounting.Domain;
using Erp.Modules.Accounting.Persistence;
using Erp.SharedKernel.Errors;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Accounting.Application;

internal sealed class AccountingPostingService(AccountingDbContext db, PostingEngine engine, IAccountLookup lookup) : IAccountingPostingService
{
    public async Task<PostingResult> PostAsync(PostingRequest request, CancellationToken cancellationToken)
    {
        var existing = await FindLiveEntryAsync(request.Source, request.Kind, cancellationToken);
        if (existing is not null)
        {
            return new PostingResult(existing.Id, existing.EntryNumber!, AlreadyPosted: true);
        }

        var lines = new List<JournalEntryLine>();
        foreach (var line in request.Lines)
        {
            var accountId = line.Account.AccountId ?? (await lookup.ResolveAsync(line.Account.Purpose!.Value, cancellationToken)).Id;
            lines.Add(new JournalEntryLine(accountId, line.Debit, line.Credit, line.CostCenterId, line.Party?.Type, line.Party?.Id, line.Notes));
        }

        var entry = new JournalEntry(request.Date, request.Description, request.Source, request.Kind);
        entry.AddLines(lines);
        db.JournalEntries.Add(entry);
        await engine.PostAsync(entry, cancellationToken);
        return new PostingResult(entry.Id, entry.EntryNumber!, AlreadyPosted: false);
    }

    public async Task<PostingResult> ReverseAsync(SourceRef source, string kind, DateOnly date, string reason, CancellationToken cancellationToken)
    {
        var original = await FindLiveEntryAsync(source, kind, cancellationToken)
            ?? throw ErpException.NotFound("Posted journal entry", "القيد المرحّل");
        var reversal = await ReverseEntryAsync(original, date, reason, cancellationToken);
        return new PostingResult(reversal.Id, reversal.EntryNumber!, AlreadyPosted: false);
    }

    public async Task<PostingResult?> RepostAsync(PostingRequest request, CancellationToken cancellationToken)
    {
        var existing = await FindLiveEntryAsync(request.Source, request.Kind, cancellationToken);
        if (existing is not null)
        {
            await ReverseEntryAsync(existing, request.Date, "تعديل المستند المصدر", cancellationToken);
        }

        return request.Lines.Count == 0 ? null : await PostAsync(request, cancellationToken);
    }

    /// <summary>Posts the mirror of <paramref name="original"/> (same source, lines swapped) and marks the original reversed.</summary>
    public async Task<JournalEntry> ReverseEntryAsync(JournalEntry original, DateOnly date, string reason, CancellationToken cancellationToken)
    {
        if (original.Status != JournalEntryStatus.Posted || original.ReversalOfId is not null)
        {
            throw ErpException.Conflict("entry_not_reversible", "Only posted, non-reversal entries can be reversed.", "لا يمكن عكس هذا القيد.");
        }

        var source = new SourceRef(original.SourceModule, original.SourceDocumentType, original.SourceDocumentId, original.SourceDocumentNumber ?? original.EntryNumber!);
        var reversal = new JournalEntry(date, $"قيد عكسي للقيد {original.EntryNumber}: {reason}", source, original.PostingKind);
        reversal.MarkAsReversalOf(original);
        reversal.AddLines(original.Lines.Select(l => l.Mirror()));
        db.JournalEntries.Add(reversal);

        await engine.PostAsync(reversal, cancellationToken);
        original.MarkReversed(reversal.Id);

        // Flush inside the ambient transaction so a re-post of the same source sees the original as reversed.
        await db.SaveChangesAsync(cancellationToken);
        return reversal;
    }

    private Task<JournalEntry?> FindLiveEntryAsync(SourceRef source, string kind, CancellationToken ct) =>
        db.JournalEntries.Include(e => e.Lines).SingleOrDefaultAsync(
            e => e.SourceModule == source.Module
                && e.SourceDocumentType == source.DocumentType
                && e.SourceDocumentId == source.DocumentId
                && e.PostingKind == kind
                && e.ReversalOfId == null
                && e.Status == JournalEntryStatus.Posted,
            ct);
}

internal sealed class AccountLookup(AccountingDbContext db) : IAccountLookup
{
    public async Task<AccountSummary?> FindAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        return account is null ? null : ToSummary(account);
    }

    public async Task<AccountSummary?> FindByCodeAsync(string code, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Code == code.Trim(), cancellationToken);
        return account is null ? null : ToSummary(account);
    }

    public async Task<IReadOnlyDictionary<Guid, AccountSummary>> FindManyAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken)
    {
        if (accountIds.Count == 0)
        {
            return new Dictionary<Guid, AccountSummary>();
        }

        var accounts = await db.Accounts.AsNoTracking().Where(a => accountIds.Contains(a.Id)).ToListAsync(cancellationToken);
        return accounts.ToDictionary(a => a.Id, ToSummary);
    }

    public async Task<AccountSummary> ResolveAsync(PostingPurpose purpose, CancellationToken cancellationToken)
    {
        var account = await (
            from m in db.PostingMappings.AsNoTracking()
            join a in db.Accounts.AsNoTracking() on m.AccountId equals a.Id
            where m.Purpose == purpose
            select a).SingleOrDefaultAsync(cancellationToken);

        return account is null
            ? throw ErpException.Conflict(
                "posting_mapping_missing",
                $"No account is mapped for '{purpose}'. Configure it in the posting mappings.",
                "لا يوجد حساب مربوط لهذا الغرض المحاسبي، يرجى ضبط التوجيه المحاسبي.")
            : ToSummary(account);
    }

    public static AccountSummary ToSummary(Account a) =>
        new(a.Id, a.Code, a.NameAr, a.NameEn, a.Type, a.IsPostable, a.IsActive, a.LinkedEntityType, a.LinkedEntityId);
}

internal sealed class AccountProvisioningService(AccountingDbContext db, IAccountLookup lookup) : IAccountProvisioningService
{
    public async Task<AccountSummary> CreateSubAccountAsync(SubAccountRequest request, CancellationToken cancellationToken)
    {
        var existing = await db.Accounts.SingleOrDefaultAsync(
            a => a.LinkedEntityType == request.LinkedEntityType && a.LinkedEntityId == request.LinkedEntityId, cancellationToken);
        if (existing is not null)
        {
            return AccountLookup.ToSummary(existing);
        }

        var parentSummary = await lookup.ResolveAsync(request.ParentPurpose, cancellationToken);
        var parent = await db.Accounts.SingleAsync(a => a.Id == parentSummary.Id, cancellationToken);
        if (parent.IsPostable && await db.JournalEntryLines.AnyAsync(l => l.AccountId == parent.Id, cancellationToken))
        {
            throw ErpException.Conflict(
                "parent_has_postings",
                $"Account {parent.Code} already has postings and cannot get sub-accounts.",
                $"الحساب {parent.Code} عليه حركات ولا يمكن إضافة حسابات فرعية تحته.");
        }

        var code = request.PreferredCode is { Length: > 0 } preferred ? preferred.Trim() : await NextChildCodeAsync(parent, cancellationToken);
        if (!code.StartsWith(parent.Code, StringComparison.Ordinal) || code.Length <= parent.Code.Length)
        {
            throw ErpException.Validation($"The account code must start with {parent.Code}.", $"رمز الحساب يجب أن يبدأ بـ {parent.Code}.");
        }

        if (db.Accounts.Local.Any(a => a.Code == code) || await db.Accounts.AnyAsync(a => a.Code == code, cancellationToken))
        {
            throw ErpException.Conflict("account_code_taken", $"Account code {code} already exists.", $"رمز الحساب {code} مستخدم مسبقاً.");
        }

        var account = new Account(code, request.NameAr, request.NameEn, parent.Type, parent, parent.IsDebitNature, isSystem: false);
        account.LinkTo(request.LinkedEntityType, request.LinkedEntityId);
        account.Update(account.NameAr, account.NameEn, null, request.CurrencyCode, isActive: true);
        db.Accounts.Add(account);
        return AccountLookup.ToSummary(account);
    }

    public async Task RenameAsync(Guid accountId, string nameAr, string nameEn, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        account?.Rename(nameAr, nameEn);
    }

    /// <summary>Parent code + 3-digit sequence (112 → 112001, 112002 …), matching the frontend's per-party codes.</summary>
    private async Task<string> NextChildCodeAsync(Account parent, CancellationToken ct)
    {
        var length = parent.Code.Length + 3;
        // Include sub-accounts created earlier in the same unit of work (not yet saved).
        var siblings = (await db.Accounts.AsNoTracking()
                .Where(a => a.ParentId == parent.Id && a.Code.Length == length)
                .Select(a => a.Code)
                .ToListAsync(ct))
            .Concat(db.Accounts.Local.Where(a => a.ParentId == parent.Id && a.Code.Length == length).Select(a => a.Code))
            .ToList();
        var next = siblings.Select(c => int.TryParse(c[parent.Code.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .DefaultIfEmpty(0).Max() + 1;
        return parent.Code + next.ToString("000", CultureInfo.InvariantCulture);
    }
}

internal sealed class AccountBalanceQueries(AccountingDbContext db) : IAccountBalanceQueries
{
    public async Task<decimal> GetBalanceAsync(Guid accountId, DateOnly? asOf, CancellationToken cancellationToken) =>
        (await GetBalancesAsync([accountId], asOf, cancellationToken)).GetValueOrDefault(accountId);

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetBalancesAsync(IReadOnlyCollection<Guid> accountIds, DateOnly? asOf, CancellationToken cancellationToken)
    {
        var natures = await db.Accounts.AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.IsDebitNature, cancellationToken);

        Dictionary<Guid, (decimal Debit, decimal Credit)> totals;
        if (asOf is null)
        {
            totals = await db.AccountBalances.AsNoTracking()
                .Where(b => accountIds.Contains(b.AccountId))
                .GroupBy(b => b.AccountId)
                .Select(g => new { g.Key, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) })
                .ToDictionaryAsync(x => x.Key, x => (x.Debit, x.Credit), cancellationToken);
        }
        else
        {
            var date = asOf.Value;
            totals = await (
                from l in db.JournalEntryLines.AsNoTracking()
                join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
                where accountIds.Contains(l.AccountId) && e.Status != JournalEntryStatus.Draft && e.Date <= date
                group l by l.AccountId into g
                select new { g.Key, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) })
                .ToDictionaryAsync(x => x.Key, x => (x.Debit, x.Credit), cancellationToken);
        }

        return natures.ToDictionary(
            n => n.Key,
            n => totals.TryGetValue(n.Key, out var t) ? (n.Value ? t.Debit - t.Credit : t.Credit - t.Debit) : 0m);
    }
}

internal sealed class AccountStatementService(AccountingQueries queries) : IAccountStatementService
{
    public async Task<AccountStatement> GetAsync(Guid accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var report = await queries.StatementAsync(accountId, from, to, cancellationToken);
        return new AccountStatement(
            report.Account.Id,
            report.Account.Code,
            report.Account.NameAr,
            report.From,
            report.To,
            report.OpeningBalance,
            report.Rows.Select(r => new AccountStatementEntry(r.Date, r.EntryNumber, r.Description, r.Reference, r.Debit, r.Credit, r.Balance)).ToList(),
            report.ClosingBalance);
    }
}

internal sealed class CostCenterLookup(AccountingDbContext db) : ICostCenterLookup
{
    public Task<bool> ExistsAsync(Guid costCenterId, CancellationToken cancellationToken) =>
        db.CostCenters.AnyAsync(c => c.Id == costCenterId && c.IsActive, cancellationToken);
}
