using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public class DetailedItemLedgerEntryDto
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public DateTime Date { get; set; }
    /// <summary>وقت تسجيل الحركة: يرتّب حركات اليوم الواحد.</summary>
    public DateTime CreatedAt { get; set; }
    /// <summary>نوع الحركة: purchase, sales, sales_return, purchase_return, adjustment_in, adjustment_out, transfer_in, transfer_out.</summary>
    public string DocType { get; set; } = string.Empty;
    public Guid? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public string DocNumber { get; set; } = string.Empty;
    public decimal QuantityIn { get; set; }
    public decimal QuantityOut { get; set; }
    public decimal UnitCost { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal TotalValue { get; set; }
    public decimal RunningStockBalance { get; set; }
    public decimal RunningStockValue { get; set; }
}
