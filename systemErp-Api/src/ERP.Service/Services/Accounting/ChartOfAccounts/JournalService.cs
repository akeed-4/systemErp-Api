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

    /// <summary>أعمدة الفرز المسموحة (أسماء حقول JournalEntryDto).</summary>
    private static readonly Dictionary<string, System.Linq.Expressions.Expression<Func<JournalEntry, object>>> SortColumns = new()
    {
        ["entryNumber"] = e => e.EntryNumber, ["date"] = e => e.Date, ["description"] = e => e.Description,
        ["referenceType"] = e => e.ReferenceType!, ["referenceNumber"] = e => e.ReferenceNumber!,
        ["totalDebit"] = e => e.TotalDebit, ["totalCredit"] = e => e.TotalCredit, ["status"] = e => e.Status,
    };

    /// <summary>مرشّحات القائمة والملخص (مصدر واحد). referenceType مطابقة تامة (مثل manual لقيود اليومية اليدوية).</summary>
    private IQueryable<JournalEntry> Filtered(PaginationParams p, string? referenceType)
    {
        var q = _db.Set<JournalEntry>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(referenceType))
        {
            var rt = referenceType.Trim();
            // القيد بلا نوع مرجع يُعدّ يدويًا (كما تعرضه الواجهة)
            q = string.Equals(rt, "manual", StringComparison.OrdinalIgnoreCase)
                ? q.Where(e => e.ReferenceType == null || e.ReferenceType == "" || e.ReferenceType == "manual")
                : q.Where(e => e.ReferenceType == rt);
        }
        if (p.StartDate.HasValue) q = q.Where(e => e.Date >= p.StartDate);
        if (ListQuery.EndExclusive(p) is { } end) q = q.Where(e => e.Date < end);
        if (!string.IsNullOrWhiteSpace(p.Status) && Enum.TryParse<JournalEntryStatus>(p.Status, true, out var st)) q = q.Where(e => e.Status == st);
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var t = p.SearchTerm.Trim();
            q = q.Where(e => e.EntryNumber.Contains(t) || e.Description.Contains(t) || (e.ReferenceNumber != null && e.ReferenceNumber.Contains(t)));
        }
        return q;
    }

    /// <summary>إجماليات كامل النتيجة المرشّحة (الصيغ في <see cref="JournalEntrySummaryDto"/>).</summary>
    public async Task<JournalEntrySummaryDto> SummaryAsync(PaginationParams p, string? referenceType = null, CancellationToken ct = default)
        => await Filtered(p, referenceType).GroupBy(_ => 1).Select(g => new JournalEntrySummaryDto
        {
            Count = g.Count(),
            TotalDebit = g.Sum(e => e.TotalDebit), TotalCredit = g.Sum(e => e.TotalCredit),
            UnbalancedCount = g.Count(e => e.TotalDebit != e.TotalCredit),
        }).FirstOrDefaultAsync(ct) ?? new JournalEntrySummaryDto();

    public async Task<PagedResult<JournalEntryDto>> ListAsync(PaginationParams p, string? referenceType = null, CancellationToken ct = default)
    {
        var q = Filtered(p, referenceType);
        var total = await q.CountAsync(ct);
        var ordered = ListQuery.Sort(q, p, SortColumns,
            x => x.OrderByDescending(e => e.Date).ThenByDescending(e => e.EntryNumber).ThenByDescending(e => e.Id), e => e.Id);
        var items = await ordered.Include(e => e.Lines)
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
