using ERP.Core.Models.Accounting;

namespace ERP.Core.DTOs.Accounting;

public class SaveTailoringOrderLineDto
{
    public Guid? Id { get; set; }
    public Guid GarmentTypeId { get; set; }
    /// <summary>ملف القياس (لقطة نسخته الحالية تُحفظ على السطر)؛ إلزامي عند التأكيد.</summary>
    public Guid? MeasurementProfileId { get; set; }
    public string? WearerName { get; set; }
    /// <summary>مفتاح الخيار ← رمز القيمة (للاختيار) أو النص/الرقم/true|false.</summary>
    public Dictionary<string, string?> Design { get; set; } = new();
    public string FabricSource { get; set; } = TailoringFabricSources.Shop;
    public Guid? FabricProductId { get; set; }
    /// <summary>فارغ = أمتار النوع التقديرية.</summary>
    public decimal? FabricMeters { get; set; }
    public string? CustomerFabricDescription { get; set; }
    public decimal? CustomerFabricMeters { get; set; }
    public string? CustomerFabricColor { get; set; }
    public int Quantity { get; set; } = 1;
    /// <summary>فارغ = السعر المقترح؛ المخالف يتطلب «خصم يدوي».</summary>
    public decimal? UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? Instructions { get; set; }
}

public class SaveTailoringOrderDto
{
    public Guid CustomerId { get; set; }
    public Guid? WarehouseId { get; set; }
    public DateTime? OrderDate { get; set; }
    public DateTime? ExpectedDeliveryDate { get; set; }
    public string Priority { get; set; } = TailoringPriorities.Normal;
    public string? Notes { get; set; }
    public List<SaveTailoringOrderLineDto> Lines { get; set; } = new();
}

public class TailoringDesignChoiceDto
{
    public string Key { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string InputType { get; set; } = TailoringOptionInputTypes.Select;
    public string? Value { get; set; }
    public string? ValueNameAr { get; set; }
    public string? ValueNameEn { get; set; }
    public decimal PriceDelta { get; set; }
}

public class TailoringOrderLineDto
{
    public Guid Id { get; set; }
    public int SortOrder { get; set; }
    public Guid GarmentTypeId { get; set; }
    public string GarmentTypeName { get; set; } = string.Empty;
    public Guid? MeasurementProfileId { get; set; }
    public Guid? MeasurementVersionId { get; set; }
    public int? MeasurementVersionNo { get; set; }
    public string? WearerName { get; set; }
    public string? MeasurementUnit { get; set; }
    public Dictionary<string, decimal?> Measurements { get; set; } = new();
    public Dictionary<string, string?> MeasurementChoices { get; set; } = new();
    public List<TailoringDesignChoiceDto> Design { get; set; } = new();
    public string FabricSource { get; set; } = TailoringFabricSources.Shop;
    public Guid? FabricProductId { get; set; }
    public string? FabricName { get; set; }
    public decimal FabricMeters { get; set; }
    public decimal FabricUnitPrice { get; set; }
    public string? CustomerFabricDescription { get; set; }
    public decimal? CustomerFabricMeters { get; set; }
    public string? CustomerFabricColor { get; set; }
    public int Quantity { get; set; }
    public decimal BasePrice { get; set; }
    public decimal OptionsPrice { get; set; }
    public decimal FabricPrice { get; set; }
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

public class TailoringGarmentDto
{
    public Guid Id { get; set; }
    public Guid LineId { get; set; }
    public int PieceNo { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? ReadyAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public Guid? DeliveryId { get; set; }
    public Guid? StageId { get; set; }
    public string? StageNameAr { get; set; }
    public string? StageNameEn { get; set; }
    public DateTime? StageStartedAt { get; set; }
    public Guid? EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public DateTime? NextFittingAt { get; set; }
    /// <summary>تعديلات مفتوحة (ومنها ما بعد التسليم).</summary>
    public int OpenModifications { get; set; }
    public bool AfterSalesOpen { get; set; }
}

public class TailoringOrderHistoryDto
{
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string? ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; }
}

public class TailoringOrderDto
{
    public Guid Id { get; set; }
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
    public decimal GrossAmount { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal NetTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public Guid? AdvanceInvoiceId { get; set; }
    public string? AdvanceInvoiceNumber { get; set; }
    public decimal AdvanceAmount { get; set; }
    public decimal AdvanceNet { get; set; }
    public decimal AdvanceVat { get; set; }
    public decimal AdvanceRecovered { get; set; }
    public DateTime? AdvanceAt { get; set; }
    /// <summary>المتبقي على العميل = الإجمالي − الدفعة المقدمة (التحصيل الفعلي من الفواتير والسندات).</summary>
    public decimal Remaining { get; set; }
    /// <summary>الحد الأدنى للدفعة المقدمة من الإعداد (للتنبيه).</summary>
    public decimal MinAdvanceAmount { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public string? ConfirmedBy { get; set; }
    public string? CancelReason { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsLate { get; set; }
    public List<TailoringOrderLineDto> Lines { get; set; } = new();
    public List<TailoringGarmentDto> Garments { get; set; } = new();
    public List<TailoringOrderHistoryDto> History { get; set; } = new();
    /// <summary>دفعات الأمر: فاتورة العربون وفواتير التسليم القائمة (التسوية بلا فاتورة لا تظهر).</summary>
    public List<TailoringPaymentDto> Payments { get; set; } = new();
    /// <summary>المحصّل فعلاً: الفاتورة غير الآجلة مدفوعة بإصدارها، والآجلة بما خُصص لها من سندات القبض.</summary>
    public decimal PaidTotal { get; set; }
    /// <summary>المستحق على العميل = الإجمالي − المحصّل (لا يقل عن صفر).</summary>
    public decimal BalanceDue { get; set; }
    /// <summary>unpaid | partial | paid</summary>
    public string PaymentStatus { get; set; } = "unpaid";
}

/// <summary>دفعة على أمر التفصيل كما يحسبها السرفر (لا حساب مالي في الواجهة).</summary>
public class TailoringPaymentDto
{
    /// <summary>advance | delivery</summary>
    public string Kind { get; set; } = "advance";
    public Guid InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    /// <summary>رقم التسليم لفاتورة التسليم.</summary>
    public string? Reference { get; set; }
    public DateTime Date { get; set; }
    public decimal Total { get; set; }
    public decimal Vat { get; set; }
    public decimal Paid { get; set; }
    /// <summary>المستحق على الأمر بعد هذه الدفعة (للإيصال).</summary>
    public decimal BalanceAfter { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string? PaymentMethodCode { get; set; }
}

public class TailoringOrderRowDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDeliveryDate { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public int PieceCount { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal AdvanceAmount { get; set; }
    public decimal Remaining { get; set; }
    public string? AdvanceInvoiceNumber { get; set; }
}

public class TailoringAdvanceRequestDto
{
    /// <summary>مبلغ الدفعة شاملاً الضريبة.</summary>
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public string? PaymentMethodCode { get; set; }
    public DateTime? Date { get; set; }
    public string? Notes { get; set; }
}

public class TailoringReasonDto
{
    public string Reason { get; set; } = string.Empty;
}
