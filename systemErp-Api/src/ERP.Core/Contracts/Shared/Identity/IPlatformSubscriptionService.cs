using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>إدارة الاشتراكات والباقات على مستوى المنصة (كل المنشآت). لمدير المنصة فقط.</summary>
public interface IPlatformSubscriptionService
{
    Task<PlatformSummaryDto> SummaryAsync(CancellationToken ct = default);
    Task<List<PlatformTenantSubscriptionDto>> ListTenantsAsync(CancellationToken ct = default);
    Task<List<SubscriptionDto>> HistoryAsync(Guid tenantId, CancellationToken ct = default);
    Task<SubscriptionDto> ChangePlanAsync(Guid tenantId, PlatformChangePlanRequestDto request, CancellationToken ct = default);
    Task<SubscriptionDto> ExtendAsync(Guid tenantId, PlatformExtendRequestDto request, CancellationToken ct = default);
    Task<SubscriptionDto> SetModulesAsync(Guid tenantId, PlatformSetModulesRequestDto request, CancellationToken ct = default);
    Task<SubscriptionDto> SetStatusAsync(Guid tenantId, PlatformSetStatusRequestDto request, CancellationToken ct = default);
    Task<List<PlatformPlanDto>> ListPlansAsync(CancellationToken ct = default);
    Task<PlatformPlanDto> UpdatePlanAsync(SubscriptionPlanId id, UpdatePlanRequestDto request, CancellationToken ct = default);
}
