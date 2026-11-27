using ERP.Core.DTOs.Accounting;
using ERP.Core.Models.Shared;

namespace ERP.Core.DTOs.Shared;

// =====================================================================
// مركز مراقبة المنشأة (/command-center). كل الأرقام من مصادرها المعتمدة في الخادم؛ لا تقدير ولا بيانات وهمية.
// =====================================================================

/// <summary>المؤشرات المالية والتجارية للفترة: من خدمتي لوحة المؤشرات والإحصاءات المالية نفسيهما (لا حساب مكرر).</summary>
public class CommandCenterOverviewDto
{
    public string Period { get; set; } = "month";
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public SalesPerformanceTotalsDto Current { get; set; } = new();
    public SalesPerformanceTotalsDto Previous { get; set; } = new();
    public decimal CashAndBankBalance { get; set; }
    public decimal ReceivablesBalance { get; set; }
    public decimal PayablesBalance { get; set; }
    public decimal InventoryValuation { get; set; }
    public DateTime GeneratedAt { get; set; }
}

/// <summary>النبض التشغيلي (خفيف، للتحديث الدوري): عمليات اليوم والتنبيهات المفتوحة وحالة الهيئة وآخر فحص.</summary>
public class CommandCenterLiveDto
{
    public DateTime GeneratedAt { get; set; }
    /// <summary>بداية اليوم بتوقيت المنشأة (UTC).</summary>
    public DateTime DayStart { get; set; }
    public int SalesInvoicesToday { get; set; }
    public int PurchaseInvoicesToday { get; set; }
    public int ReturnsToday { get; set; }
    public int JournalEntriesToday { get; set; }
    public int StockMovementsToday { get; set; }
    public int AuditEventsToday { get; set; }
    /// <summary>مستخدمون فعّالون دخلوا خلال 24 ساعة.</summary>
    public int ActiveUsers24h { get; set; }
    public int TotalActiveUsers { get; set; }
    public int FailedLogins24h { get; set; }
    public CommandCenterZatcaSummaryDto Zatca { get; set; } = new();
    public Dictionary<string, int> OpenAlertsBySeverity { get; set; } = new();
    public MonitoringCheckRunDto? LastCheck { get; set; }
}

/// <summary>
/// حالة الإرسال للهيئة. المستند غير الخاضع للإرسال (المشتريات، هامش الربح، الربط غير مفعّل) لا يُعدّ فاشلاً ولا معلقاً.
/// </summary>
public class CommandCenterZatcaSummaryDto
{
    public bool Enabled { get; set; }
    public int Cleared { get; set; }
    public int Reported { get; set; }
    public int AcceptedWithWarnings { get; set; }
    public int Rejected { get; set; }
    /// <summary>فواتير بيع مرحّلة لم تُرسل بعد (خاضعة للإرسال).</summary>
    public int Pending { get; set; }
    /// <summary>منها مبسطة تجاوزت 24 ساعة من إصدارها.</summary>
    public int PendingOverdue { get; set; }
    /// <summary>محاولات الإرسال في الفترة حسب النتيجة.</summary>
    public Dictionary<string, int> AttemptsByOutcome { get; set; } = new();
}

/// <summary>صف سجل محاولات الإرسال للهيئة (بلا XML ولا أسرار).</summary>
public class ZatcaAttemptRowDto
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string DocumentKind { get; set; } = string.Empty;
    public Guid? DocumentUuid { get; set; }
    public long? Icv { get; set; }
    public string? UserName { get; set; }
    public DateTime AttemptedAt { get; set; }
    public int DurationMs { get; set; }
    public ZatcaAttemptOutcome Outcome { get; set; }
    public int? HttpStatus { get; set; }
    public string? IssueCodes { get; set; }
    public string? Message { get; set; }
    public bool IsResubmission { get; set; }
}

/// <summary>صف نشاط التدقيق (قراءة فقط).</summary>
public class AuditActivityRowDto
{
    public Guid Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string PerformedBy { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public string Details { get; set; } = string.Empty;
}

/// <summary>صف محاولة دخول (المعرّف مقنَّع).</summary>
public class LoginEventRowDto
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public string? TenantName { get; set; }
    public Guid? UserId { get; set; }
    public string Identifier { get; set; } = string.Empty;
    public LoginOutcome Outcome { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime OccurredAt { get; set; }
}

/// <summary>تنبيه مخزني من بيانات الأصناف الحالية (رصيد سالب أو تحت حد الطلب).</summary>
public class InventoryAlertRowDto
{
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal CurrentStock { get; set; }
    public decimal MinStockLevel { get; set; }
    /// <summary>negative_stock | below_min</summary>
    public string Kind { get; set; } = string.Empty;
}

// ---------- التنبيهات والفحص ----------

public class MonitoringCheckRunDto
{
    public Guid Id { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string RunByName { get; set; } = string.Empty;
    public int AlertsOpened { get; set; }
    public int AlertsCleared { get; set; }
    /// <summary>نتيجة كل قاعدة: عدد ما اكتُشف، أو -1 = لا ينطبق على المنشأة.</summary>
    public Dictionary<string, int> Rules { get; set; } = new();
    public string? Error { get; set; }
}

public class MonitoringAlertRowDto
{
    public Guid Id { get; set; }
    public string RuleKey { get; set; } = string.Empty;
    public AlertSeverity Severity { get; set; }
    public string Module { get; set; } = string.Empty;
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public string? SourceNumber { get; set; }
    public string? ParamsJson { get; set; }
    public bool CauseConfirmed { get; set; }
    public AlertStatus Status { get; set; }
    public bool ConditionActive { get; set; }
    public DateTime FirstDetectedAt { get; set; }
    public DateTime LastDetectedAt { get; set; }
    public int OccurrenceCount { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public string? AssignedToName { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedByName { get; set; }
}

public class MonitoringAlertEventDto
{
    public Guid Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime At { get; set; }
}

public class MonitoringAlertDetailsDto
{
    public MonitoringAlertRowDto Alert { get; set; } = new();
    public List<MonitoringAlertEventDto> Events { get; set; } = new();
}

/// <summary>إجراء على تنبيه: acknowledge | assign | start | note | resolve | close | reopen.</summary>
public class MonitoringAlertActionRequestDto
{
    public string Action { get; set; } = string.Empty;
    public string? Note { get; set; }
    /// <summary>للتعيين: المستخدم المسؤول (من مستخدمي المنشأة).</summary>
    public Guid? AssigneeUserId { get; set; }
}
