
namespace ERP.Core.Models.Accounting;

public class DeliveryReturnItem : BaseEntity
{
    public Guid ReturnNoteId { get; set; }
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }

    public virtual DeliveryReturnNote ReturnNote { get; set; } = null!;
}
