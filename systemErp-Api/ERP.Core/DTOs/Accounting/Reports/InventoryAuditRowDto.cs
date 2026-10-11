using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public class InventoryAuditRowDto
{
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal SystemQuantity { get; set; }
    public decimal AverageUnitCost { get; set; }
    public decimal SystemValuation { get; set; }
}
