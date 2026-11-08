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
    public string Priority { get; set; } = "medium";
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    /// <summary>المشروع (عقد مقاولات): الكميات تُقارن باحتياجاته المعتمدة وما طُلب سابقاً.</summary>
    public Guid? ContractId { get; set; }
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
    public List<MaterialRequisitionItemDto> Items { get; set; } = new();
}
