using ERP.Domain.Common;
using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public class StockMovement : BaseEntity
{
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public StockMovementType Type { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal? UnitPrice { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public decimal RemainingStock { get; set; }
}
