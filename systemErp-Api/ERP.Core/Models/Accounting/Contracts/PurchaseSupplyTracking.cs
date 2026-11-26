namespace ERP.Core.Models.Accounting;

/// <summary>
/// متابعة توريد مصدر مشتريات واحد (فاتورة مشتريات مرحّلة أو اتفاقية شراء): موعد التوريد والملاحظات والحالة وسجل الأحداث.
/// سندات الاستلام نفسها هي <see cref="DeliveryNote"/> (purchase_delivery)؛ الكميات تُحسب من السندات والمرتجعات ولا تُخزَّن هنا.
/// حالة «متأخر» محسوبة من <see cref="ExpectedDate"/> ولا تُخزَّن؛ الرفض والإلغاء يدويان بسبب ومستخدم وتاريخ.
/// </summary>
public class PurchaseSupplyTracking : BaseEntity
{
    /// <summary><see cref="PurchaseSupplySourceTypes"/>: purchase_invoice | contract.</summary>
    public string SourceType { get; set; } = PurchaseSupplySourceTypes.PurchaseInvoice;
    public Guid SourceId { get; set; }
    public string SourceNumber { get; set; } = string.Empty;
    /// <summary>تاريخ المصدر (تاريخ الفاتورة أو بداية الاتفاقية): أساس فترة التقارير.</summary>
    public DateTime SourceDate { get; set; }
    public Guid? SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public Guid? WarehouseId { get; set; }
    /// <summary>موعد التوريد المتوقع؛ فارغ = بلا موعد (لا يتأخر).</summary>
    public DateTime? ExpectedDate { get; set; }
    /// <summary>
    /// <see cref="PurchaseSupplyStatuses"/> المخزنة: pending | partially_received | received تُحدَّث من الكميات بعد كل عملية،
    /// وrejected | cancelled يدويتان وثابتتان.
    /// </summary>
    public string Status { get; set; } = PurchaseSupplyStatuses.Pending;
    public DateTime? LastReceiptDate { get; set; }
    public string? Notes { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime? RejectedAt { get; set; }
    public Guid? RejectedByUserId { get; set; }
    public string? RejectedByName { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancelledByName { get; set; }
    public string? CancellationReason { get; set; }
}

/// <summary>سجل أحداث متابعة التوريد: لا يُعدَّل ولا يُحذف.</summary>
public class PurchaseSupplyEvent : BaseEntity
{
    public Guid TrackingId { get; set; }
    /// <summary>سند الاستلام أو مرتجعه المرتبط بالحدث (إن وُجد).</summary>
    public Guid? DeliveryNoteId { get; set; }
    public string? DocumentNumber { get; set; }
    /// <summary>
    /// opened | updated | receipt_created | receipt_updated | receipt_deleted | receipt_returned | receipt_return_deleted | rejected | cancelled
    /// </summary>
    public string EventType { get; set; } = string.Empty;
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string? Notes { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public DateTime OccurredAt { get; set; }
}

public static class PurchaseSupplySourceTypes
{
    public const string PurchaseInvoice = "purchase_invoice";
    public const string Contract = "contract";
    public static bool IsValid(string? t) => t is PurchaseInvoice or Contract;
}

public static class PurchaseSupplyStatuses
{
    public const string Pending = "pending";
    public const string PartiallyReceived = "partially_received";
    public const string Received = "received";
    /// <summary>محسوبة فقط: مفتوحة (معلّقة أو جزئية) وتجاوزت موعد التوريد.</summary>
    public const string Delayed = "delayed";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";

    /// <summary>الرفض والإلغاء يغلقان المتابعة: لا سندات جديدة من مصدرها.</summary>
    public static bool IsClosedManually(string? s) => s is Rejected or Cancelled;
}

/// <summary>أنواع مالك المرفقات المشتركة لاستلام المشتريات.</summary>
public static class PurchaseReceiptAttachmentOwnerTypes
{
    /// <summary>مستندات متابعة التوريد (مراسلات المورد، بوليصة الشحن...).</summary>
    public const string Tracking = "purchase_supply_tracking";
    /// <summary>صور ومستندات سند استلام المشتريات (الأصناف المستلمة والمرفوضة وإذن المورد).</summary>
    public const string Receipt = "purchase_receipt";
}
