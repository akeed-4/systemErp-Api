namespace ERP.Core.Models.Shared;

public enum AlertSeverity
{
    Critical = 1,
    High = 2,
    Medium = 3,
    Low = 4,
    Info = 5,
}

public enum AlertStatus
{
    Open = 1,
    Acknowledged = 2,
    InProgress = 3,
    Resolved = 4,
    Closed = 5,
}

/// <summary>
/// تنبيه تشغيلي للمنشأة من قاعدة فحص معرّفة في الكود (`RuleKey`) على مصدر بيانات يمكن التحقق منه. البصمة فريدة لكل منشأة
/// (القاعدة + المصدر) فلا يتكرر التنبيه للمشكلة نفسها: الفحص التالي يحدّث آخر اكتشاف وعدد المرات، ويعيد فتح المغلق إن عادت.
/// النص يُعرض بلغة المستخدم من مفتاح القاعدة ومعاملاتها (`ParamsJson`)، لا نص محفوظ بلغة واحدة.
/// </summary>
public class MonitoringAlert : BaseEntity
{
    public string RuleKey { get; set; } = string.Empty;
    public AlertSeverity Severity { get; set; }
    /// <summary>sales | purchases | inventory | accounting | zatca | security | system</summary>
    public string Module { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    /// <summary>المستند أو الكيان المرتبط (إن وُجد): نوعه ومعرّفه ورقمه.</summary>
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public string? SourceNumber { get; set; }
    /// <summary>معاملات نص القاعدة (مبالغ، أعداد، فروق) كـ JSON صغير.</summary>
    public string? ParamsJson { get; set; }
    /// <summary>السبب مثبت (true) أم محتمل يحتاج مراجعة (false): الفرق قد يعود لتوقيت ترحيل لم يكتمل.</summary>
    public bool CauseConfirmed { get; set; }
    public AlertStatus Status { get; set; } = AlertStatus.Open;
    /// <summary>الشرط ما زال قائماً في آخر فحص.</summary>
    public bool ConditionActive { get; set; } = true;
    public DateTime FirstDetectedAt { get; set; }
    public DateTime LastDetectedAt { get; set; }
    public int OccurrenceCount { get; set; } = 1;
    public Guid? AssignedToUserId { get; set; }
    public string? AssignedToName { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedByName { get; set; }
}

/// <summary>سجل إجراءات التنبيه (اكتشاف، إقرار، تعيين، بدء، ملاحظة، إغلاق، إعادة فتح). لا يُعدَّل ولا يُحذف.</summary>
public class MonitoringAlertEvent : BaseEntity
{
    public Guid AlertId { get; set; }
    /// <summary>detected | redetected | condition_cleared | acknowledged | assigned | started | note | resolved | closed | reopened</summary>
    public string Action { get; set; } = string.Empty;
    public string? Note { get; set; }
    public Guid? UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime At { get; set; }
}
