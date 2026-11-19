
namespace ERP.Core.Models.Accounting;

public class DeliveryNote : BaseEntity, IPartyDocument
{
    public string DeliveryNumber { get; set; } = string.Empty;
    public string Type { get; set; } = "sales_delivery"; // sales_delivery | purchase_delivery
    public Guid? ContractId { get; set; }
    public string? ContractNumber { get; set; }
    /// <summary>العميل/المورد المرتبط (اختياري) - يحدّد حساب الذمم عند الفوترة.</summary>
    public Guid? PartyId { get; set; }
    /// <summary>customer | supplier</summary>
    public string? PartyType { get; set; }
    /// <summary>نسخة PartyId المقيّدة بمفتاح أجنبي إلى Customer (تُعبّأ تلقائياً عند الحفظ).</summary>
    public Guid? CustomerId { get; set; }
    /// <summary>نسخة PartyId المقيّدة بمفتاح أجنبي إلى Supplier (تُعبّأ تلقائياً عند الحفظ).</summary>
    public Guid? SupplierId { get; set; }
    public bool IsSupplierParty => PartySide.IsSupplier(PartyType, Type.StartsWith("purchase"));
    public string PartyName { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public DeliveryNoteStatus Status { get; set; } = DeliveryNoteStatus.Draft;
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? PartyTaxNumber { get; set; }
    public string? PartyPhone { get; set; }
    public string? WarehouseLocation { get; set; }
    public string? DriverName { get; set; }
    public string? VehiclePlate { get; set; }
    public string? Notes { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }

    public virtual ICollection<DeliveryNoteItem> Items { get; set; } = new List<DeliveryNoteItem>();
}
