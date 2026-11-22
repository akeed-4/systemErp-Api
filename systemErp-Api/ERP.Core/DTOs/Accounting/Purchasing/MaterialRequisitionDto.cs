namespace ERP.Core.DTOs.Accounting;

public partial class MaterialRequisitionDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string RequisitionNumber { get; set; } = string.Empty;
    public DateTime RequestDate { get; set; }
    public DateTime RequiredDate { get; set; }
    public string Department { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public Guid? DepartmentId { get; set; }
    public Guid? RequestedByEmployeeId { get; set; }
    public string Priority { get; set; } = "medium";
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    /// <summary>المشروع (عقد مقاولات): الكميات تُقارن باحتياجاته المعتمدة وما طُلب سابقاً.</summary>
    public Guid? ContractId { get; set; }
    /// <summary>مرحلة التنفيذ (فارغة في الطلبات القديمة وفي مشروع بلا مراحل).</summary>
    public Guid? PhaseId { get; set; }
    public Guid? WorkOrderId { get; set; }
    public string? WorkOrderNumber { get; set; }
    /// <summary>warehouse | project_site</summary>
    public string? DeliveryLocation { get; set; }
    public string? DeliveryAddress { get; set; }
    public decimal TotalEstimatedCost { get; set; }
    public string Status { get; set; } = "draft";
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovalDate { get; set; }
    public Guid? ConvertedInvoiceId { get; set; }
    public string? Notes { get; set; }
    /// <summary>للقراءة: رقم العقد واسم المشروع.</summary>
    public string? ContractNumber { get; set; }
    public string? ProjectName { get; set; }
    public string? ProjectNameEn { get; set; }
    /// <summary>للقراءة: اسم مرحلة التنفيذ.</summary>
    public string? PhaseName { get; set; }
    public List<MaterialRequisitionItemDto> Items { get; set; } = new();
}

/// <summary>خيارات القسم ومقدّم الطلب من شؤون الموظفين (فارغة بلا وحدة الموارد البشرية: يبقى النص الحر).</summary>
public class RequisitionRequestersDto
{
    public List<RequisitionDepartmentOptionDto> Departments { get; set; } = new();
    public List<RequisitionEmployeeOptionDto> Employees { get; set; } = new();
}

public class RequisitionDepartmentOptionDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class RequisitionEmployeeOptionDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? JobTitle { get; set; }
    public Guid? DepartmentId { get; set; }
    /// <summary>حساب الموظف: يُختار به مقدّم الطلب افتراضياً للمستخدم الحالي.</summary>
    public Guid? UserId { get; set; }
}
