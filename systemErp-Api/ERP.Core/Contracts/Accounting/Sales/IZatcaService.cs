using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Accounting.Sales;
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
    /// <summary>حالة الربط واختبارات الامتثال المحفوظة للمستخدم، بلا مفاتيح أو بيانات اعتماد.</summary>
    Task<ZatcaOnboardingStatusDto?> GetOnboardingStatusAsync(Guid? userId = null, CancellationToken ct = default);
    /// <summary>إعادة إرسال آمنة لاختبار امتثال رُفض صراحةً، باستخدام المستند الموقّع نفسه.</summary>
    Task<ZatcaOnboardingStatusDto> RetryComplianceTestAsync(Guid sessionId, Guid testId, CancellationToken ct = default);
    /// <summary>
    /// فحص اتصال الخادم ببوابة الهيئة (DNS ثم TCP ثم TLS ثم طلب HTTPS لا يصدر شهادة) مع زمن كل خطوة وتفاصيل الخطأ.
    /// للمالك ومدير النظام. <paramref name="environment"/> فارغ = بيئة المنشأة الحالية.
    /// </summary>
    Task<ZatcaConnectivityReportDto> CheckConnectivityAsync(ZatcaEnvironment? environment = null, CancellationToken ct = default);
    /// <summary>جلسات الربط العالقة (طلب شهادة جارٍ أو نتيجته مجهولة) مع إمكانية معالجة كل منها. للمالك ومدير النظام.</summary>
    Task<List<ZatcaStuckSessionDto>> ListStuckSessionsAsync(CancellationToken ct = default);
    /// <summary>
    /// يغلق جلسة امتثال عالقة لم يُحفظ لها أي شهادة (لا تُحذف، تبقى للتدقيق) فيُسمح ببدء محاولة جديدة بطلب شهادة
    /// ومفتاح جديدين. مرحلة شهادة الإنتاج لا تُعالج هنا. للمالك ومدير النظام.
    /// </summary>
    Task<ZatcaOnboardingStatusDto> RecoverStuckSessionAsync(Guid sessionId, ZatcaSessionRecoveryRequestDto request, CancellationToken ct = default);
    /// <summary>الوحدات المربوطة: كلها للمالك ومدير النظام، ووحدة المستخدم نفسه لغيرهما.</summary>
    Task<List<ZatcaUnitDto>> ListUnitsAsync(CancellationToken ct = default);
    /// <summary>إيقاف وحدة فلا يرسل صاحبها حتى يُعاد ربطه؛ للمالك ومدير النظام أو لصاحب الوحدة.</summary>
    Task DisableUnitAsync(Guid unitId, CancellationToken ct = default);
}
