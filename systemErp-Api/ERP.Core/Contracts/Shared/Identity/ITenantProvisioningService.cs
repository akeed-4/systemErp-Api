using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>يهيّئ بيانات منشأة جديدة: شجرة الحسابات، العملة، المستودع الرئيسي، سياسة التكلفة.</summary>
public interface ITenantProvisioningService
{
    /// <summary>يجب أن يكون سياق المنشأة الحالي مضبوطاً على المنشأة الجديدة قبل الاستدعاء.</summary>
    Task SeedAsync(Guid tenantId, string baseCurrencyCode, CancellationToken ct = default);
}
