namespace ERP.Core.Models.Accounting;

// ---------- الفحص والتشخيص ----------

public static class InspectionStatuses
{
    public const string Draft = "draft";
    public const string Completed = "completed";
}

/// <summary>نتيجة بند الحالة: سليم، يحتاج انتباهاً، فشل.</summary>
public static class InspectionConditions
{
    public const string Ok = "ok";
    public const string Attention = "attention";
    public const string Fail = "fail";
    public static bool IsValid(string? v) => v is Ok or Attention or Fail;
}

/// <summary>
/// فحص على أمر العمل (استقبال، تشخيص، جودة) من قالب: البنود لقطة من القالب عند الإنشاء (تعديل القالب لا يمس الفحوص المسجلة). مسودة تُعبَّأ
/// ثم تُكمَّل؛ المكتمل لا يُعدَّل.
/// </summary>
public class WorkOrderInspection : BaseEntity
{
    public Guid WorkOrderId { get; set; }
    public Guid? TemplateId { get; set; }
    public string Usage { get; set; } = InspectionUsages.Reception;
    public string TemplateName { get; set; } = string.Empty;
    public string Status { get; set; } = InspectionStatuses.Draft;
    public string? InspectorName { get; set; }
    public Guid? InspectorUserId { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Summary { get; set; }
    public string? CreatedBy { get; set; }
    public virtual ICollection<WorkOrderInspectionResult> Results { get; set; } = new List<WorkOrderInspectionResult>();
}

/// <summary>نتيجة بند فحص بلقطة نصه ونوعه، وصوره مرفقات بمالك <c>work_order_inspection_result</c>.</summary>
public class WorkOrderInspectionResult : BaseEntity
{
    public Guid InspectionId { get; set; }
    public Guid? TemplateItemId { get; set; }
    public string? Section { get; set; }
    public string TextAr { get; set; } = string.Empty;
    public string? TextEn { get; set; }
    public string ResultType { get; set; } = InspectionResultTypes.Condition;
    public bool PhotoRequiredOnFail { get; set; }
    public string? Condition { get; set; }
    public decimal? NumberValue { get; set; }
    public string? TextValue { get; set; }
    public string? Note { get; set; }
    public int SortOrder { get; set; }
    public virtual WorkOrderInspection Inspection { get; set; } = null!;
}

// ---------- الأسطر ----------

/// <summary>نوع سطر أمر العمل: عمالة (خدمة معرّفة)، قطعة (صنف مخزني)، خدمة خارجية، رسوم.</summary>
public static class WorkOrderLineKinds
{
    public const string Labor = "labor";
    public const string Part = "part";
    public const string External = "external";
    public const string Fee = "fee";
    public static bool IsValid(string? v) => v is Labor or Part or External or Fee;
}

/// <summary>موافقة العميل على السطر.</summary>
public static class LineApprovalStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Deferred = "deferred";
    public static bool IsDecision(string? v) => v is Approved or Rejected or Deferred;
}

/// <summary>على من يُحمَّل السطر: العميل، الضمان، داخلي، مجاملة (الثلاثة الأخيرة بلا مبلغ على العميل).</summary>
public static class LineBillingTypes
{
    public const string Customer = "customer";
    public const string Warranty = "warranty";
    public const string Internal = "internal";
    public const string Goodwill = "goodwill";
    public static bool IsValid(string? v) => v is Customer or Warranty or Internal or Goodwill;
}

public static class LineExecutionStatuses
{
    public const string NotStarted = "not_started";
    public const string InProgress = "in_progress";
    public const string Done = "done";
    public static bool IsValid(string? v) => v is NotStarted or InProgress or Done;
}

