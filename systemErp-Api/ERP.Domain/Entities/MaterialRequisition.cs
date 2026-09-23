using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class MaterialRequisition : BaseEntity
{
    public string RequisitionNumber { get; set; } = string.Empty;
    public DateTime RequestDate { get; set; }
    public DateTime RequiredDate { get; set; }
    public string Department { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public string Priority { get; set; } = "medium"; // low, medium, high, urgent
    
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    
    public decimal TotalEstimatedCost { get; set; }
    public string Status { get; set; } = "Draft";
    
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovalDate { get; set; }
    public Guid? ConvertedInvoiceId { get; set; }
    public string? Notes { get; set; }

    public virtual ICollection<MaterialRequisitionItem> Items { get; set; } = new List<MaterialRequisitionItem>();
}

public class MaterialRequisitionItem : BaseEntity
{
    public Guid RequisitionId { get; set; }
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal RequestedQuantity { get; set; }
    public decimal? ApprovedQuantity { get; set; }
    public decimal? EstimatedCost { get; set; }
    public string? Notes { get; set; }

    public virtual MaterialRequisition Requisition { get; set; } = null!;
}
