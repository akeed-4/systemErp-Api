using ERP.Core.DTOs.Accounting.Sales;

namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// تشخيص محلي مؤقت لمسار ما بعد إصدار شهادة الامتثال، ينفَّذ داخل عملية الخادم نفسها: مفتاح تجريبي، شهادة محلية
/// موقّعة من جهة إصدار تجريبية (ليست من الهيئة)، تحليلها، التشفير وفكّه، توليد مستندات الامتثال وتوقيعها والتحقق منها،
/// وتوافق مكتبات التشفير في البيئة. لا اتصال خارجي، لا قاعدة بيانات، ولا بيانات منشآت.
/// </summary>
public interface IZatcaLocalSelfTest
{
    /// <summary>تشغيل واحد في كل مرة على مستوى العملية؛ تشغيل متزامن يُرفض بتعارض.</summary>
    Task<ZatcaSelfTestReportDto> RunAsync(CancellationToken ct = default);
}
