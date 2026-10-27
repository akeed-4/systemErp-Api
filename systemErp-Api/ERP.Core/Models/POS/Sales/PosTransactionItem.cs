namespace ERP.Core.Models.POS;

public class PosTransactionItem : BaseEntity
{
    public Guid TransactionId { get; set; }
    public Guid ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? UnitAr { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal ExciseTaxRate { get; set; }
    public decimal ExciseRetailPrice { get; set; }
    public decimal ExciseStandardPrice { get; set; }
    public decimal ExciseTaxBaseAmount { get; set; }
    public decimal ExciseTaxAmount { get; set; }
    public decimal VatRate { get; set; } = 15;
    public decimal VatAmount { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal TotalWithVat { get; set; }

    public virtual PosTransaction Transaction { get; set; } = null!;
}
