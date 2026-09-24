
namespace ERP.Core.Models.Accounting;

public class InvoiceItem : BaseEntity
{
    public Guid InvoiceId { get; set; }
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal UnitCost { get; set; }
    public decimal Discount { get; set; }
    public decimal VatRate { get; set; } = 15m;
    public decimal VatAmount { get; set; }
    /// <summary>مبلغ ضريبة ثابت للسطر (نظام هامش الربح للسيارات المستعملة). فارغ = تُحسب من النسبة.</summary>
    public decimal? VatAmountOverride { get; set; }
    public decimal TotalBeforeVat { get; set; }
    public decimal TotalAfterVat { get; set; }
    public Guid? CostCenterId { get; set; }

    public virtual Invoice Invoice { get; set; } = null!;
}
