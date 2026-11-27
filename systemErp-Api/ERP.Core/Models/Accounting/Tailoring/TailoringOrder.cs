namespace ERP.Core.Models.Accounting;

/// <summary>
/// حالات أمر التفصيل. اليدوية: مسودة ← مؤكد، والإلغاء والإغلاق. ما بين «بانتظار المواد» و«مُسلَّم» تشتقها الخدمة من حالات القطع
/// (المرحلة 3 فما بعد) فلا تُضبط باليد.
/// </summary>
public static class TailoringOrderStatuses
{
    public const string Draft = "draft";
    public const string Confirmed = "confirmed";
    public const string AwaitingMaterials = "awaiting_materials";
    public const string ReadyForProduction = "ready_for_production";
    public const string InProduction = "in_production";
    public const string AwaitingFitting = "awaiting_fitting";
    public const string ModificationRequired = "modification_required";
    public const string QualityInspection = "quality_inspection";
    public const string ReadyForDelivery = "ready_for_delivery";
    public const string PartiallyDelivered = "partially_delivered";
    public const string Delivered = "delivered";
    public const string Closed = "closed";
    public const string Cancelled = "cancelled";

    public static readonly string[] All =
    {
        Draft, Confirmed, AwaitingMaterials, ReadyForProduction, InProduction, AwaitingFitting, ModificationRequired, QualityInspection,
        ReadyForDelivery, PartiallyDelivered, Delivered, Closed, Cancelled,
    };

    /// <summary>بعد التأكيد وقبل التسليم الكامل: الأمر قائم.</summary>
    public static readonly string[] Open =
    {
        Confirmed, AwaitingMaterials, ReadyForProduction, InProduction, AwaitingFitting, ModificationRequired, QualityInspection, ReadyForDelivery, PartiallyDelivered,
    };

    public static bool IsValid(string? v) => v != null && All.Contains(v);
}

/// <summary>مصدر قماش التشكيلة: قماش العميل لا يدخل المخزون ولا التكلفة (عهدة فقط)، وقماش المحل يُصرف من المخزون فعلياً.</summary>
public static class TailoringFabricSources
{
    public const string Customer = "customer";
    public const string Shop = "shop";
    public static bool IsValid(string? v) => v is Customer or Shop;
}

public static class TailoringPriorities
{
    public const string Normal = "normal";
    public const string Urgent = "urgent";
    public static bool IsValid(string? v) => v is Normal or Urgent;
}

/// <summary>
/// أمر تفصيل لعميل واحد: لقطة العميل، والمواعيد، والمبالغ (من أسطره، محسوبة في الخادم)، والدفعة المقدمة بفاتورتها الضريبية (مرجعها
/// <c>tailoring_advance</c>، صافيها على «دفعات مقدمة من عملاء الخياطة» حتى يُسترد عند التسليم). المسودة تُطبع عرض سعر.
/// </summary>
public class TailoringOrder : BaseEntity
{
    public string OrderNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public Guid? WarehouseId { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDeliveryDate { get; set; }
    public string Priority { get; set; } = TailoringPriorities.Normal;
    public string Status { get; set; } = TailoringOrderStatuses.Draft;
    public string? Notes { get; set; }
    public int PieceCount { get; set; }

    /// <summary>مجموع (الكمية × سعر الوحدة) قبل الخصم.</summary>
    public decimal GrossAmount { get; set; }
    public decimal DiscountTotal { get; set; }
    /// <summary>الصافي قبل الضريبة.</summary>
    public decimal NetTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }

    // ---------- الدفعة المقدمة ----------
    public Guid? AdvanceInvoiceId { get; set; }
    public string? AdvanceInvoiceNumber { get; set; }
    /// <summary>إجمالي فاتورة الدفعة شاملاً الضريبة.</summary>
    public decimal AdvanceAmount { get; set; }
    /// <summary>صافي الدفعة على حساب الالتزام (يُسترد عند التسليم).</summary>
    public decimal AdvanceNet { get; set; }
    public decimal AdvanceVat { get; set; }
    /// <summary>ما استُرد من صافي الدفعة في فواتير التسليم (المرحلة 5).</summary>
    public decimal AdvanceRecovered { get; set; }
    public DateTime? AdvanceAt { get; set; }

