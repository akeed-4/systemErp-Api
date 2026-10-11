namespace ERP.Core.Models.Shared;

/// <summary>
/// تحويل مخزون بين مستودعين: ينقل الكميات دون أثر على إجمالي رصيد الصنف ولا تكلفته ولا الدفاتر
/// (التكلفة موحّدة وحساب المخزون واحد). لا يُعدَّل بعد تسجيله؛ يُصحَّح بتحويل معاكس.
/// </summary>
public class StockTransfer : BaseEntity
{
    public string TransferNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public Guid FromWarehouseId { get; set; }
    public string FromWarehouseName { get; set; } = string.Empty;
    public Guid ToWarehouseId { get; set; }
    public string ToWarehouseName { get; set; } = string.Empty;
    public string? Notes { get; set; }

    public virtual ICollection<StockTransferItem> Items { get; set; } = new List<StockTransferItem>();
}

public class StockTransferItem : BaseEntity
{
    public Guid StockTransferId { get; set; }
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public decimal Quantity { get; set; }
}
