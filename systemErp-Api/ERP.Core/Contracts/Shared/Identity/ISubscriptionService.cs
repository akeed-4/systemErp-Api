using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface ISubscriptionService
{
    /// <summary>الباقات المتاحة للبيع (الفعّالة فقط) مرتبة.</summary>
    Task<List<SubscriptionPlanDto>> GetPlansAsync(CancellationToken ct = default);
    Task<SubscriptionDto?> GetCurrentAsync(CancellationToken ct = default);
}
