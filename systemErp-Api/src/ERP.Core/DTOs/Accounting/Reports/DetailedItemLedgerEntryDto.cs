using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public class DetailedItemLedgerEntryDto
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public DateTime Date { get; set; }
    public string DocType { get; set; } = string.Empty;
    public string DocNumber { get; set; } = string.Empty;
    public decimal QuantityIn { get; set; }
    public decimal QuantityOut { get; set; }
    public decimal UnitCost { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal TotalValue { get; set; }
    public decimal RunningStockBalance { get; set; }
    public decimal RunningStockValue { get; set; }
}
