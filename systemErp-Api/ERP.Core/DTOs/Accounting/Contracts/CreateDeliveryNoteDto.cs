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
    public string? PartyTaxNumber { get; set; }
    /// <summary>الموقع الذي ستُصرف منه/تُستلم فيه الأصناف؛ ينتقل إلى الفاتورة (فارغ = الافتراضي).</summary>
    public Guid? WarehouseId { get; set; }
    public string? PartyPhone { get; set; }
    public string? WarehouseLocation { get; set; }
    public string? DriverName { get; set; }
    public string? VehiclePlate { get; set; }
    public string? Notes { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public List<DeliveryNoteItemDto> Items { get; set; } = new();
    /// <summary>طلب شراء المشروع المعتمد (سند استلام مشروع: purchase_delivery على عقد مقاولات).</summary>
    public Guid? RequisitionId { get; set; }
    /// <summary>استلام مشتريات مرتبط بمشروع مقاولات إلى موقع المشروع، دون تحويله إلى عهدة طلب شراء.</summary>
    public bool ConstructionWarehouseReceipt { get; set; }
    /// <summary>أمر الشراء المستلم عليه (يُفحص المتبقي والمورد والسعر من الأمر).</summary>
    public Guid? PurchaseOrderId { get; set; }
    /// <summary>مصدر البيان: contract (الاتفاقية في ContractId) أو sales_invoice (فاتورة بيع مرحّلة في SourceId)؛ فارغ = كما كان.</summary>
    public string? SourceType { get; set; }
    /// <summary>فاتورة البيع المصدر (sales_invoice).</summary>
    public Guid? SourceId { get; set; }
    /// <summary>موقع العميل المختار؛ العنوان والإحداثيات الفارغة تُعبّأ منه (لقطة).</summary>
    public Guid? DeliveryLocationId { get; set; }
    public string? DeliveryAddress { get; set; }
    public decimal? DeliveryLatitude { get; set; }
    public decimal? DeliveryLongitude { get; set; }
}
