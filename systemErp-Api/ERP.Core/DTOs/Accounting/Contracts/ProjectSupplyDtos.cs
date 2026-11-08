namespace ERP.Core.DTOs.Accounting.Contracts;

/// <summary>
/// متابعة توريد مواد المشروع لكل صنف: المطلوب في الاحتياجات المعتمدة (دراسة الجدوى) بوحدة الصنف، وما طُلب شراؤه، وما اشتُري،
/// وما سُلِّم للمشروع، والمتبقي للطلب وللتسليم.
/// </summary>
public class ProjectMaterialTrackingDto
{
    public Guid ContractId { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string? ProjectName { get; set; }
    public string? ProjectLocation { get; set; }
    public Guid? WarehouseId { get; set; }
    /// <summary>حالة خطة الاحتياجات: none | draft | submitted | approved. المطلوب من المعتمدة وحدها.</summary>
    public string PlanStatus { get; set; } = "none";
    /// <summary>مواد في الخطة غير مربوطة بصنف مخزني (لا تُطلب ولا تُصرف حتى تُربط).</summary>
    public int UnlinkedMaterials { get; set; }
    public List<ProjectMaterialTrackingLineDto> Lines { get; set; } = new();
    public ProjectMaterialTrackingTotalsDto Totals { get; set; } = new();
}

public class ProjectMaterialTrackingLineDto
{
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    /// <summary>الكمية المطلوبة في دراسة الجدوى (الاحتياجات المعتمدة) بوحدة الصنف.</summary>
    public decimal RequiredQuantity { get; set; }
    /// <summary>طُلب شراؤه في طلبات شراء المشروع (غير المرفوضة).</summary>
    public decimal RequestedQuantity { get; set; }
    /// <summary>اشتُري بفواتير شراء مرحّلة محوّلة من طلبات المشروع.</summary>
    public decimal PurchasedQuantity { get; set; }
    /// <summary>سُلِّم للمشروع بمستندات صرف مرحّلة.</summary>
    public decimal DeliveredQuantity { get; set; }
    public decimal RemainingToRequest { get; set; }
    public decimal RemainingToDeliver { get; set; }
    /// <summary>الرصيد في مستودع المشروع (أو مستودع الطلب).</summary>
    public decimal WarehouseStock { get; set; }
    public decimal UnitCost { get; set; }
    /// <summary>صنف خارج الاحتياجات المعتمدة (طُلب أو صُرف دون أن يكون مطلوباً).</summary>
    public bool OutsidePlan { get; set; }
}

public class ProjectMaterialTrackingTotalsDto
{
    public int Items { get; set; }
    public decimal RequiredValue { get; set; }
    public decimal DeliveredValue { get; set; }
    /// <summary>نسبة التسليم بالقيمة (0-100).</summary>
    public decimal DeliveredPercent { get; set; }
    public int FullyDelivered { get; set; }
    public int PendingRequest { get; set; }
}

public class ProjectMaterialIssueDocDto
{
    public Guid Id { get; set; }
    public Guid ContractId { get; set; }
    public string IssueNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public Guid? WarehouseId { get; set; }
    public string ReceiverType { get; set; } = string.Empty;
    public string ReceiverName { get; set; } = string.Empty;
    public Guid? ReceiverEmployeeId { get; set; }
    public Guid? ReceiverSupplierId { get; set; }
    public Guid? RequisitionId { get; set; }
    public string Status { get; set; } = "draft";
    public decimal TotalCost { get; set; }
    public Guid? JournalEntryId { get; set; }
    public string? Notes { get; set; }
    public DateTime? PostedAt { get; set; }
    public string? PostedBy { get; set; }
    public string? CancelReason { get; set; }
    public List<ProjectMaterialIssueLineDto> Lines { get; set; } = new();
}

public class ProjectMaterialIssueLineDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public string? Notes { get; set; }
    // للقراءة (أعمدة المستند): من متابعة المشروع وقت القراءة، دون هذا المستند
    public decimal RequiredQuantity { get; set; }
    public decimal DeliveredBefore { get; set; }
    public decimal RemainingBefore { get; set; }
}

public class SaveProjectMaterialIssueDto
{
    public DateTime? Date { get; set; }
    public string? ManualNumber { get; set; }
    public Guid? WarehouseId { get; set; }
    public string ReceiverType { get; set; } = "site_engineer";
    public string ReceiverName { get; set; } = string.Empty;
    public Guid? ReceiverEmployeeId { get; set; }
    public Guid? ReceiverSupplierId { get; set; }
    public Guid? RequisitionId { get; set; }
    public string? Notes { get; set; }
    /// <summary>ترحيل مباشر بعد الحفظ.</summary>
    public bool Post { get; set; }
    public List<SaveProjectMaterialIssueLineDto> Lines { get; set; } = new();
}

public class SaveProjectMaterialIssueLineDto
{
    public Guid ProductId { get; set; }
    public decimal Quantity { get; set; }
    public string? Notes { get; set; }
}
