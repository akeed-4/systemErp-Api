namespace ERP.Core.Models.Accounting;

/// <summary>
/// إعداد الورشة للمنشأة (سطر واحد): سعر ساعة البيع الافتراضي، ومدة حجز القطع، والضمان الافتراضي، والموقع الافتراضي للاستقبال والقطع.
/// </summary>
public class WorkshopSettings : BaseEntity
{
    /// <summary>سعر بيع ساعة العمالة الافتراضي قبل الضريبة (الخدمة المسعّرة بالساعة بلا سعر خاص).</summary>
    public decimal DefaultLaborRate { get; set; }
    /// <summary>مدة حجز القطع لأمر العمل بالأيام قبل انتهائه (0 = بلا انتهاء).</summary>
    public int ReservationHoldDays { get; set; } = 7;
    public int DefaultWarrantyMonths { get; set; }
    public int DefaultWarrantyKm { get; set; }
    public Guid? DefaultWarehouseId { get; set; }
    /// <summary>فحص الاستقبال إلزامي قبل نقل أمر العمل من «مستلم».</summary>
    public bool RequireReceptionInspection { get; set; }
}

/// <summary>طريقة تسعير الخدمة: مبلغ ثابت، أو بالساعات المعيارية × سعر الساعة.</summary>
public static class WorkshopPricingMethods
{
    public const string Fixed = "fixed";
    public const string Hourly = "hourly";
    public static bool IsValid(string? v) => v is Fixed or Hourly;
}

/// <summary>
/// مرحلة فرعية قابلة للتهيئة داخل حالة ثابتة لأمر العمل (مثلاً «بانتظار القطع» داخل «متوقف»، أو «سمكرة» و«دهان» داخل «قيد التنفيذ»).
/// الحالة نفسها ثابتة في الكود (<see cref="WorkOrderStatuses"/>) وعليها تُبنى القواعد.
/// </summary>
public class WorkshopStage : BaseEntity
{
    public string Status { get; set; } = WorkOrderStatuses.InProgress;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>سبب توقف أمر العمل أو الفني (بانتظار قطع، بانتظار موافقة، عطل معدات...).</summary>
public class WorkshopPauseReason : BaseEntity
{
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// تعريف خدمة الورشة فوق صنف خدمي من الأصناف القائمة (الإيراد والضريبة من الصنف): ساعاتها المعيارية، وطريقة تسعيرها، وضمانها.
/// </summary>
public class WorkshopServiceDefinition : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public decimal StandardHours { get; set; }
    public string PricingMethod { get; set; } = WorkshopPricingMethods.Hourly;
    /// <summary>السعر الثابت (للطريقة الثابتة)، أو سعر ساعة خاص بالخدمة (للطريقة بالساعة؛ فارغ = سعر الإعداد).</summary>
    public decimal? Price { get; set; }
    public int WarrantyMonths { get; set; }
    public int WarrantyKm { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>استخدام قالب الفحص: الاستقبال، التشخيص، فحص الجودة قبل التسليم.</summary>
public static class InspectionUsages
{
    public const string Reception = "reception";
    public const string Diagnosis = "diagnosis";
    public const string Quality = "quality";
    public static bool IsValid(string? v) => v is Reception or Diagnosis or Quality;
}

/// <summary>نوع نتيجة بند الفحص: سليم/يحتاج انتباه/فشل، أو قيمة رقمية، أو نص.</summary>
public static class InspectionResultTypes
{
    public const string Condition = "condition";
    public const string Number = "number";
    public const string Text = "text";
    public static bool IsValid(string? v) => v is Condition or Number or Text;
}

/// <summary>قالب فحص بأقسام وبنود (استقبال، تشخيص، جودة).</summary>
public class InspectionTemplate : BaseEntity
{
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string Usage { get; set; } = InspectionUsages.Reception;
    public bool IsActive { get; set; } = true;
    public virtual ICollection<InspectionTemplateItem> Items { get; set; } = new List<InspectionTemplateItem>();
}

public class InspectionTemplateItem : BaseEntity
{
    public Guid TemplateId { get; set; }
    public string? Section { get; set; }
    public string TextAr { get; set; } = string.Empty;
    public string? TextEn { get; set; }
    public string ResultType { get; set; } = InspectionResultTypes.Condition;
    /// <summary>صورة إلزامية إن كانت النتيجة «فشل».</summary>
    public bool PhotoRequiredOnFail { get; set; }
    public int SortOrder { get; set; }
    public virtual InspectionTemplate Template { get; set; } = null!;
}
