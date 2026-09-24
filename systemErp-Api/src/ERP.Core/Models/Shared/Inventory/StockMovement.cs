
namespace ERP.Core.Models.Shared;

public class StockMovement : BaseEntity
{
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
    /// <summary>الكمية غير المستهلكة من هذه الدفعة (للحركات الواردة فقط) - أساس تكلفة FIFO.</summary>
    public decimal RemainingQuantity { get; set; }
    public string? Notes { get; set; }
    /// <summary>مصدر الحركة: manual (تسوية يدوية) | invoice | invoice_reversal | ... — الحركات اليدوية فقط تُعدَّل/تُحذف.</summary>
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }
}
