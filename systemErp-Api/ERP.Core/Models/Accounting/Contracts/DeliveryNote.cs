
namespace ERP.Core.Models.Accounting;

public class DeliveryNote : BaseEntity, IScopedRecord, IPartyDocument
{
    /// <summary>منشئ المستند (يُختم آلياً) لنطاق «السجلات التي أنشأها».</summary>
    public Guid? CreatedByUserId { get; set; }
    /// <summary>المستخدم المسند إليه المستند لنطاق «السجلات المسندة إليه».</summary>
    public Guid? AssignedToUserId { get; set; }
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

    // ---------- سند استلام مشتريات مشروع (purchase_delivery على عقد مقاولات) ----------
    /// <summary>طلب شراء المشروع المعتمد الذي يُستلم عليه؛ فارغ = سند عادي (الموقع والفوترة كما كانا).</summary>
    public Guid? RequisitionId { get; set; }
    /// <summary>عهدة المشروع المنشأة من السند (<c>ProjectMaterialIssue</c> بنوع purchase_receipt) وقيدها: لا تدخل الموقع العام.</summary>
    public Guid? CustodyIssueId { get; set; }
    /// <summary>أمر الشراء الذي يُستلم عليه (استلام جزئي): الكمية ≤ المتبقي من الأمر والسعر والمورد من الأمر.</summary>
    public Guid? PurchaseOrderId { get; set; }

    // ---------- مصدر البيان ومتابعة التوصيل (الهجرة DeliveryNoteSourcesAndTracking) ----------
    /// <summary>مصدر البيان <see cref="DeliveryNoteSources"/>: manual (القائم وسندات الاستلام) | contract | sales_invoice.</summary>
    public string SourceType { get; set; } = DeliveryNoteSources.Manual;
    /// <summary>المستند المصدر: الاتفاقية أو فاتورة البيع المرحّلة.</summary>
    public Guid? SourceId { get; set; }
    public string? SourceNumber { get; set; }
    /// <summary>
    /// حالة التوصيل <see cref="DeliveryTrackingStatuses"/> مستقلة عن الفوترة والمرتجع. untracked = غير متتبَّع (البيانات السابقة، واليدوية، وسندات الاستلام): لا يُفترض أنه سُلّم فعلياً.
    /// </summary>
    public string DeliveryStatus { get; set; } = DeliveryTrackingStatuses.Legacy;
    public DateTime? DispatchedAt { get; set; }
    /// <summary>تأكيد الاستلام (إثبات التسليم): الوقت والمستخدم واسم المستلم.</summary>
    public DateTime? DeliveredAt { get; set; }
    public Guid? DeliveryConfirmedByUserId { get; set; }
    public string? ReceiverName { get; set; }
    public DateTime? CancelledAt { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancellationReason { get; set; }

    // ---------- موقع التوصيل (الهجرة CustomerDeliveryLocations) ----------
    /// <summary>موقع العميل المختار (مرجع للمتابعة فقط)؛ المعتمد هو اللقطة أدناه.</summary>
    public Guid? DeliveryLocationId { get; set; }
    /// <summary>لقطة العنوان والإحداثيات وقت الإعداد: لا تتغير بتعديل موقع العميل.</summary>
    public string? DeliveryAddress { get; set; }
    public decimal? DeliveryLatitude { get; set; }
    public decimal? DeliveryLongitude { get; set; }
    /// <summary>مراجعة الموقع قبل الخروج (إلزامية بإعداد المنشأة)؛ تُمسح بتغيير اللقطة.</summary>
    public DateTime? LocationReviewedAt { get; set; }
    public Guid? LocationReviewedByUserId { get; set; }
    public string? LocationReviewedByName { get; set; }
    public string? LocationReviewNotes { get; set; }

    public virtual ICollection<DeliveryNoteItem> Items { get; set; } = new List<DeliveryNoteItem>();
}

/// <summary>مصدر بيان التسليم.</summary>
public static class DeliveryNoteSources
{
    /// <summary>البيانات القائمة اليدوية وسندات الاستلام (المشتريات) كما كانت.</summary>
    public const string Manual = "manual";
    /// <summary>اتفاقية/عقد عميل: يُفوتر من البيان بالكميات المؤكد تسليمها (فواتير مرحلية).</summary>
    public const string Contract = "contract";
    /// <summary>فاتورة بيع مرحّلة: مستند تشغيلي للتوصيل فقط، لا فاتورة ولا حركة ولا قيد.</summary>
    public const string SalesInvoice = "sales_invoice";
    /// <summary>فاتورة مشتريات مرحّلة: سند استلام تشغيلي فقط — الفاتورة أنشأت المخزون والقيد، فلا فاتورة ولا حركة ولا قيد من السند.</summary>
    public const string PurchaseInvoice = "purchase_invoice";
    /// <summary>
    /// نية الطلب فقط (لا تُخزَّن): بيان تسليم (عميل) أو بيان استلام (مورد) مستقل بلا اتفاقية ولا فاتورة، على طرف مسجَّل. يُحفظ
    /// <see cref="Manual"/> بلا عقد وبلا متابعة توصيل، فيُفوتر مباشرة (وحده أو مع بيانات الطرف الأخرى)، والمخزون يتحرك بالفاتورة
    /// من موقع كل سطر — كبيان الاتفاقية، فلا حركة مكررة.
    /// </summary>
    public const string Standalone = "standalone";

    /// <summary>بيان مستقل: يدوي بلا عقد ولا طلب ولا أمر، على طرف مسجَّل (عميل للتسليم، مورد للاستلام).</summary>
    public static bool IsStandalone(DeliveryNote n)
        => n.SourceType == Manual && n.ContractId == null && n.RequisitionId == null && n.PurchaseOrderId == null && n.PartyId != null;
}

/// <summary>حالات التوصيل (مستقلة عن <see cref="DeliveryNoteStatus"/> للفوترة والمرتجع).</summary>
public static class DeliveryTrackingStatuses
{
    public const string Legacy = "untracked";
    public const string Pending = "pending";
    public const string InTransit = "in_transit";
    public const string PartiallyDelivered = "partially_delivered";
    public const string Delivered = "delivered";
    public const string Cancelled = "cancelled";

    /// <summary>بيان متتبَّع (أُنشئ بعد التتبع): قواعد التأكيد والإلغاء والفوترة بالمؤكد.</summary>
    public static bool IsTracked(string? status) => status is not null && status != Legacy;
    public static bool IsConfirmed(string? status) => status is PartiallyDelivered or Delivered;
}
