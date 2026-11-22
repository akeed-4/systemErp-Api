using DevExtreme.AspNet.Data.ResponseModel;
using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using ERP.Service.Services.Shared.Text;

namespace ERP.Service.Services.Accounting;

public class JournalService : IJournalService
{
    private readonly ErpDbContext _db;
    private readonly IAccountingPostingService _posting;
    private readonly ITransactionRunner _tx;
    private readonly ERP.Core.Contracts.Shared.IAuditService _audit;
    private readonly IFiscalPeriodService _periods;

    public JournalService(ErpDbContext db, IAccountingPostingService posting, ITransactionRunner tx, ERP.Core.Contracts.Shared.IAuditService audit,
        IFiscalPeriodService periods)
    {
        _audit = audit; _periods = periods;
        _db = db; _posting = posting; _tx = tx;
    }

    public async Task<PagedResult<JournalEntryDto>> ListAsync(PaginationParams p, CancellationToken ct = default)
    {
        var q = _db.Set<JournalEntry>().AsNoTracking().AsQueryable();
        if (p.StartDate.HasValue) q = q.Where(e => e.Date >= p.StartDate);
        if (p.EndDate.HasValue) q = q.Where(e => e.Date <= p.EndDate);
        if (!string.IsNullOrWhiteSpace(p.Status) && Enum.TryParse<JournalEntryStatus>(p.Status, true, out var st)) q = q.Where(e => e.Status == st);
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var t = p.SearchTerm.Trim();
            q = q.Where(e => e.EntryNumber.Contains(t) || e.Description.Contains(t) || (e.ReferenceNumber != null && e.ReferenceNumber.Contains(t)));
        }
        var total = await q.CountAsync(ct);
        var items = await q.Include(e => e.Lines).OrderByDescending(e => e.Date).ThenByDescending(e => e.EntryNumber)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);
        return new PagedResult<JournalEntryDto>
        {
            Items = items.Select(Mapper.Map<JournalEntryDto>).ToList(),
            TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    /// <summary>القائمة بخيارات DevExtreme: الفلترة والفرز والترقيم في SQL.</summary>
    public Task<LoadResult> LoadAsync(DataSourceLoadOptions options, CancellationToken ct = default)
        => EntityLoader.LoadAsync(_db.Set<JournalEntry>().AsNoTracking().Include(e => e.Lines), options, ToDtosAsync, ct,
            EntityLoader.Desc(nameof(JournalEntry.Date)), EntityLoader.Desc(nameof(JournalEntry.EntryNumber)));

    public async Task<JournalEntryDto> GetAsync(Guid id, CancellationToken ct = default)
        => (await ToDtosAsync(new List<JournalEntry> { await _db.Set<JournalEntry>().AsNoTracking().Include(e => e.Lines).FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new NotFoundException(Messages.JournalEntryNotFound) }, ct))[0];

    /// <summary>القيود مع رقم قيدها العكسي (للمعكوس منها) باستعلام واحد للصفحة.</summary>
    private async Task<List<JournalEntryDto>> ToDtosAsync(List<JournalEntry> page, CancellationToken ct)
    {
        var reversedIds = page.Where(e => e.Status == JournalEntryStatus.Reversed).Select(e => (Guid?)e.Id).ToList();
        var byOriginal = reversedIds.Count == 0 ? new Dictionary<Guid, string>()
            : (await _db.Set<JournalEntry>().AsNoTracking()
                .Where(e => e.SourceType == "reversal" && reversedIds.Contains(e.SourceReferenceId))
                .Select(e => new { Original = e.SourceReferenceId!.Value, e.EntryNumber }).ToListAsync(ct))
                .GroupBy(x => x.Original).ToDictionary(g => g.Key, g => g.First().EntryNumber);
        return page.Select(e =>
        {
            var dto = Mapper.Map<JournalEntryDto>(e);
            dto.ReversedByEntryNumber = byOriginal.GetValueOrDefault(e.Id);
            return dto;
        }).ToList();
    }

    public Task<JournalEntryDto> CreateManualAsync(CreateJournalEntryDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var request = ToRequest(r);
            request.UserEntered = true; request.ManualNumber = r.EntryNumber; // قيد يدوي: رقمه يدوي إن ضُبط نوع القيود يدوياً
            var result = await _posting.PostAsync(request, token);
            await _audit.LogAsync("JOURNAL_ENTRY_CREATED", nameof(JournalEntry), result.JournalEntryId.ToString(), $"قيد يدوي {result.EntryNumber}", token);
            return await GetAsync(result.JournalEntryId, token);
        }, ct);

    public Task<JournalEntryDto> UpdateManualAsync(Guid id, UpdateJournalEntryDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            await _posting.ReplaceManualAsync(id, ToRequest(r), token);
            var updated = await GetAsync(id, token);
            await _audit.LogAsync("JOURNAL_ENTRY_UPDATED", nameof(JournalEntry), id.ToString(), $"تعديل القيد {updated.EntryNumber}", token);
            return updated;
        }, ct);

    /// <summary>
    /// حذف نهائي للقيد اليدوي (لا قيد عكسي ولا أرشفة). قيد الإقفال لا يُحذف: يُعكس فتُعاد السنة. سجل التدقيق يحفظ صورة القيد المحذوف
    /// (رقمه وتاريخه وأسطره) لأن القيد نفسه يختفي، ولا تُمس سجلات التدقيق السابقة.
    /// </summary>
    public Task DeleteManualAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var entry = await _db.Set<JournalEntry>().AsNoTracking().Include(e => e.Lines).FirstOrDefaultAsync(e => e.Id == id, token)
                ?? throw new NotFoundException(Messages.JournalEntryNotFound);
            if (entry.SourceType == FiscalPeriodService.ClosingSource) throw new ConflictException(Messages.ClosingEntryCannotBeDeleted);
            await _posting.PurgeManualAsync(id, token);
            var lines = string.Join("؛ ", entry.Lines.Select(l => $"{l.AccountCode} م{l.Debit:0.##} د{l.Credit:0.##}"));
            await _audit.LogAsync("JOURNAL_ENTRY_DELETED", nameof(JournalEntry), id.ToString(),
                $"حذف نهائي للقيد {entry.EntryNumber} بتاريخ {entry.Date:yyyy-MM-dd} ({entry.Description}) بإجمالي {entry.TotalDebit:0.##}: {lines}", token);
        }, ct);

    public Task<JournalEntryDto> ReverseAsync(Guid id, string? reason, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var entry = await SourceOfAsync(id, token);
            if (entry.SourceType == FiscalPeriodService.ClosingSource)
                return await GetAsync((await _periods.ReopenYearAsync(id, token)).JournalEntryId, token);
            // القيد الآلي يُعكس من مستنده (مرتجع الفاتورة، حذف السند...) ليعود أثره كاملاً في المخزون والذمم
            if (!AccountingPostingService.IsManual(entry.SourceType)) throw new ConflictException(Messages.JournalEntryOwnedByDocument);
            var why = reason?.Trim();
            if (string.IsNullOrEmpty(why)) throw new ValidationFailedException(Messages.ReversalReasonRequired);
            var result = await _posting.ReverseAsync(id, why, token);
            await _audit.LogAsync("JOURNAL_ENTRY_REVERSED", nameof(JournalEntry), id.ToString(),
                $"عكس القيد {entry.EntryNumber} بالقيد {result.EntryNumber}؛ السبب: {why}", token);
            return await GetAsync(result.JournalEntryId, token);
        }, ct);

    /// <summary>
    /// مصدر القيد. عكس قيد الإقفال أو إلغاؤه = إعادة فتح سنته (FiscalPeriodService.ReopenYearAsync): يُعكس بتاريخه في السنة نفسها،
    /// لا بتاريخ اليوم فيدخل صافي السنة المقفلة في قائمة دخل سنة أخرى.
    /// </summary>
    private async Task<(string? SourceType, string EntryNumber)> SourceOfAsync(Guid id, CancellationToken ct)
    {
        var entry = await _db.Set<JournalEntry>().AsNoTracking().Where(e => e.Id == id).Select(e => new { e.SourceType, e.EntryNumber }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.JournalEntryNotFound);
        return (entry.SourceType, entry.EntryNumber);
    }

    private static GenericPostingRequest ToRequest(CreateJournalEntryDto r) => new()
    {
        Module = r.Module,
        Date = r.Date == default ? SaudiTime.Today : r.Date,
        Description = string.IsNullOrWhiteSpace(r.Description) ? "قيد يومية" : r.Description,
        SourceType = "manual",
        Lines = r.Lines.Select(l => new PostingLine(l.AccountCode, l.Debit, l.Credit, l.Notes, l.CostCenterId)).ToList(),
    };
}
