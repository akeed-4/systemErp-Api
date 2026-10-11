using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Accounting;

public interface IZatcaService
{
    /// <summary>ينشئ رمز QR وفق TLV المرحلة الأولى (Base64) للفاتورة.</summary>
    string GenerateTlvQr(string sellerName, string vatNumber, DateTime timestamp, decimal total, decimal vat);
    /// <summary>
    /// يوقّع الفاتورة المرحّلة بوحدة المستخدم صاحب الجلسة ويرسلها للهيئة: المبسطة تُبلَّغ، والضريبية تُعتمد وتُحفظ
    /// نسختها المختومة من الهيئة. مستخدم غير مربوط لا يرسل.
    /// رفض الهيئة أو تعذّر الاتصال لا يرمي: يُحفظ السبب على الفاتورة وتُعاد النتيجة ليُعاد الإرسال لاحقاً.
    /// </summary>
    Task<ZatcaSubmitResultDto> SubmitInvoiceAsync(Guid invoiceId, CancellationToken ct = default);
    /// <summary>
    /// ربط مستخدم مع الهيئة (المرحلة الثانية): ينشئ مفتاح وحدته وطلب الشهادة، يصدر شهادة الامتثال برمز التحقق،
    /// يجتاز فحوص الامتثال لأنواع الفواتير، ثم يصدر شهادة الإنتاج ويحفظها باسمه. المستخدم يربط نفسه مباشرة،
    /// والمالك ومدير النظام يربطان غيرهما.
    /// </summary>
    Task<TenantDto> OnboardAsync(ZatcaOnboardRequestDto request, CancellationToken ct = default);
    /// <summary>الوحدات المربوطة: كلها للمالك ومدير النظام، ووحدة المستخدم نفسه لغيرهما.</summary>
    Task<List<ZatcaUnitDto>> ListUnitsAsync(CancellationToken ct = default);
    /// <summary>إيقاف وحدة فلا يرسل صاحبها حتى يُعاد ربطه؛ للمالك ومدير النظام أو لصاحب الوحدة.</summary>
    Task DisableUnitAsync(Guid unitId, CancellationToken ct = default);
}
