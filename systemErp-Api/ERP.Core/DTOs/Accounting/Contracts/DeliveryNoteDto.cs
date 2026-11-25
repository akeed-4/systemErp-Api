namespace ERP.Core.DTOs.Accounting;

public partial class DeliveryNoteDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string DeliveryNumber { get; set; } = string.Empty;
    public string Type { get; set; } = "sales_delivery";
    public Guid? ContractId { get; set; }
    public string? ContractNumber { get; set; }
    public string? ProjectName { get; set; }
    public string? ProjectNameEn { get; set; }
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
    public Guid? RequisitionId { get; set; }
    /// <summary>عهدة المشروع المنشأة من السند (سند استلام مشروع).</summary>
    public Guid? CustodyIssueId { get; set; }
    public Guid? PurchaseOrderId { get; set; }
    public string SourceType { get; set; } = "manual";
    public Guid? SourceId { get; set; }
    public string? SourceNumber { get; set; }
    /// <summary>حالة التوصيل: untracked | pending | in_transit | partially_delivered | delivered | cancelled.</summary>
    public string DeliveryStatus { get; set; } = "untracked";
    public DateTime? DispatchedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public Guid? DeliveryConfirmedByUserId { get; set; }
    public string? ReceiverName { get; set; }
    public DateTime? CancelledAt { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancellationReason { get; set; }
    public Guid? DeliveryLocationId { get; set; }
    public string? DeliveryAddress { get; set; }
    public decimal? DeliveryLatitude { get; set; }
    public decimal? DeliveryLongitude { get; set; }
    public DateTime? LocationReviewedAt { get; set; }
    public Guid? LocationReviewedByUserId { get; set; }
    public string? LocationReviewedByName { get; set; }
    public string? LocationReviewNotes { get; set; }
}
