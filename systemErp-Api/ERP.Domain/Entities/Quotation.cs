using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class Quotation : BaseEntity
{
    public string QuotationNumber { get; set; } = string.Empty;
    public string Type { get; set; } = "sales_quotation"; // sales_quotation, purchase_quotation
    public string PartyName { get; set; } = string.Empty;
    public string? PartyPhone { get; set; }
    public string? PartyEmail { get; set; }
    public string? PartyVatNumber { get; set; }
    public DateTime Date { get; set; }
    public DateTime ValidUntil { get; set; }
    public string? PaymentTerms { get; set; }
    
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    
    public string Status { get; set; } = "Draft";
    public Guid? ConvertedInvoiceId { get; set; }
    public string? Notes { get; set; }
    public string? TermsAndConditions { get; set; }

    public virtual ICollection<QuotationItem> Items { get; set; } = new List<QuotationItem>();
}

public class QuotationItem : BaseEntity
{
    public Guid QuotationId { get; set; }
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal VatRate { get; set; } = 0.15m;
    public decimal VatAmount { get; set; }
    public decimal TotalBeforeVat { get; set; }
    public decimal TotalAfterVat { get; set; }

    public virtual Quotation Quotation { get; set; } = null!;
}
