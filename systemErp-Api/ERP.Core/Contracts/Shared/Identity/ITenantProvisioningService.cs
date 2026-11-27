using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>يهيّئ بيانات منشأة جديدة: شجرة الحسابات (من القوالب)، العملة، الموقع الرئيسي، سياسة التكلفة.</summary>
public interface ITenantProvisioningService
{
    /// <summary>
    /// تركيبة المنشأة القديمة (بلا نشاط): الشجرة الافتراضية كاملة مع حسابات العقود، كما كان التأسيس قبل القوالب.
    /// يجب أن يكون سياق المنشأة الحالي مضبوطاً على المنشأة الجديدة قبل الاستدعاء.
    /// </summary>
    Task SeedAsync(Guid tenantId, string baseCurrencyCode, CancellationToken ct = default);

    /// <summary>
    /// الدليل من القالب الأساسي وقالب النشاط وقوالب الموديولات المفعّلة، ثم بقية البيانات الافتتاحية — كلها في معاملة واحدة
    /// (معاملة المستدعي إن وُجدت): إما دليل مكتمل متحقق منه وإما لا شيء.
    /// </summary>
    Task SeedAsync(Guid tenantId, string baseCurrencyCode, string? activityKey, IEnumerable<string>? modules, CancellationToken ct = default);
}