    public DateTime? ConfirmedAt { get; set; }
    public string? ConfirmedBy { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public DateTime? ClosedAt { get; set; }
    /// <summary>قيد نقل باقي التكلفة عند الإغلاق (ما قُيّد على الأمر بعد آخر تسليم).</summary>
    public Guid? CloseCostJournalEntryId { get; set; }
    public decimal CloseCostAmount { get; set; }
    public string? CreatedBy { get; set; }
    public Guid? CreatedByUserId { get; set; }
}

/// <summary>
/// تشكيلة في الأمر: نوع لباس بقياس لابس (لقطة النسخة) وتصميم (لقطة الخيارات بأسعارها) وقماش، بكمية من القطع المتماثلة. السعر المقترح
/// = الأساسي + فروق التصميم + (أمتار قماش المحل × سعره)؛ المخالف والخصم يتطلبان «خصم يدوي».
/// </summary>
public class TailoringOrderLine : BaseEntity
{
    public Guid OrderId { get; set; }
    public int SortOrder { get; set; }
    public Guid GarmentTypeId { get; set; }
    public string GarmentTypeName { get; set; } = string.Empty;
    public Guid? ProductId { get; set; }
    public Guid? MeasurementProfileId { get; set; }
    public Guid? MeasurementVersionId { get; set; }
    public string? WearerName { get; set; }
    public string? MeasurementUnit { get; set; }
    /// <summary>لقطة قيم القياس عند الحفظ (لا تتغير بقياس لاحق).</summary>
    public string? MeasurementsJson { get; set; }
    /// <summary>لقطة صفات الجسم (مفتاح ← رمز).</summary>
    public string? MeasurementChoicesJson { get; set; }
    /// <summary>لقطة التصميم: لكل خيار مفتاحه واسمه وقيمته وفرق سعرها.</summary>
    public string DesignJson { get; set; } = "[]";
    public string FabricSource { get; set; } = TailoringFabricSources.Shop;
    public Guid? FabricProductId { get; set; }
    public string? FabricName { get; set; }
    /// <summary>الأمتار التقديرية للقطعة (للتسعير؛ الاستهلاك الفعلي من الصرف).</summary>
    public decimal FabricMeters { get; set; }
    public decimal FabricUnitPrice { get; set; }
    // قماش العميل: عهدة بلا مخزون ولا تكلفة
    public string? CustomerFabricDescription { get; set; }
    public decimal? CustomerFabricMeters { get; set; }
    public string? CustomerFabricColor { get; set; }
    public DateTime? CustomerFabricReceivedAt { get; set; }

    public int Quantity { get; set; } = 1;
    public decimal BasePrice { get; set; }
    public decimal OptionsPrice { get; set; }
    public decimal FabricPrice { get; set; }
    /// <summary>السعر المقترح للقطعة (الأساسي + التصميم + القماش).</summary>
    public decimal SuggestedUnitPrice { get; set; }
    public decimal UnitPrice { get; set; }
    public bool PriceOverridden { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Instructions { get; set; }
}

/// <summary>حالة القطعة الواحدة (تُفصَّل في المرحلة 3: الإنتاج والبروفة والجودة والتسليم).</summary>
public static class TailoringGarmentStatuses
{
    public const string Pending = "pending";
    public const string InProduction = "in_production";
    public const string AwaitingFitting = "awaiting_fitting";
    public const string Modification = "modification";
    public const string Quality = "quality";
    public const string Ready = "ready";
    public const string Delivered = "delivered";
    public const string Cancelled = "cancelled";
}

/// <summary>قطعة واحدة من تشكيلة (ت6): تُنشأ عند تأكيد الأمر بعدد الكمية، برمز يتتبعها مستقلة في الإنتاج والتسليم.</summary>
public class TailoringGarment : BaseEntity
{
    public Guid OrderId { get; set; }
    public Guid LineId { get; set; }
    public int PieceNo { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Status { get; set; } = TailoringGarmentStatuses.Pending;
    public Guid? CurrentStageId { get; set; }
    public Guid? AssignedEmployeeId { get; set; }
    /// <summary>بداية المرحلة الحالية (مدة المرحلة في السجل).</summary>
    public DateTime? StageStartedAt { get; set; }
    public DateTime? ReadyAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public Guid? DeliveryId { get; set; }
}

/// <summary>سجل انتقالات حالة الأمر بسببها ومن أجراها.</summary>
public class TailoringOrderStatusHistory : BaseEntity
{
    public Guid OrderId { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string? ChangedBy { get; set; }
    public Guid? ChangedByUserId { get; set; }
    public DateTime ChangedAt { get; set; }
}

/// <summary>مالكو مرفقات الخياطة: صور المرجع والتصميم ومستندات الأمر.</summary>
public static class TailoringAttachmentOwnerTypes
{
    public const string Order = "tailoring_order";
}
