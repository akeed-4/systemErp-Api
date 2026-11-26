namespace ERP.Core.DTOs.Accounting;

public partial class CreateMaterialRequisitionDto
{
    public string RequisitionNumber { get; set; } = string.Empty;
    public DateTime RequestDate { get; set; }
    public DateTime RequiredDate { get; set; }
    public string Department { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    /// <summary>قسم الموارد البشرية: يُكتب منه اسم القسم (يتقدّم على النص).</summary>
    public Guid? DepartmentId { get; set; }
    /// <summary>الموظف مقدّم الطلب (من القسم)؛ يُكتب منه الاسم، ويُملأ القسم منه إن لم يُرسل.</summary>
    public Guid? RequestedByEmployeeId { get; set; }
    public string Priority { get; set; } = "medium";
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    /// <summary>المشروع (عقد مقاولات): الكميات تُقارن باحتياجاته المعتمدة وما طُلب سابقاً.</summary>
    public Guid? ContractId { get; set; }
    /// <summary>مرحلة التنفيذ (إلزامية لطلب جديد في مشروع موادُّه مسندة لمراحل؛ تثبت بعد الإنشاء).</summary>
    public Guid? PhaseId { get; set; }
    /// <summary>أمر عمل الورشة (لا يجتمع مع المشروع؛ يثبت بعد الإنشاء).</summary>
    public Guid? WorkOrderId { get; set; }
    /// <summary>warehouse | project_site</summary>
    public string? DeliveryLocation { get; set; }
    public string? DeliveryAddress { get; set; }
    public decimal TotalEstimatedCost { get; set; }
    public string Status { get; set; } = "draft";
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovalDate { get; set; }
    public Guid? ConvertedInvoiceId { get; set; }
    public string? Notes { get; set; }
    public List<MaterialRequisitionItemDto> Items { get; set; } = new();
}
