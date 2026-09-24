namespace ERP.Core.DTOs.Accounting;

public partial class DeliveryReturnItemDto
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
}
