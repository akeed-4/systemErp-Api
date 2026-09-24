using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface ISubscriptionService
{
    List<SubscriptionPlanDto> GetPlans();
    Task<SubscriptionDto?> GetCurrentAsync(CancellationToken ct = default);
    Task<SubscriptionDto> UpgradeAsync(UpgradeSubscriptionRequestDto request, CancellationToken ct = default);
}
