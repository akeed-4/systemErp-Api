namespace ERP.Core.Models.Shared;

/// <summary>
/// تشغيل يدوي لفحوص مراقبة المنشأة («تشغيل الفحص»): متى ومن ونتيجته، فتُعرض آخر نتيجة دون إعادة الفحص عند فتح اللوحة.
/// </summary>
public class MonitoringCheckRun : BaseEntity
{
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    /// <summary>running | succeeded | failed</summary>
    public string Status { get; set; } = "running";
    public Guid? RunByUserId { get; set; }
    public string RunByName { get; set; } = string.Empty;
    /// <summary>ملخص نتيجة كل قاعدة (JSON): عدد ما اكتُشف أو «لا ينطبق».</summary>
    public string? SummaryJson { get; set; }
    public int AlertsOpened { get; set; }
    public int AlertsCleared { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// تشغيل يدوي لفحوص المنصة (صحة القواعد، أخطاء API، محاولات الدخول) على القاعدة المشتركة وحدها، بلا مرشّح منشأة
/// (لا يقرؤه إلا مدير المنصة). النتيجة لقطة كاملة (JSON) بتنبيهات المنصة المحسوبة.
/// </summary>
public class PlatformCheckRun
{
    public Guid Id { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; } = "running";
    public string RunByName { get; set; } = string.Empty;
    public string? ResultJson { get; set; }
    public string? Error { get; set; }
}
