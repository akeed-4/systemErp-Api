namespace ERP.Core.DTOs.Shared;

public partial class StockMovementDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid ItemId { get; set; }
    public Guid? WarehouseId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public StockMovementType Type { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal? UnitPrice { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public decimal RemainingStock { get; set; }
    public decimal RemainingQuantity { get; set; }
    public string? Notes { get; set; }
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }
}
