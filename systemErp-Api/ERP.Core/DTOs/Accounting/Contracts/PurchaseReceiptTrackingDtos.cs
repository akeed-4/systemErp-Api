namespace ERP.Core.DTOs.Accounting;

/// <summary>
/// مصدر سند استلام المشتريات (فاتورة مشتريات مرحّلة أو اتفاقية شراء) بكميات كل سطر. القيم يحسبها السرفر وحده
/// (<c>PurchaseReceiptQuantities</c>)، والشاشات والتقارير تعرضها.
/// </summary>
public class PurchaseReceiptSourceDto
{
    /// <summary>purchase_invoice | contract</summary>
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
    public string SourceNumber { get; set; } = string.Empty;
    public DateTime SourceDate { get; set; }
    public Guid? PartyId { get; set; }
    public string PartyName { get; set; } = string.Empty;
    public string? PartyTaxNumber { get; set; }
    public string? PartyPhone { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? CostCenterId { get; set; }
    /// <summary>المتابعة النشطة للمصدر (إن وُجدت).</summary>
    public Guid? TrackingId { get; set; }
    public string? TrackingStatus { get; set; }
    public List<PurchaseReceiptSourceLineDto> Lines { get; set; } = new();
}

/// <summary>
/// كميات سطر المصدر منفصلة: المطلوب، المستلم فعلياً، المقبول، المرفوض، المرتجع تشغيلياً (مرتجع سند الاستلام يعيده للمتبقي)،
/// المرتجع مالياً (مرتجع مشتريات مرحّل)، بانتظار المرتجع المالي، المتبقي، والمفوتر.
/// </summary>
public class PurchaseReceiptSourceLineDto
{
    public Guid SourceLineId { get; set; }
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string? Unit { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? CostCenterId { get; set; }
    /// <summary>سعر الوحدة قبل الضريبة.</summary>
    public decimal UnitPrice { get; set; }
    public decimal VatRate { get; set; }
    public decimal SourceNet { get; set; }
    /// <summary>المطلوب: كمية سطر الفاتورة أو المتفق عليه في الاتفاقية.</summary>
    public decimal OrderedQty { get; set; }
    public decimal ReceivedQty { get; set; }
    public decimal AcceptedQty { get; set; }
    public decimal RejectedQty { get; set; }
    public decimal OperationalReturnedQty { get; set; }
    /// <summary>صافي المقبول = المقبول − المرتجع تشغيلياً.</summary>
    public decimal NetAcceptedQty { get; set; }
    public decimal FinancialReturnedQty { get; set; }
    /// <summary>المرفوض الذي لم يقابله مرتجع مالي بعد (سند فاتورة المشتريات فقط؛ مرفوض الاتفاقية لم يُفوتر فيعود للمتبقي).</summary>
    public decimal AwaitingFinancialReturnQty { get; set; }
    public decimal RemainingQty { get; set; }
    public decimal InvoicedQty { get; set; }
    /// <summary>صنف مادي يُستلم؛ الخدمة والبند الوصفي لا يُستلمان بسند.</summary>
    public bool Receivable { get; set; } = true;
}

// ---------------- المتابعة ----------------

public class PurchaseSupplyTrackingDto
{
    public Guid Id { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
    public string SourceNumber { get; set; } = string.Empty;
    public DateTime SourceDate { get; set; }
    public Guid? SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public Guid? WarehouseId { get; set; }
    public DateTime? ExpectedDate { get; set; }
    /// <summary>المخزنة (pending | partially_received | received | rejected | cancelled).</summary>
    public string StoredStatus { get; set; } = string.Empty;
    /// <summary>للعرض: المخزنة، أو delayed للمفتوحة التي تجاوزت موعد التوريد.</summary>
    public string Status { get; set; } = string.Empty;
    public bool IsDelayed { get; set; }
    public int DelayDays { get; set; }
    public DateTime? LastReceiptDate { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string? RejectedByName { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelledByName { get; set; }
    public string? CancellationReason { get; set; }
    public decimal OrderedQty { get; set; }
    public decimal ReceivedQty { get; set; }
    public decimal AcceptedQty { get; set; }
    public decimal RejectedQty { get; set; }
    public decimal OperationalReturnedQty { get; set; }
    public decimal FinancialReturnedQty { get; set; }
    public decimal AwaitingFinancialReturnQty { get; set; }
    public decimal RemainingQty { get; set; }
    public decimal InvoicedQty { get; set; }
    public int ReceiptsCount { get; set; }
    /// <summary>أرقام سندات الاستلام (للعرض والبحث).</summary>
    public List<string> ReceiptNumbers { get; set; } = new();
}

public class PurchaseSupplyTrackingDetailsDto : PurchaseSupplyTrackingDto
{
    public List<PurchaseReceiptSourceLineDto> Lines { get; set; } = new();
    public List<PurchaseReceiptSummaryDto> Receipts { get; set; } = new();
    public List<PurchaseSupplyEventDto> Events { get; set; } = new();
}

/// <summary>سند استلام مختصر في المتابعة بأسطره وكمياتها ومرتجعاته.</summary>
public class PurchaseReceiptSummaryDto
{
    public Guid Id { get; set; }
    public string DeliveryNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid? WarehouseId { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? Notes { get; set; }
    public decimal ReceivedQty { get; set; }
    public decimal AcceptedQty { get; set; }
    public decimal RejectedQty { get; set; }
    public decimal ReturnedQty { get; set; }
    public List<DeliveryNoteItemDto> Items { get; set; } = new();
    public List<string> ReturnNumbers { get; set; } = new();
}

public class PurchaseSupplyEventDto
{
    public Guid Id { get; set; }
    public Guid? DeliveryNoteId { get; set; }
    public string? DocumentNumber { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string? Notes { get; set; }
    public string? UserName { get; set; }
    public DateTime OccurredAt { get; set; }
}

public class PurchaseSupplyTrackingQueryDto
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string? Search { get; set; }
    /// <summary>pending | partially_received | received | delayed | rejected | cancelled؛ فارغ = الكل.</summary>
    public string? Status { get; set; }
    public string? SourceType { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? WarehouseId { get; set; }
    /// <summary>الفترة على تاريخ المصدر.</summary>
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

/// <summary>فتح متابعة لمصدر (أو إعادة فتحها بعد رفض/إلغاء): ينشئها إن لم تكن نشطة.</summary>
public class OpenPurchaseSupplyTrackingDto
{
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public string? Notes { get; set; }
}

public class UpdatePurchaseSupplyTrackingDto
{
    public DateTime? ExpectedDate { get; set; }
    public string? Notes { get; set; }
}

public class ClosePurchaseSupplyTrackingDto
{
    public string Reason { get; set; } = string.Empty;
}

// ---------------- التقارير ----------------

public class PurchaseReceiptReportDto
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public PurchaseReceiptReportTotalsDto Totals { get; set; } = new();
    public List<PurchaseReceiptSupplierRowDto> Suppliers { get; set; } = new();
    /// <summary>المتابعات المستلمة جزئياً.</summary>
    public List<PurchaseSupplyTrackingDto> Partial { get; set; } = new();
    public List<PurchaseSupplyTrackingDto> Delayed { get; set; } = new();
    /// <summary>الأصناف: المطلوب والمستلم والمتبقي لكل صنف.</summary>
    public List<PurchaseReceiptItemRowDto> Items { get; set; } = new();
    public List<PurchaseReceiptRejectedRowDto> Rejected { get; set; } = new();
    public List<PurchaseReceiptReturnRowDto> Returns { get; set; } = new();
}

public class PurchaseReceiptReportTotalsDto
{
    public int Trackings { get; set; }
    public int Delayed { get; set; }
    public decimal OrderedQty { get; set; }
    public decimal ReceivedQty { get; set; }
    public decimal AcceptedQty { get; set; }
    public decimal RejectedQty { get; set; }
    public decimal OperationalReturnedQty { get; set; }
    public decimal FinancialReturnedQty { get; set; }
    public decimal AwaitingFinancialReturnQty { get; set; }
    public decimal RemainingQty { get; set; }
}

public class PurchaseReceiptSupplierRowDto
{
    public Guid? SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public int Trackings { get; set; }
    public int Received { get; set; }
    public int Partial { get; set; }
    public int Delayed { get; set; }
    public decimal OrderedQty { get; set; }
    public decimal ReceivedQty { get; set; }
    public decimal RejectedQty { get; set; }
    public decimal RemainingQty { get; set; }
    /// <summary>نسبة الاستلام = صافي المقبول ÷ المطلوب.</summary>
    public decimal FulfillmentPercent { get; set; }
}

public class PurchaseReceiptItemRowDto
{
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public decimal OrderedQty { get; set; }
    public decimal ReceivedQty { get; set; }
    public decimal AcceptedQty { get; set; }
    public decimal RejectedQty { get; set; }
    public decimal OperationalReturnedQty { get; set; }
    public decimal FinancialReturnedQty { get; set; }
    public decimal RemainingQty { get; set; }
}

public class PurchaseReceiptRejectedRowDto
{
    public Guid DeliveryNoteId { get; set; }
    public string DeliveryNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string SourceNumber { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public decimal RejectedQty { get; set; }
    public string? RejectionReason { get; set; }
    /// <summary>awaiting_financial_return (فاتورة لم يُرحَّل مرتجعها المالي) | financially_returned | returned_to_remaining (اتفاقية).</summary>
    public string State { get; set; } = string.Empty;
}

public class PurchaseReceiptReturnRowDto
{
    public Guid Id { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string DeliveryNumber { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string SourceNumber { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public decimal Quantity { get; set; }
    public decimal Total { get; set; }
}
