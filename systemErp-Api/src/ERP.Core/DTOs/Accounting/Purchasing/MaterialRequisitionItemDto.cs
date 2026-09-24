namespace ERP.Core.DTOs.Accounting;

public partial class MaterialRequisitionItemDto
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal RequestedQuantity { get; set; }
    public decimal? ApprovedQuantity { get; set; }
    public decimal? EstimatedCost { get; set; }
    public string? Notes { get; set; }
}
