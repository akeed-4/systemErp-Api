namespace ERP.Core.DTOs.Accounting;

public partial class CreateDeliveryNoteDto
{
    public string DeliveryNumber { get; set; } = string.Empty;
    public string Type { get; set; } = "sales_delivery";
    public Guid? ContractId { get; set; }
    public string? ContractNumber { get; set; }
    public Guid? PartyId { get; set; }
    public string? PartyType { get; set; }
    public string PartyName { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public DeliveryNoteStatus Status { get; set; } = DeliveryNoteStatus.Draft;
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public List<DeliveryNoteItemDto> Items { get; set; } = new();
}
