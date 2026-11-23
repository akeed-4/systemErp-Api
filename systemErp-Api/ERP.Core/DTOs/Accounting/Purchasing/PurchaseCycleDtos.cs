namespace ERP.Core.DTOs.Accounting.Purchasing;

// ---------- طلب عرض الأسعار ----------

public class CreatePurchaseRfqDto
{
    public Guid RequisitionId { get; set; }
    public DateTime? Date { get; set; }
    public DateTime? DueDate { get; set; }
    public string? Notes { get; set; }
    /// <summary>الموردون المدعوون (مورد واحد على الأقل).</summary>
    public List<Guid> SupplierIds { get; set; } = new();
    /// <summary>أسطر مختارة من الطلب بكمياتها؛ فارغ = كل أسطر الطلب بالكمية المعتمدة.</summary>
    public List<PurchaseRfqItemInputDto> Items { get; set; } = new();
}

public class PurchaseRfqItemInputDto
{
    public Guid RequisitionItemId { get; set; }
    public decimal Quantity { get; set; }
}

public class AddRfqSuppliersDto
{
    public List<Guid> SupplierIds { get; set; } = new();
}

public class PurchaseRfqDto
{
    public Guid Id { get; set; }
    public string RfqNumber { get; set; } = string.Empty;
    public Guid RequisitionId { get; set; }
    public string? RequisitionNumber { get; set; }
    public Guid? ContractId { get; set; }
    public Guid? WarehouseId { get; set; }
    public DateTime Date { get; set; }
    public DateTime? DueDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? AwardedBy { get; set; }
    /// <summary>الترسية محجوزة بطلب اعتماد معلّق (سياسة الاعتماد).</summary>
    public bool AwardPendingApproval { get; set; }
    public DateTime? AwardedAt { get; set; }
    public int QuotationCount { get; set; }
    public List<PurchaseRfqItemDto> Items { get; set; } = new();
    public List<PurchaseRfqSupplierDto> Suppliers { get; set; } = new();
    /// <summary>أوامر الشراء الناتجة عن الترسية.</summary>
    public List<PurchaseOrderSummaryDto> Orders { get; set; } = new();
}

public class PurchaseRfqItemDto
{
    public Guid Id { get; set; }
    public Guid? RequisitionItemId { get; set; }
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
}

public class PurchaseRfqSupplierDto
{
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public Guid? QuotationId { get; set; }
    public string? QuotationNumber { get; set; }
    public string? QuotationStatus { get; set; }
    public decimal? QuotationTotal { get; set; }
}

// ---------- عرض المورد ----------

public class SubmitSupplierQuotationDto
{
    public Guid SupplierId { get; set; }
    /// <summary>رقم عرض المورد (اختياري؛ وإلا يُرقّم تلقائياً).</summary>
    public string? QuotationNumber { get; set; }
    public DateTime? Date { get; set; }
    public DateTime? ValidUntil { get; set; }
    public decimal ExtraCosts { get; set; }
    public int? DeliveryDays { get; set; }
    public string? PaymentTerms { get; set; }
    public string? Notes { get; set; }
    public List<SupplierQuotationLineInputDto> Lines { get; set; } = new();
}

public class SupplierQuotationLineInputDto
{
    public Guid RfqItemId { get; set; }
    /// <summary>الكمية المعروضة (≤ كمية الطلب)؛ 0 = كمية السطر كاملة.</summary>
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    /// <summary>خصم السطر (مبلغ).</summary>
    public decimal Discount { get; set; }
    public decimal VatRate { get; set; } = 15m;
}

// ---------- المقارنة ----------

public class RfqComparisonDto
{
    public Guid RfqId { get; set; }
    public string RfqNumber { get; set; } = string.Empty;
    public List<RfqComparisonQuoteDto> Quotes { get; set; } = new();
    public List<RfqComparisonLineDto> Lines { get; set; } = new();
    /// <summary>أقل إجمالي (شامل الضريبة والتكاليف الإضافية) بين العروض التي تغطي كل الأسطر بكاملها.</summary>
    public Guid? BestTotalQuotationId { get; set; }
}

