namespace ERP.Core.Models.CarShowroom;

/// <summary>أمر توريد وشراء سيارات - يمر بدورة الشراء المعتمدة من 7 مراحل (ProcurementStage).</summary>
public class CarProcurementOrder : BaseEntity
{
    public string OrderNumber { get; set; } = string.Empty;
    public ProcurementStage Stage { get; set; } = ProcurementStage.Requisition;
    public DateTime Date { get; set; }

    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string? SupplierCr { get; set; }
    public string? SupplierVat { get; set; }
    /// <summary>normal | urgent | custom_order</summary>
    public string? Priority { get; set; } = "normal";
    /// <summary>SAR | USD | EUR | AED</summary>
    public string Currency { get; set; } = "SAR";
    public decimal ExchangeRate { get; set; } = 1;

    /// <summary>cash | credit | bank_lc | advance_milestone</summary>
    public string PaymentType { get; set; } = "cash";
    public int? CreditDays { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public ProcurementOrderStatus Status { get; set; } = ProcurementOrderStatus.Draft;
    public string? Notes { get; set; }
    public string? ApprovalNotes { get; set; }

    // الشحن واللوجستيات
    public string? ShippingCarrier { get; set; }
    public string? TrackingNumber { get; set; }
    /// <summary>land_carrier | sea_roro | air</summary>
    public string? ShippingType { get; set; }
    public string? PortOfEntry { get; set; }
    public string? BillOfLadingNumber { get; set; }

    // الجمارك والرسوم
    public string? CustomsDeclarationNumber { get; set; }
    public decimal? CustomsDutyFee { get; set; }
    public decimal? PortStorageFee { get; set; }
    public string? ClearanceAgent { get; set; }

    // استلام الشواسيه
    public bool? PdiInspectionPassed { get; set; }
    public string? RejectionReason { get; set; }
    public string? WarehouseLocation { get; set; }

    // الفوترة والترحيل
    public string? MatchedInvoiceNumber { get; set; }
    public DateTime? SupplierInvoiceDate { get; set; }
    public string? DebitNoteNumber { get; set; }
    public string? DebitNoteReason { get; set; }
    public Guid? PurchaseInvoiceId { get; set; }

    public virtual ICollection<CarProcurementOrderItem> Items { get; set; } = new List<CarProcurementOrderItem>();
    public virtual ICollection<CarProcurementOrderVin> ReceivedVins { get; set; } = new List<CarProcurementOrderVin>();
}
