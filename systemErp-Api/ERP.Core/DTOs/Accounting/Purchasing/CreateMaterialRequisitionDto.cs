namespace ERP.Core.DTOs.Accounting;

public partial class CreateMaterialRequisitionDto
{
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
    public decimal TotalEstimatedCost { get; set; }
    public string Status { get; set; } = "draft";
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovalDate { get; set; }
    public Guid? ConvertedInvoiceId { get; set; }
    public string? Notes { get; set; }
    public List<MaterialRequisitionItemDto> Items { get; set; } = new();
}
