namespace ERP.Core.DTOs.Accounting;

/// <summary>خروج البيان للتوصيل (pending ← in_transit).</summary>
public class DispatchDeliveryNoteDto
{
    public string? DriverName { get; set; }
    public string? VehiclePlate { get; set; }
    public string? Notes { get; set; }
}

/// <summary>مراجعة موقع التوصيل قبل الخروج.</summary>
public class ReviewDeliveryLocationDto
{
    public string? Notes { get; set; }
}

/// <summary>تأكيد الاستلام (إثبات التسليم): الكمية المسلّمة فعلياً لكل سطر (لا تتجاوز كمية البيان).</summary>
public class ConfirmDeliveryDto
{
    public string ReceiverName { get; set; } = string.Empty;
    /// <summary>وقت الاستلام؛ فارغ = الآن.</summary>
    public DateTime? DeliveredAt { get; set; }
    public string? Notes { get; set; }
    public List<ConfirmDeliveryLineDto> Lines { get; set; } = new();
}

public class ConfirmDeliveryLineDto
{
    /// <summary>معرّف سطر البيان.</summary>
    public Guid LineId { get; set; }
    public decimal ConfirmedQty { get; set; }
}

/// <summary>إلغاء البيان بسبب إلزامي.</summary>
public class CancelDeliveryNoteDto
{
    public string Reason { get; set; } = string.Empty;
}

public class DeliveryNoteEventDto
{
    public Guid Id { get; set; }
    public Guid DeliveryNoteId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string? Notes { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public DateTime OccurredAt { get; set; }
}

/// <summary>
/// صف نافذة اختيار مصدر البيان (فاتورة بيع/مشتريات مرحّلة أو اتفاقية): أعمدة العرض والبحث فقط بلا أسطر،
/// تُحمَّل صفحةً صفحة من <c>deliverynotes/sources/load</c>. الأسطر والكميات المتبقية من نقطة المصدر بعد الاختيار.
/// </summary>
public class DeliverySourceDocumentRowDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public DateTime? Date { get; set; }
    public Guid? PartyId { get; set; }
    public string PartyName { get; set; } = string.Empty;
    public string? PartyVatNumber { get; set; }
    public string? PartyPhone { get; set; }
    /// <summary>البيان: ملاحظات الفاتورة أو عنوان الاتفاقية.</summary>
    public string? Description { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? ProjectName { get; set; }
    public string? CostCenterName { get; set; }
    public string? CostCenterNameEn { get; set; }
    public string? WarehouseName { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>مصدر بيان التسليم (اتفاقية أو فاتورة بيع مرحّلة) بأسطره وكمياتها المتبقية للتسليم.</summary>
public class DeliverySourceDto
{
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
    public string SourceNumber { get; set; } = string.Empty;
    public Guid? PartyId { get; set; }
    public string PartyName { get; set; } = string.Empty;
    public string? PartyTaxNumber { get; set; }
    public string? PartyPhone { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? CostCenterId { get; set; }
    public List<DeliverySourceLineDto> Lines { get; set; } = new();
}

public class DeliverySourceLineDto
{
    public Guid SourceLineId { get; set; }
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string? Unit { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? CostCenterId { get; set; }
    /// <summary>سعر الوحدة قبل الضريبة (سطر الفاتورة الشاملة يُعاد بصافيه).</summary>
    public decimal UnitPrice { get; set; }
    public decimal VatRate { get; set; }
    /// <summary>صافي سطر الفاتورة قبل الضريبة بعد خصم السطر ونصيبه من خصم الفاتورة؛ البيان يأخذ منه بنسبة كميته.</summary>
    public decimal SourceNet { get; set; }
    /// <summary>الكمية في المصدر (المفوترة أو المتفق عليها).</summary>
    public decimal SourceQty { get; set; }
    /// <summary>المرتجع مالياً (إشعارات دائنة مرحّلة على سطر الفاتورة).</summary>
    public decimal FinancialReturnedQty { get; set; }
    /// <summary>صافي ما في بيانات التسليم غير الملغاة (المحجوز أو المسلَّم، بعد مرتجعات التسليم).</summary>
    public decimal DeliveredQty { get; set; }
    public decimal RemainingQty { get; set; }
    /// <summary>خدمة أو بند وصفي أو صنف غير مخزني: يبقى في الفاتورة ولا يُسلَّم ببيان.</summary>
    public bool Deliverable { get; set; } = true;
}

public class DeliveryTrackingQueryDto
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string? Search { get; set; }
    /// <summary>حالة التوصيل؛ فارغ = كل البيانات المتتبَّعة.</summary>
    public string? Status { get; set; }
    public string? SourceType { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public class DeliveryStatusSummaryDto
{
    public string SourceType { get; set; } = string.Empty;
    public string DeliveryStatus { get; set; } = string.Empty;
    public int Count { get; set; }
}
