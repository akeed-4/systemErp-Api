using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

public class SubscriptionService : ISubscriptionService
{
    private readonly ErpDbContext _db;
    public SubscriptionService(ErpDbContext db) => _db = db;

    public async Task<List<SubscriptionPlanDto>> GetPlansAsync(CancellationToken ct = default)
        => (await _db.Set<PlanDefinition>().AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.SortOrder).ToListAsync(ct))
            .Select(SubscriptionCatalog.ToDto).ToList();

    public async Task<SubscriptionDto?> GetCurrentAsync(CancellationToken ct = default)
    {
        var sub = await _db.Set<Subscription>().AsNoTracking().OrderByDescending(s => s.StartDate).FirstOrDefaultAsync(ct);
        return sub == null ? null : SubscriptionCatalog.ToDto(sub);
    }
}
