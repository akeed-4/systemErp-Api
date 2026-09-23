using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class CommercialOrder : BaseEntity
{
    public string OrderNumber { get; set; } = string.Empty;
    public string Type { get; set; } = "sales_order"; // sales_order, purchase_order
    public string PartyName { get; set; } = string.Empty;
    public string? PartyPhone { get; set; }
    public string? PartyVatNumber { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime ExpectedDeliveryDate { get; set; }
    public string? PaymentTerms { get; set; }
    
    public decimal Subtotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    
    public string Status { get; set; } = "Draft";
    public Guid? ConvertedInvoiceId { get; set; }
    public string? Notes { get; set; }

    public virtual ICollection<QuotationItem> Items { get; set; } = new List<QuotationItem>(); // Reusing QuotationItem structure
}
