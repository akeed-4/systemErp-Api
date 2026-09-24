namespace ERP.Core.DTOs.Accounting;

public partial class CreateDeliveryReturnNoteDto
{
    public string ReturnNumber { get; set; } = string.Empty;
    public string Type { get; set; } = "sales_delivery_return";
    public Guid DeliveryNoteId { get; set; }
    public string DeliveryNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Status { get; set; } = "draft";
    public List<DeliveryReturnItemDto> Items { get; set; } = new();
}
