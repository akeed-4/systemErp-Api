using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>إدارة الاشتراكات والباقات على مستوى المنصة (كل المنشآت). لمدير المنصة فقط.</summary>
public interface IPlatformSubscriptionService
{
    Task<List<PlatformModuleDto>> ListModulesAsync(CancellationToken ct = default);
    Task<PlatformSummaryDto> SummaryAsync(CancellationToken ct = default);
    Task<List<PlatformTenantSubscriptionDto>> ListTenantsAsync(CancellationToken ct = default);
    Task<List<SubscriptionDto>> HistoryAsync(Guid tenantId, CancellationToken ct = default);
    /// <summary>منشأة جديدة باشتراك فعّال ومالك، على القاعدة المشتركة أو بقاعدة مستقلة تُجهَّز لها.</summary>
    Task<PlatformTenantSubscriptionDto> CreateTenantAsync(PlatformCreateTenantRequestDto request, CancellationToken ct = default);
    Task<PlatformSettingsDto> GetSettingsAsync(CancellationToken ct = default);
    /// <summary>يغيّر نوع قاعدة المنشآت الجديدة من التسجيل العام فقط؛ لا يمسّ قاعدة أي منشأة قائمة.</summary>
    Task<PlatformSettingsDto> UpdateSettingsAsync(UpdatePlatformSettingsDto request, CancellationToken ct = default);
    Task<PlatformPaymobSettingsDto> GetPaymobSettingsAsync(CancellationToken ct = default);
    /// <summary>حساب Paymob الذي تُدفع به اشتراكات المنشآت؛ يغلب إعدادات الخادم ويسري على عمليات الدفع التالية فوراً.</summary>
    Task<PlatformPaymobSettingsDto> UpdatePaymobSettingsAsync(UpdatePlatformPaymobSettingsDto request, CancellationToken ct = default);
    /// <summary>إعادة تجهيز قاعدة منشأة مستقلة لم يكتمل تجهيزها (لا يغيّر نوع قاعدة أي منشأة ولا ينقل بياناتها).</summary>
    Task<PlatformTenantSubscriptionDto> ProvisionDatabaseAsync(Guid tenantId, CancellationToken ct = default);
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
