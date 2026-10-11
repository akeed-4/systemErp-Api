using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// التسوية البنكية. القاعدة: رصيد الكشف = مجموع كل الحركات المطابَقة حتى تاريخه (السابقة + المختارة الآن)؛
/// فإن اختلفا رُفض الاعتماد مع بيان الفرق (حركة ناقصة في الدفاتر مثل رسوم بنكية تُقيَّد أولاً بقيد).
/// ما لم يُطابَق يبقى معلّقاً: إيداعات لم تظهر ومدفوعات لم تُصرف، ومنها يتّفق رصيد الدفاتر مع الكشف.
/// </summary>
public class BankReconciliationService : IBankReconciliationService
{
    private readonly ErpDbContext _db;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;

    public BankReconciliationService(ErpDbContext db, ITransactionRunner tx, ICurrentUser user, IAuditService audit)
    {
        _db = db; _tx = tx; _user = user; _audit = audit;
    }

    private async Task<Account> BankAccountAsync(string? accountCode, CancellationToken ct)
    {
        var code = accountCode?.Trim() ?? string.Empty;
        var account = await _db.Set<Account>().AsNoTracking().FirstOrDefaultAsync(a => a.Code == code, ct)
            ?? throw new NotFoundException(Messages.AccountNotFound);
        if (!code.StartsWith(DefaultAccounts.Banks, StringComparison.Ordinal) || code == DefaultAccounts.Banks)
            throw new ValidationFailedException(Messages.ReconciliationRequiresBankAccount);
        return account;
    }

    /// <summary>حركات الحساب المرحّلة حتى تاريخ الكشف (شاملاً اليوم).</summary>
    private IQueryable<JournalEntryLine> LinesThrough(string accountCode, DateTime statementDate)
    {
        var end = statementDate.Date.AddDays(1);
        return from l in _db.Set<JournalEntryLine>()
               join e in _db.Set<JournalEntry>() on l.JournalEntryId equals e.Id
               where l.AccountCode == accountCode && e.Date < end && e.Status == JournalEntryStatus.Posted
               select l;
    }

    public async Task<BankReconciliationWorksheetDto> GetWorksheetAsync(string accountCode, DateTime statementDate, CancellationToken ct = default)
    {
        var account = await BankAccountAsync(accountCode, ct);
        var lines = LinesThrough(account.Code, statementDate).AsNoTracking();
        var open = await (from l in lines.Where(l => l.BankReconciliationId == null)
                          join e in _db.Set<JournalEntry>().AsNoTracking() on l.JournalEntryId equals e.Id
                          orderby e.Date, e.EntryNumber
                          select new BankLineDto
                          {
                              LineId = l.Id, Date = e.Date, EntryNumber = e.EntryNumber, Description = l.Notes ?? e.Description,
                              ReferenceNumber = e.ReferenceNumber, Debit = l.Debit, Credit = l.Credit,
                          }).ToListAsync(ct);
        return new BankReconciliationWorksheetDto
        {
            AccountCode = account.Code, AccountName = account.NameAr, StatementDate = statementDate.Date, UnreconciledLines = open,
            BookBalance = await lines.SumAsync(l => l.Debit - l.Credit, ct),
            PreviouslyReconciledBalance = await lines.Where(l => l.BankReconciliationId != null).SumAsync(l => l.Debit - l.Credit, ct),
        };
    }

    public Task<BankReconciliationDto> CreateAsync(CreateBankReconciliationDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var account = await BankAccountAsync(r.AccountCode, token);
            var date = r.StatementDate.Date;
            if (date > DateTime.UtcNow.Date.AddDays(1)) throw new ValidationFailedException(Messages.StatementDateInFuture);
            // التسويات متتابعة: لا كشف بتاريخ يسبق آخر كشف مطابَق للحساب
            var last = await _db.Set<BankReconciliation>().AsNoTracking().Where(b => b.AccountCode == account.Code)
                .OrderByDescending(b => b.StatementDate).Select(b => (DateTime?)b.StatementDate).FirstOrDefaultAsync(token);
            if (last.HasValue && date <= last.Value) throw new ConflictException(string.Format(Messages.StatementDateNotAfterLast, last.Value.ToString("yyyy-MM-dd")));

            var all = await LinesThrough(account.Code, date).ToListAsync(token);
            var selectedIds = r.LineIds.ToHashSet();
            var selected = all.Where(l => l.BankReconciliationId == null && selectedIds.Contains(l.Id)).ToList();
            if (selected.Count != selectedIds.Count) throw new ValidationFailedException(Messages.ReconciliationLinesInvalid);

            var reconciledBalance = all.Where(l => l.BankReconciliationId != null).Sum(l => l.Debit - l.Credit) + selected.Sum(l => l.Debit - l.Credit);
            var difference = DocumentPricing.Round(r.StatementBalance - reconciledBalance);
            if (difference != 0) throw new ConflictException(string.Format(Messages.ReconciliationDoesNotBalance, difference.ToString("0.00")));

            var outstanding = all.Where(l => l.BankReconciliationId == null && !selectedIds.Contains(l.Id)).ToList();
            var reconciliation = new BankReconciliation
            {
                AccountCode = account.Code, AccountName = account.NameAr, StatementDate = date, StatementBalance = r.StatementBalance,
                BookBalance = all.Sum(l => l.Debit - l.Credit), OutstandingDeposits = outstanding.Sum(l => l.Debit), OutstandingPayments = outstanding.Sum(l => l.Credit),
                ReconciledLines = selected.Count, Notes = r.Notes, PerformedBy = _user.Name ?? "system",
            };
            _db.Add(reconciliation);
            foreach (var line in selected) line.BankReconciliationId = reconciliation.Id;
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("reconcile", nameof(BankReconciliation), reconciliation.Id.ToString(),
                $"{account.Code} حتى {date:yyyy-MM-dd}: رصيد الكشف {r.StatementBalance:0.00}، {selected.Count} حركة", token);
            return Mapper.Map<BankReconciliationDto>(reconciliation);
        }, ct);

    public async Task<List<BankReconciliationDto>> ListAsync(string? accountCode, CancellationToken ct = default)
    {
        var q = _db.Set<BankReconciliation>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(accountCode)) q = q.Where(b => b.AccountCode == accountCode.Trim());
        return (await q.OrderByDescending(b => b.StatementDate).ThenBy(b => b.AccountCode).ToListAsync(ct)).Select(Mapper.Map<BankReconciliationDto>).ToList();
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var reconciliation = await _db.Set<BankReconciliation>().FirstOrDefaultAsync(b => b.Id == id, token)
                ?? throw new NotFoundException(Messages.BankReconciliationNotFound);
            if (await _db.Set<BankReconciliation>().AnyAsync(b => b.AccountCode == reconciliation.AccountCode && b.StatementDate > reconciliation.StatementDate, token))
                throw new ConflictException(Messages.OnlyLastReconciliationCanBeDeleted);

            foreach (var line in await _db.Set<JournalEntryLine>().Where(l => l.BankReconciliationId == id).ToListAsync(token)) line.BankReconciliationId = null;
            _db.Remove(reconciliation);
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("delete", nameof(BankReconciliation), id.ToString(), $"{reconciliation.AccountCode} حتى {reconciliation.StatementDate:yyyy-MM-dd}", token);
        }, ct);
}