public class RfqComparisonQuoteDto
{
    public Guid QuotationId { get; set; }
    public string QuotationNumber { get; set; } = string.Empty;
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public DateTime ValidUntil { get; set; }
    public int? DeliveryDays { get; set; }
    public string? PaymentTerms { get; set; }
    public decimal Gross { get; set; }
    public decimal Discount { get; set; }
    public decimal Net { get; set; }
    public decimal ExtraCosts { get; set; }
    public decimal Vat { get; set; }
    public decimal GrandTotal { get; set; }
    public int LinesQuoted { get; set; }
    public bool CoversAll { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class RfqComparisonLineDto
{
    public Guid RfqItemId { get; set; }
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public List<RfqComparisonOfferDto> Offers { get; set; } = new();
    /// <summary>أقل تكلفة وحدة شاملة (بعد الخصم ومع نصيب التكاليف الإضافية والضريبة) للكمية كاملة.</summary>
    public Guid? BestQuotationId { get; set; }
}

public class RfqComparisonOfferDto
{
    public Guid QuotationId { get; set; }
    public Guid QuotationItemId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal VatRate { get; set; }
    public decimal Net { get; set; }
    public decimal ExtraShare { get; set; }
    public decimal Vat { get; set; }
    public decimal Total { get; set; }
    /// <summary>تكلفة الوحدة قبل الضريبة شاملة الخصم ونصيب التكاليف الإضافية.</summary>
    public decimal LandedUnitPrice { get; set; }
    public bool FullQuantity { get; set; }
}

// ---------- الترسية ----------

public class AwardRfqDto
{
    /// <summary>ترسية العرض كله على مورد واحد.</summary>
    public Guid? QuotationId { get; set; }
    /// <summary>ترسية كل سطر على عرض (عدة موردين)؛ تُنشأ أمر شراء لكل مورد.</summary>
    public List<AwardRfqLineDto> Lines { get; set; } = new();
    public DateTime? ExpectedDeliveryDate { get; set; }
}

public class AwardRfqLineDto
{
    public Guid RfqItemId { get; set; }
    public Guid QuotationId { get; set; }
}

// ---------- أمر الشراء: الاستلام الجزئي والفوترة ----------

public class PurchaseOrderSummaryDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid? SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal GrandTotal { get; set; }
    public decimal ExtraCosts { get; set; }
}

/// <summary>فوترة عدة سندات استلام من أمر الشراء بفاتورة مورد واحدة.</summary>
public class InvoicePurchaseReceiptsDto
{
    public List<Guid> DeliveryNoteIds { get; set; } = new();
    public DateTime? Date { get; set; }
}

public class ReceivePurchaseOrderDto
{
    public DateTime? Date { get; set; }
    public string? DeliveryNumber { get; set; }
    /// <summary>موقع الاستلام؛ فارغ = موقع الأمر.</summary>
    public Guid? WarehouseId { get; set; }
    public string? Notes { get; set; }
    public List<ReceivePurchaseOrderLineDto> Lines { get; set; } = new();
}

public class ReceivePurchaseOrderLineDto
{
    public Guid ItemId { get; set; }
    public decimal Quantity { get; set; }
}

public class PurchaseOrderTrackingDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public Guid? WarehouseId { get; set; }
    public decimal ExtraCosts { get; set; }
    public decimal GrandTotal { get; set; }
    public Guid? RequisitionId { get; set; }
    public string? RequisitionNumber { get; set; }
    public Guid? RfqId { get; set; }
    public string? RfqNumber { get; set; }
    public Guid? QuotationId { get; set; }
    public string? QuotationNumber { get; set; }
    public List<PurchaseOrderTrackingLineDto> Lines { get; set; } = new();
    public List<PurchaseOrderReceiptDto> Receipts { get; set; } = new();
}

public class PurchaseOrderTrackingLineDto
{
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Ordered { get; set; }
    public decimal Received { get; set; }
    public decimal Invoiced { get; set; }
    public decimal RemainingToReceive { get; set; }
    public decimal ReceivedNotInvoiced { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal VatRate { get; set; }
}

public class PurchaseOrderReceiptDto
{
    public Guid Id { get; set; }
    public string DeliveryNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public decimal GrandTotal { get; set; }
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
}
