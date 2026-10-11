
namespace ERP.Core.Models.Accounting;

public class DeliveryReturnNote : BaseEntity
{
    public string ReturnNumber { get; set; } = string.Empty;
    public string Type { get; set; } = "sales_delivery_return"; // sales_delivery_return | purchase_delivery_return
    public Guid DeliveryNoteId { get; set; }
    public string DeliveryNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Status { get; set; } = "draft"; // draft | completed
    public Guid? ContractId { get; set; }
    public string? ContractNumber { get; set; }
    public string PartyName { get; set; } = string.Empty;
    public string? ReturnReason { get; set; }
    public string? Notes { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }

    public virtual ICollection<DeliveryReturnItem> Items { get; set; } = new List<DeliveryReturnItem>();
}
