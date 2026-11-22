
namespace ERP.Core.Models.Accounting;

public class CommercialOrderItem : BaseEntity
{
    public Guid OrderId { get; set; }
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal VatRate { get; set; } = 15m;
    public decimal VatAmount { get; set; }
    public decimal TotalBeforeVat { get; set; }
    public decimal TotalAfterVat { get; set; }
    public Guid? RfqItemId { get; set; }
    public Guid? RequisitionItemId { get; set; }
    public Guid? QuotationItemId { get; set; }
    /// <summary>سعر وحدة الاستلام والفوترة قبل الضريبة: (الصافي بعد الخصم + نصيب التكاليف الإضافية) ÷ الكمية. فارغ = السعر − الخصم.</summary>
    public decimal? LandedUnitPrice { get; set; }

    public virtual CommercialOrder Order { get; set; } = null!;
}
