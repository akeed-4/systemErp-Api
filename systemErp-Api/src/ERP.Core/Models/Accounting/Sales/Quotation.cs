
namespace ERP.Core.Models.Accounting;

public class Quotation : BaseEntity
{
    public string QuotationNumber { get; set; } = string.Empty;
    public string Type { get; set; } = "sales_quotation"; // sales_quotation, purchase_quotation
    /// <summary>العميل/المورد المرتبط (اختياري) - يحدّد حساب الذمم عند الفوترة.</summary>
    public Guid? PartyId { get; set; }
    /// <summary>customer | supplier</summary>
    public string? PartyType { get; set; }
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
    
    public string Status { get; set; } = "draft";
    public Guid? ConvertedInvoiceId { get; set; }
    public string? Notes { get; set; }
    public string? TermsAndConditions { get; set; }

    public virtual ICollection<QuotationItem> Items { get; set; } = new List<QuotationItem>();
}