/// <summary>
/// سطر أمر العمل: صنفه (خدمي للعمالة والخارجي والرسوم، مخزني للقطعة) ومنه الضريبة والإيراد عند الفوترة، والكمية والسعر والخصم قبل الضريبة،
/// وموافقة العميل والتقدير الذي وافق فيه، ونوع التحميل، وحالة التنفيذ، والتكلفة التقديرية (للربحية). الموافق مقفل السعر والكمية.
/// </summary>
public class WorkOrderLine : BaseEntity
{
    public Guid WorkOrderId { get; set; }
    public string Kind { get; set; } = WorkOrderLineKinds.Labor;
    public Guid? ServiceId { get; set; }
    public Guid ProductId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    /// <summary>السعر مُدخل يدوياً بخلاف سعر الخدمة أو الصنف (يتطلب «خصم يدوي»).</summary>
    public bool PriceOverridden { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal VatRate { get; set; }
    public string ApprovalStatus { get; set; } = LineApprovalStatuses.Pending;
    public Guid? ApprovedEstimateId { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string BillingType { get; set; } = LineBillingTypes.Customer;
    public string ExecutionStatus { get; set; } = LineExecutionStatuses.NotStarted;
    public decimal EstimatedCost { get; set; }
    /// <summary>أُضيف بعد أن اعتُمد الأمر (يحتاج تقديراً لاحقاً).</summary>
    public bool AddedAfterApproval { get; set; }
    /// <summary>نتيجة الفحص التي اقترحته.</summary>
    public Guid? SourceResultId { get; set; }
    public string? Notes { get; set; }
    public int SortOrder { get; set; }
    public string? CreatedBy { get; set; }
}

// ---------- التقديرات ----------

public static class EstimateStatuses
{
    public const string Draft = "draft";
    public const string Sent = "sent";
    public const string Responded = "responded";
    public const string Superseded = "superseded";
}

/// <summary>قناة رد العميل (O13: داخلية مسجلة).</summary>
public static class EstimateChannels
{
    public const string InPerson = "in_person";
    public const string Phone = "phone";
    public const string WhatsApp = "whatsapp";
    public const string Email = "email";
    public static bool IsValid(string? v) => v is InPerson or Phone or WhatsApp or Email;
}

/// <summary>
/// نسخة تقدير مجمّدة لأمر العمل (D3): لقطة أسطرها المعروضة للقرار وإجمالياتها، ثم رد العميل لكل سطر بقناته واسم من رد. نسخة جديدة تستبدل
/// السابقة غير المجابة.
/// </summary>
public class WorkOrderEstimate : BaseEntity
{
    public Guid WorkOrderId { get; set; }
    public int Version { get; set; }
    public string Status { get; set; } = EstimateStatuses.Draft;
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal Total { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public string? ResponseChannel { get; set; }
    public string? RespondedByName { get; set; }
    public string? ResponseNotes { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public string? RecordedBy { get; set; }
    public virtual ICollection<WorkOrderEstimateLine> Lines { get; set; } = new List<WorkOrderEstimateLine>();
}

/// <summary>لقطة سطر في نسخة التقدير وقرار العميل عليه.</summary>
public class WorkOrderEstimateLine : BaseEntity
{
    public Guid EstimateId { get; set; }
    public Guid LineId { get; set; }
    public string Kind { get; set; } = WorkOrderLineKinds.Labor;
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal VatRate { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string BillingType { get; set; } = LineBillingTypes.Customer;
    /// <summary>القرار المسجل (فارغ حتى الرد).</summary>
    public string? Decision { get; set; }
    public int SortOrder { get; set; }
    public virtual WorkOrderEstimate Estimate { get; set; } = null!;
}

/// <summary>
/// استثناء موثق لتنفيذ سطر لم يوافق عليه العميل: السبب ومن خوّله (شاشة مقيدة)، ومرفق اختياري من مرفقات الأمر. يُسجَّل ولا يُعدَّل.
/// </summary>
public class WorkOrderExecutionException : BaseEntity
{
    public Guid WorkOrderId { get; set; }
    public Guid LineId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? AttachmentId { get; set; }
    public string? AuthorizedBy { get; set; }
    public Guid? AuthorizedByUserId { get; set; }
    public DateTime AuthorizedAt { get; set; }
}
