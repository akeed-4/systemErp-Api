namespace ERP.Core.DTOs.Accounting;

// ---------- الفحص ----------

public class CreateInspectionDto
{
    public Guid TemplateId { get; set; }
    public string? InspectorName { get; set; }
}

public class SaveInspectionResultDto
{
    public Guid ResultId { get; set; }
    public string? Condition { get; set; }
    public decimal? NumberValue { get; set; }
    public string? TextValue { get; set; }
    public string? Note { get; set; }
}

public class SaveInspectionResultsDto
{
    public string? InspectorName { get; set; }
    public string? Summary { get; set; }
    public List<SaveInspectionResultDto> Results { get; set; } = new();
}

public class InspectionResultDto
{
    public Guid Id { get; set; }
    public string? Section { get; set; }
    public string TextAr { get; set; } = string.Empty;
    public string? TextEn { get; set; }
    public string ResultType { get; set; } = string.Empty;
    public bool PhotoRequiredOnFail { get; set; }
    public string? Condition { get; set; }
    public decimal? NumberValue { get; set; }
    public string? TextValue { get; set; }
    public string? Note { get; set; }
    public int SortOrder { get; set; }
    public int PhotoCount { get; set; }
    /// <summary>سطر أمر العمل المقترح من هذه النتيجة، إن وُجد.</summary>
    public Guid? SuggestedLineId { get; set; }
}

public class WorkOrderInspectionDto
{
    public Guid Id { get; set; }
    public Guid WorkOrderId { get; set; }
    public Guid? TemplateId { get; set; }
    public string Usage { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? InspectorName { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Summary { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public int FailCount { get; set; }
    public int AttentionCount { get; set; }
    public List<InspectionResultDto> Results { get; set; } = new();
}

// ---------- الأسطر ----------

public class SaveWorkOrderLineDto
{
    public string Kind { get; set; } = string.Empty;
    /// <summary>العمالة: الخدمة المعرّفة (منها الصنف والسعر).</summary>
    public Guid? ServiceId { get; set; }
    /// <summary>القطعة: صنف مخزني؛ الخارجي والرسوم: صنف خدمي.</summary>
    public Guid? ProductId { get; set; }
    public string? Description { get; set; }
    public decimal Quantity { get; set; } = 1;
    /// <summary>فارغ = سعر الخدمة أو الصنف؛ المخالف يتطلب «خصم يدوي».</summary>
    public decimal? UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? BillingType { get; set; }
    public decimal? EstimatedCost { get; set; }
    public Guid? SourceResultId { get; set; }
    public string? Notes { get; set; }
}

public class WorkOrderLineDto
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public Guid? ServiceId { get; set; }
    public string? ServiceCode { get; set; }
    public Guid ProductId { get; set; }
    public string? ProductSku { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public bool PriceOverridden { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal VatRate { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string ApprovalStatus { get; set; } = string.Empty;
    public Guid? ApprovedEstimateId { get; set; }
    public int? ApprovedVersion { get; set; }
    public string BillingType { get; set; } = string.Empty;
    public string ExecutionStatus { get; set; } = string.Empty;
    /// <summary>التكلفة التقديرية (تظهر بصلاحية رؤية التكلفة فقط).</summary>
    public decimal? EstimatedCost { get; set; }
    public bool AddedAfterApproval { get; set; }
    public Guid? SourceResultId { get; set; }
    /// <summary>يُنفَّذ: موافق عليه أو له استثناء موثق.</summary>
    public bool CanExecute { get; set; }
    public bool HasException { get; set; }
    public string? Notes { get; set; }
    public int SortOrder { get; set; }
}

public class ChangeLineExecutionDto
{
    public string ExecutionStatus { get; set; } = string.Empty;
}

/// <summary>إجماليات أسطر أمر العمل قبل الفوترة (على العميل وحده).</summary>
public class WorkOrderTotalsDto
{
    public decimal ApprovedNet { get; set; }
    public decimal ApprovedVat { get; set; }
    public decimal ApprovedTotal { get; set; }
    public decimal PendingTotal { get; set; }
    public int PendingCount { get; set; }
    public int ApprovedCount { get; set; }
    public int RejectedCount { get; set; }
}

// ---------- التقديرات ----------

public class CreateEstimateDto
{
    public string? Notes { get; set; }
}

public class EstimateDecisionDto
{
    public Guid LineId { get; set; }
    public string Decision { get; set; } = string.Empty;
}

/// <summary>رد العميل: قناته واسم من رد، وقرار لكل سطر معروض (موافق، مرفوض، مؤجل).</summary>
public class RespondEstimateDto
{
    public string Channel { get; set; } = string.Empty;
    public string? RespondedByName { get; set; }
    public string? Notes { get; set; }
    public DateTime? RespondedAt { get; set; }
    public List<EstimateDecisionDto> Decisions { get; set; } = new();
}

public class EstimateLineDto
{
    public Guid Id { get; set; }
    public Guid LineId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal VatRate { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public string BillingType { get; set; } = string.Empty;
    public string? Decision { get; set; }
    public int SortOrder { get; set; }
}

public class WorkOrderEstimateDto
{
    public Guid Id { get; set; }
    public Guid WorkOrderId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public int Version { get; set; }
    public string Status { get; set; } = string.Empty;
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
    public DateTime CreatedAt { get; set; }
    public List<EstimateLineDto> Lines { get; set; } = new();
}

// ---------- الاستثناء ----------

public class CreateExecutionExceptionDto
{
    public string? Reason { get; set; }
    /// <summary>مرفق من مرفقات أمر العمل يوثّق التخويل (رسالة، توقيع).</summary>
    public Guid? AttachmentId { get; set; }
}

public class ExecutionExceptionDto
{
    public Guid Id { get; set; }
    public Guid LineId { get; set; }
    public string? LineDescription { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? AttachmentId { get; set; }
    public string? AuthorizedBy { get; set; }
    public DateTime AuthorizedAt { get; set; }
}

/// <summary>لوحة العمل على أمر العمل: الأسطر وإجمالياتها، والفحوص، والتقديرات، والاستثناءات.</summary>
public class WorkOrderWorkDto
{
    public Guid WorkOrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool CanViewCost { get; set; }
    public List<WorkOrderLineDto> Lines { get; set; } = new();
    public WorkOrderTotalsDto Totals { get; set; } = new();
    public List<WorkOrderInspectionDto> Inspections { get; set; } = new();
    public List<WorkOrderEstimateDto> Estimates { get; set; } = new();
    public List<ExecutionExceptionDto> Exceptions { get; set; } = new();
}
