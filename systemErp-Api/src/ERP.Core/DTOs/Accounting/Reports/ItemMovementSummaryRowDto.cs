using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public class ItemMovementSummaryRowDto
{
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal OpeningStock { get; set; }
    public decimal PurchaseInQty { get; set; }
    public decimal SalesOutQty { get; set; }
    public decimal ReturnsQty { get; set; }
    public decimal ClosingStock { get; set; }
    public decimal AverageCost { get; set; }
    public decimal TotalStockValue { get; set; }
}
