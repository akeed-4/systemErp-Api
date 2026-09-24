namespace ERP.Core.DTOs.Accounting;

public partial class DeliveryNoteItemDto
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal ContractQty { get; set; }
    public decimal DeliveredQty { get; set; }
    public decimal ReturnedQty { get; set; }
}
