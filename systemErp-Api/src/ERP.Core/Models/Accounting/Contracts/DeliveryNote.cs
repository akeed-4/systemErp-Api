
namespace ERP.Core.Models.Accounting;

public class DeliveryNote : BaseEntity
{
    public string DeliveryNumber { get; set; } = string.Empty;
    public string Type { get; set; } = "sales_delivery"; // sales_delivery | purchase_delivery
    public Guid? ContractId { get; set; }
    public string? ContractNumber { get; set; }
    /// <summary>العميل/المورد المرتبط (اختياري) - يحدّد حساب الذمم عند الفوترة.</summary>
    public Guid? PartyId { get; set; }
    /// <summary>customer | supplier</summary>
    public string? PartyType { get; set; }
    public string PartyName { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public DeliveryNoteStatus Status { get; set; } = DeliveryNoteStatus.Draft;
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }

    public virtual ICollection<DeliveryNoteItem> Items { get; set; } = new List<DeliveryNoteItem>();
}
