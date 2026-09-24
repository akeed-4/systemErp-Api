namespace ERP.Core.DTOs.Accounting;

public partial class CreateCommercialOrderDto
{
    public string OrderNumber { get; set; } = string.Empty;
    public string Type { get; set; } = "sales_order";
    public Guid? PartyId { get; set; }
    public string? PartyType { get; set; }
    public string PartyName { get; set; } = string.Empty;
    public string? PartyPhone { get; set; }
    public string? PartyVatNumber { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime ExpectedDeliveryDate { get; set; }
    public string? PaymentTerms { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public string Status { get; set; } = "draft";
    public Guid? ConvertedInvoiceId { get; set; }
    public string? Notes { get; set; }
    public List<CommercialOrderItemDto> Items { get; set; } = new();
}
