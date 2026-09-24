namespace ERP.Core.DTOs.Accounting;

public partial class CreateQuotationDto
{
    public string QuotationNumber { get; set; } = string.Empty;
    public string Type { get; set; } = "sales_quotation";
    public Guid? PartyId { get; set; }
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
    public List<QuotationItemDto> Items { get; set; } = new();
}
