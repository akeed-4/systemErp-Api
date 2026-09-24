using ERP.Core.Contracts.POS;
using ERP.Core.DTOs.POS;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.POS;

public class PosLoyaltyService : IPosLoyaltyService
{
    private readonly ErpDbContext _db;
    public PosLoyaltyService(ErpDbContext db) => _db = db;

    public async Task<PagedResult<CustomerLoyaltyDto>> ListAsync(PaginationParams p, CancellationToken ct = default)
    {
        var q = _db.Set<CustomerLoyalty>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var t = p.SearchTerm.Trim();
            q = q.Where(l => l.CustomerName.Contains(t) || l.Phone.Contains(t));
        }
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(l => l.PointsBalance).Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);
        return new PagedResult<CustomerLoyaltyDto>
        {
            Items = items.Select(Mapper.Map<CustomerLoyaltyDto>).ToList(),
            TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    public async Task<CustomerLoyaltyDto> GetAsync(Guid id, CancellationToken ct = default)
        => Mapper.Map<CustomerLoyaltyDto>(await _db.Set<CustomerLoyalty>().AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NotFoundException("رصيد الولاء غير موجود"));

    public async Task<CustomerLoyaltyDto?> GetByCustomerAsync(Guid customerId, CancellationToken ct = default)
    {
        var l = await _db.Set<CustomerLoyalty>().AsNoTracking().FirstOrDefaultAsync(x => x.CustomerId == customerId, ct);
        return l == null ? null : Mapper.Map<CustomerLoyaltyDto>(l);
    }
}
