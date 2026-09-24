namespace ERP.Core.DTOs.CarShowroom;

public partial class CarProcurementOrderDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public ProcurementStage Stage { get; set; } = ProcurementStage.Requisition;
    public DateTime Date { get; set; }
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string? SupplierCr { get; set; }
    public string? SupplierVat { get; set; }
    public string? Priority { get; set; } = "normal";
    public string Currency { get; set; } = "SAR";
    public decimal ExchangeRate { get; set; } = 1;
    public string PaymentType { get; set; } = "cash";
    public int? CreditDays { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public ProcurementOrderStatus Status { get; set; } = ProcurementOrderStatus.Draft;
    public string? Notes { get; set; }
    public string? ApprovalNotes { get; set; }
    public string? ShippingCarrier { get; set; }
    public string? TrackingNumber { get; set; }
    public string? ShippingType { get; set; }
    public string? PortOfEntry { get; set; }
    public string? BillOfLadingNumber { get; set; }
    public string? CustomsDeclarationNumber { get; set; }
    public decimal? CustomsDutyFee { get; set; }
    public decimal? PortStorageFee { get; set; }
    public string? ClearanceAgent { get; set; }
    public bool? PdiInspectionPassed { get; set; }
    public string? RejectionReason { get; set; }
    public string? WarehouseLocation { get; set; }
    public string? MatchedInvoiceNumber { get; set; }
    public DateTime? SupplierInvoiceDate { get; set; }
    public string? DebitNoteNumber { get; set; }
    public string? DebitNoteReason { get; set; }
    public Guid? PurchaseInvoiceId { get; set; }
    public List<CarProcurementOrderItemDto> Items { get; set; } = new();
    public List<CarProcurementOrderVinDto> ReceivedVins { get; set; } = new();
}
