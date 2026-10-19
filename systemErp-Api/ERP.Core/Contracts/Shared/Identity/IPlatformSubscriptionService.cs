using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>إدارة الاشتراكات والباقات على مستوى المنصة (كل المنشآت). لمدير المنصة فقط.</summary>
public interface IPlatformSubscriptionService
{
    Task<List<PlatformModuleDto>> ListModulesAsync(CancellationToken ct = default);
    Task<PlatformSummaryDto> SummaryAsync(CancellationToken ct = default);
    Task<List<PlatformTenantSubscriptionDto>> ListTenantsAsync(CancellationToken ct = default);
    Task<List<SubscriptionDto>> HistoryAsync(Guid tenantId, CancellationToken ct = default);
    Task<SubscriptionDto> ChangePlanAsync(Guid tenantId, PlatformChangePlanRequestDto request, CancellationToken ct = default);
    Task<SubscriptionDto> ExtendAsync(Guid tenantId, PlatformExtendRequestDto request, CancellationToken ct = default);
    Task<SubscriptionDto> SetModulesAsync(Guid tenantId, PlatformSetModulesRequestDto request, CancellationToken ct = default);
    Task<SubscriptionDto> SetStatusAsync(Guid tenantId, PlatformSetStatusRequestDto request, CancellationToken ct = default);
    Task<List<PlatformPlanDto>> ListPlansAsync(CancellationToken ct = default);
    /// <summary>باقة جديدة برمز فريد؛ تُعرض للبيع فور إنشائها إن كانت فعّالة.</summary>
    Task<PlatformPlanDto> CreatePlanAsync(CreatePlanRequestDto request, CancellationToken ct = default);
    Task<PlatformPlanDto> UpdatePlanAsync(string id, UpdatePlanRequestDto request, CancellationToken ct = default);
    /// <summary>حذف باقة لم يُشترك فيها قط ولا عليها مدفوعات؛ غير ذلك تُعطَّل بدل الحذف.</summary>
    Task DeletePlanAsync(string id, CancellationToken ct = default);
}
