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

    public JournalService(ErpDbContext db, IAccountingPostingService posting, ITransactionRunner tx, ERP.Core.Contracts.Shared.IAuditService audit)
    {
        _audit = audit;
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
        => EntityLoader.LoadAsync(_db.Set<JournalEntry>().AsNoTracking().Include(e => e.Lines), options, Mapper.Map<JournalEntryDto>, ct,
            EntityLoader.Desc(nameof(JournalEntry.Date)), EntityLoader.Desc(nameof(JournalEntry.EntryNumber)));

    public async Task<JournalEntryDto> GetAsync(Guid id, CancellationToken ct = default)
        => Mapper.Map<JournalEntryDto>(await _db.Set<JournalEntry>().AsNoTracking().Include(e => e.Lines).FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new NotFoundException(Messages.JournalEntryNotFound));

    public Task<JournalEntryDto> CreateManualAsync(CreateJournalEntryDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var result = await _posting.PostAsync(ToRequest(r), token);
            await _audit.LogAsync("JOURNAL_ENTRY_CREATED", nameof(JournalEntry), result.JournalEntryId.ToString(), $"قيد يدوي {result.EntryNumber}", token);
            return await GetAsync(result.JournalEntryId, token);
        }, ct);

    public Task<JournalEntryDto> UpdateManualAsync(Guid id, UpdateJournalEntryDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            await EnsureNotOwnedAsync(id, token);
            await _posting.ReplaceManualAsync(id, ToRequest(r), token);
            var updated = await GetAsync(id, token);
            await _audit.LogAsync("JOURNAL_ENTRY_UPDATED", nameof(JournalEntry), id.ToString(), $"تعديل القيد {updated.EntryNumber}", token);
            return updated;
        }, ct);

    public Task DeleteManualAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            await EnsureNotOwnedAsync(id, token);
            var number = await _db.Set<JournalEntry>().AsNoTracking().Where(e => e.Id == id).Select(e => e.EntryNumber).FirstOrDefaultAsync(token);
            await _posting.DeleteManualAsync(id, token);
            await _audit.LogAsync("JOURNAL_ENTRY_DELETED", nameof(JournalEntry), id.ToString(), $"حذف القيد {number}", token);
        }, ct);

    public Task<JournalEntryDto> ReverseAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            await EnsureNotOwnedAsync(id, token);
            var result = await _posting.ReverseAsync(id, null, token);
            await _audit.LogAsync("JOURNAL_ENTRY_REVERSED", nameof(JournalEntry), id.ToString(), $"عكس القيد بقيد {result.EntryNumber}", token);
            return await GetAsync(result.JournalEntryId, token);
        }, ct);

    /// <summary>قيود يحمل مستندها أرصدة أخرى (إهلاك أصل واقتناؤه واستبعاده، تسوية مخزون، رصيد افتتاحي): تُعدَّل وتُعكس من مستندها ليعود أثرها كاملاً.</summary>
    private static readonly string[] OwnedSources = { "fixed_asset_depreciation", "fixed_asset_disposal", "fixed_asset_acquisition", "stock_adjustment", "opening_balance", "payroll" };

    private async Task EnsureNotOwnedAsync(Guid id, CancellationToken ct)
    {
        var source = await _db.Set<JournalEntry>().AsNoTracking().Where(e => e.Id == id).Select(e => e.SourceType).FirstOrDefaultAsync(ct);
        if (source != null && OwnedSources.Contains(source)) throw new ConflictException(Messages.JournalEntryOwnedByDocument);
    }

    private static GenericPostingRequest ToRequest(CreateJournalEntryDto r) => new()
    {
        Date = r.Date == default ? DateTime.UtcNow : r.Date,
        Description = string.IsNullOrWhiteSpace(r.Description) ? "قيد يومية" : r.Description,
        SourceType = "manual",
        Lines = r.Lines.Select(l => new PostingLine(l.AccountCode, l.Debit, l.Credit, l.Notes, l.CostCenterId)).ToList(),
    };
}
