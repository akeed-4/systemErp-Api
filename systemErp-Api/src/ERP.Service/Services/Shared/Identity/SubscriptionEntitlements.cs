using ERP.Core.Contracts.Shared;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

public class SubscriptionEntitlements : ISubscriptionEntitlements
{
    private readonly ErpDbContext _db;
    private EntitlementInfo? _cached;
    public SubscriptionEntitlements(ErpDbContext db) => _db = db;

    public async Task<EntitlementInfo> GetAsync(CancellationToken ct = default)
    {
        if (_cached != null) return _cached;
        var sub = await _db.Set<Subscription>().AsNoTracking().OrderByDescending(s => s.StartDate).ThenByDescending(s => s.CreatedAt).FirstOrDefaultAsync(ct);
        if (sub == null) return _cached = new EntitlementInfo(false, SubscriptionStatus.Expired, null, Array.Empty<string>());
        var status = sub.Status is SubscriptionStatus.Active or SubscriptionStatus.Trial && sub.ExpiryDate < DateTime.UtcNow ? SubscriptionStatus.Expired : sub.Status;
        return _cached = new EntitlementInfo(true, status, sub.ExpiryDate, PlatformModules.Parse(sub.ModuleKeys));
    }
}
