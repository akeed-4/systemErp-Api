
namespace ERP.Core.Models.Accounting;

public class DeliveryReturnNote : BaseEntity
{
    public string ReturnNumber { get; set; } = string.Empty;
    public string Type { get; set; } = "sales_delivery_return"; // sales_delivery_return | purchase_delivery_return
    public Guid DeliveryNoteId { get; set; }
    public string DeliveryNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Status { get; set; } = "draft"; // draft | completed

    public virtual ICollection<DeliveryReturnItem> Items { get; set; } = new List<DeliveryReturnItem>();
}
