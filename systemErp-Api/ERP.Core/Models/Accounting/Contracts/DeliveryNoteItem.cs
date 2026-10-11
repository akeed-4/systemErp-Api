
namespace ERP.Core.Models.Accounting;

public class DeliveryNoteItem : BaseEntity
{
    public Guid DeliveryNoteId { get; set; }
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal ContractQty { get; set; }
    public decimal DeliveredQty { get; set; }
    public decimal ReturnedQty { get; set; }
    public string? Sku { get; set; }
    public string? Unit { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal VatRate { get; set; } = 15m;
    public decimal VatAmount { get; set; }
    public decimal TotalBeforeVat { get; set; }
    public decimal TotalAfterVat { get; set; }

    public virtual DeliveryNote DeliveryNote { get; set; } = null!;
}
