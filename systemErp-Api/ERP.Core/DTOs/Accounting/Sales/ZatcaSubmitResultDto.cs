using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public class ZatcaSubmitResultDto
{
    public bool Success { get; set; }
    public ZatcaSubmissionStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? QrCode { get; set; }
    /// <summary>حالات الإرسال مشروحة من قاموس أخطاء الهيئة: فحص محلي، أو رمز HTTP، أو رسالة تحقق.</summary>
    public List<ZatcaIssueDto> Issues { get; set; } = new();
}

/// <summary>حالة من قاموس أخطاء الهيئة: ما حدث، وكيف يُعالَج، وهل استُهلك عدّاد الفاتورة (ICV) في السلسلة.</summary>
public class ZatcaIssueDto
{
    /// <summary>رمز الحالة: مفتاح الفحص المحلي (NoActiveUnit…) أو «HTTP 503» أو رمز رسالة التحقق (BR-KSA-…).</summary>
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Fix { get; set; } = string.Empty;
    /// <summary>block | warning | retry</summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>وصلت الفاتورة للهيئة واستُهلك عدّادها؛ وإلا يُعاد الإرسال بالعدّاد والبصمة نفسيهما.</summary>
    public bool ChainConsumed { get; set; }
    /// <summary>نص الهيئة الأصلي (إن وُجد).</summary>
    public string? Message { get; set; }
}
