namespace ERP.Core.DTOs.Shared;

public class RecordStockMovementDto
{
    public Guid ItemId { get; set; }
    public Guid? WarehouseId { get; set; }
    public StockMovementType Type { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal? UnitPrice { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public DateTime? Date { get; set; }
    public string? Notes { get; set; }
    /// <summary>manual | invoice | invoice_reversal | ...</summary>
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }
}
