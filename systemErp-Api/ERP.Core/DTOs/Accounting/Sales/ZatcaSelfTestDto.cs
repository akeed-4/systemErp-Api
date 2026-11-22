namespace ERP.Core.DTOs.Accounting.Sales;

/// <summary>
/// نتيجة التشخيص المحلي. لا تحمل مفاتيح ولا شهادات ولا Stack Trace (تُسجَّل في ILogger فقط)، ولا بيانات منشآت.
/// </summary>
public class ZatcaSelfTestReportDto
{
    public Guid RunId { get; set; }
    public DateTime StartedAt { get; set; }
    public long TotalMs { get; set; }
    public bool AllPassed { get; set; }
    /// <summary>أول مرحلة فشلت (إن وُجدت): موضع التحقيق التالي.</summary>
    public string? FirstFailedStage { get; set; }
    public List<ZatcaSelfTestStageDto> Stages { get; set; } = new();
    /// <summary>معلومات بيئة آمنة (إصدارات، نظام، ثقافة، منطقة زمنية، توفر خوارزميات) للمقارنة بين المحلي والإنتاج.</summary>
    public Dictionary<string, string> Environment { get; set; } = new();
}

public class ZatcaSelfTestStageDto
{
    public string Name { get; set; } = string.Empty;
    /// <summary>passed | failed | skipped.</summary>
    public string Result { get; set; } = string.Empty;
    public long ElapsedMs { get; set; }
    public string? Detail { get; set; }
    public string? ExceptionType { get; set; }
    public string? ExceptionMessage { get; set; }
    /// <summary>الأسباب الداخلية (النوع والرسالة) بلا Stack Trace.</summary>
    public List<string> InnerExceptions { get; set; } = new();
}
