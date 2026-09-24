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

    public JournalService(ErpDbContext db, IAccountingPostingService posting, ITransactionRunner tx)
    {
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

    public async Task<JournalEntryDto> GetAsync(Guid id, CancellationToken ct = default)
        => Mapper.Map<JournalEntryDto>(await _db.Set<JournalEntry>().AsNoTracking().Include(e => e.Lines).FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new NotFoundException("القيد غير موجود"));

    public Task<JournalEntryDto> CreateManualAsync(CreateJournalEntryDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var result = await _posting.PostAsync(ToRequest(r), token);
            return await GetAsync(result.JournalEntryId, token);
        }, ct);

    public Task<JournalEntryDto> UpdateManualAsync(Guid id, UpdateJournalEntryDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            await _posting.ReplaceManualAsync(id, ToRequest(r), token);
            return await GetAsync(id, token);
        }, ct);

    public Task DeleteManualAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(token => _posting.DeleteManualAsync(id, token), ct);

    public Task<JournalEntryDto> ReverseAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var result = await _posting.ReverseAsync(id, null, token);
            return await GetAsync(result.JournalEntryId, token);
        }, ct);

    private static GenericPostingRequest ToRequest(CreateJournalEntryDto r) => new()
    {
        Date = r.Date == default ? DateTime.UtcNow : r.Date,
        Description = string.IsNullOrWhiteSpace(r.Description) ? "قيد يومية" : r.Description,
        SourceType = "manual",
        Lines = r.Lines.Select(l => new PostingLine(l.AccountCode, l.Debit, l.Credit, l.Notes, l.CostCenterId)).ToList(),
    };
}
