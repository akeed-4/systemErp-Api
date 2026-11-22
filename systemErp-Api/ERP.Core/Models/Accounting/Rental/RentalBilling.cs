namespace ERP.Core.Models.Accounting;

/// <summary>إعداد التأجير للمنشأة (سطر واحد): غرامة التأخير (O6) والتأمين قبل التسليم.</summary>
public class RentalSettings : BaseEntity
{
    /// <summary>غرامة التأخير لكل يوم = السعر اليومي × المعامل (0 = بلا غرامة).</summary>
    public decimal LateFeeFactor { get; set; } = 1m;
    /// <summary>ساعات السماح بعد النهاية المخططة قبل احتساب التأخير.</summary>
    public decimal LateGraceHours { get; set; } = 2m;
    /// <summary>يُشترط استلام التأمين المطلوب قبل أول تسليم (R3b).</summary>
    public bool RequireDepositBeforeDelivery { get; set; }
    /// <summary>طريقة الفوترة الافتراضية للعقود الجديدة (T1/Q1).</summary>
    public string DefaultBillingMode { get; set; } = RentalBillingModes.Timesheet;
    /// <summary>اعتماد كشف الساعات يتطلب صورة الكشف الموقّع مرفقة (Q2).</summary>
    public bool RequireTimesheetPhoto { get; set; } = true;
}

/// <summary>طبيعة الرسم: إضافي، نقل، وقود، تلف، تأخير، تنظيف، أخرى.</summary>
public static class RentalChargeKinds
{
    public const string Overtime = "overtime";
    public const string Transport = "transport";
    public const string Fuel = "fuel";
    public const string Damage = "damage";
    public const string Late = "late";
    public const string Cleaning = "cleaning";
    public const string Other = "other";
    public static readonly string[] All = { Overtime, Transport, Fuel, Damage, Late, Cleaning, Other };
    public static bool IsValid(string? v) => v != null && All.Contains(v);
    /// <summary>التعويضات والغرامات: إيرادها حساب التعويضات، وتصنيفها الضريبي يُقرر لكل رسم (D11).</summary>
    public static bool IsCompensation(string kind) => kind is Damage or Late;
}

/// <summary>التصنيف الضريبي للرسم: خاضع بالنسبة الأساسية، أو خارج النطاق (الفئة O، O7).</summary>
public static class RentalTaxTreatments
{
    public const string Standard = "standard";
    public const string OutOfScope = "out_of_scope";
    public static bool IsValid(string? v) => v is Standard or OutOfScope;
}

/// <summary>
/// نوع رسم تأجير (إعداد): طبيعته وحساب إيراده (فارغ = دور الإيراد أو التعويضات بحسب الطبيعة) وتصنيفه الافتراضي، و«يتطلب تصنيفاً»
/// فلا يُعتمد رسم منه إلا بتصنيف صريح (D11).
/// </summary>
public class RentalChargeType : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string Kind { get; set; } = RentalChargeKinds.Other;
    public string? RevenueAccountCode { get; set; }
    public string DefaultTaxTreatment { get; set; } = RentalTaxTreatments.Standard;
    public bool RequiresClassification { get; set; }
    public bool IsActive { get; set; } = true;
}

public static class RentalChargeStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Invoiced = "invoiced";
    public const string Waived = "waived";
}

/// <summary>
/// رسم على عقد التأجير (اختيارياً على سطر): كميته وسعره ومبلغه قبل الضريبة وتصنيفه الضريبي، ومصدره (يدوي، محضر، نقل، توليد آلي للإضافي
/// والتأخير). معلّق ← معتمد ← مفوتر، أو معفى بسبب بالتفويض المقيد.
/// </summary>
public class RentalCharge : BaseEntity
{
    public Guid ContractId { get; set; }
    public Guid? LineId { get; set; }
    public Guid ChargeTypeId { get; set; }
    public string Kind { get; set; } = RentalChargeKinds.Other;
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    /// <summary>فارغ = لم يُصنَّف بعد (يلزم لنوع يتطلب تصنيفاً قبل الاعتماد).</summary>
    public string? TaxTreatment { get; set; }
    public string SourceType { get; set; } = "manual";
    public Guid? SourceId { get; set; }
    public string Status { get; set; } = RentalChargeStatuses.Pending;
    public Guid? BillingId { get; set; }
    public string? WaiveReason { get; set; }
    public string? WaivedBy { get; set; }
    public string? ApprovedBy { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
}

public static class RentalBillingStatuses
{
    public const string Invoiced = "invoiced";
    public const string Cancelled = "cancelled";
}

/// <summary>
/// فوترة فترة لعقد التأجير: فاتورة مبيعات (مرجعها <c>rental_billing</c>، تُلغى من الفوترة وحدها) بإيجار الأسطر حتى لحظة الفوترة والرسوم
/// المعتمدة. كل سطر يحفظ ما كان مفوتراً قبله ليُعكس بالإلغاء (آخر فوترة فقط).
/// </summary>
public class RentalBilling : BaseEntity
{
    public string BillingNumber { get; set; } = string.Empty;
    public Guid ContractId { get; set; }
    public DateTime BilledUntil { get; set; }
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public decimal RentAmount { get; set; }
    public decimal ChargesAmount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = RentalBillingStatuses.Invoiced;
    public string? CancelReason { get; set; }
    public string? CreatedBy { get; set; }
    public virtual ICollection<RentalBillingLine> Lines { get; set; } = new List<RentalBillingLine>();
}

/// <summary>إيجار سطر في الفوترة: الفترة ومبلغها، وما كان مفوتراً قبلها (للإلغاء).</summary>
public class RentalBillingLine : BaseEntity
{
    public Guid BillingId { get; set; }
    public Guid ContractLineId { get; set; }
    public DateTime PeriodFrom { get; set; }
    public DateTime PeriodTo { get; set; }
    public decimal Amount { get; set; }
    public DateTime? PreviousBilledThrough { get; set; }
    public decimal PreviousBilledAmount { get; set; }
    public virtual RentalBilling Billing { get; set; } = null!;
}
