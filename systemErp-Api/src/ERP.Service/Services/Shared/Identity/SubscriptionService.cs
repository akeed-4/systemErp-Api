using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

public class SubscriptionService : ISubscriptionService
{
    private readonly ErpDbContext _db;
    public SubscriptionService(ErpDbContext db) => _db = db;

    public List<SubscriptionPlanDto> GetPlans() => SubscriptionCatalog.Plans;

    public async Task<SubscriptionDto?> GetCurrentAsync(CancellationToken ct = default)
    {
        var sub = await _db.Set<Subscription>().AsNoTracking().OrderByDescending(s => s.StartDate).FirstOrDefaultAsync(ct);
        return sub == null ? null : Mapper.Map<SubscriptionDto>(sub);
    }

    public async Task<SubscriptionDto> UpgradeAsync(UpgradeSubscriptionRequestDto request, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(request.PlanId)) throw new ValidationFailedException("الباقة غير صالحة.");

        // لا توجد بوابة دفع مُدمجة؛ الاشتراك يُفعَّل مباشرة ويُسجَّل مرجع العملية للمطابقة اليدوية.
        var active = await _db.Set<Subscription>().Where(s => s.Status == SubscriptionStatus.Active).ToListAsync(ct);
        foreach (var s in active) s.Status = SubscriptionStatus.Expired;

        var sub = SubscriptionCatalog.NewSubscription(SubscriptionCatalog.Get(request.PlanId), request.BillingCycle, request.PaymentMethod);
        _db.Add(sub);
        await _db.SaveChangesAsync(ct);
        return Mapper.Map<SubscriptionDto>(sub);
    }
}
