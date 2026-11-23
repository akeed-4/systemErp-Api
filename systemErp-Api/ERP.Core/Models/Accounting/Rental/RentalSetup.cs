namespace ERP.Core.Models.Accounting;

/// <summary>أساس سعر التأجير للعملاء: ساعة، يوم (24 ساعة)، أسبوع (7 أيام)، شهر (شهر تقويمي).</summary>
public static class RentalRateBases
{
    public const string Hour = "hour";
    public const string Day = "day";
    public const string Week = "week";
    public const string Month = "month";

    /// <summary>من الأكبر للأصغر (الفترة الكاملة بالأساس المختار، والكسر بأصغر أساس مسعّر).</summary>
    public static readonly string[] Descending = { Month, Week, Day, Hour };
    public static bool IsValid(string? v) => v is Hour or Day or Week or Month;
}

/// <summary>
/// فئة معدات التأجير (حفارات، رافعات، مولدات...) بفئة أم اختيارية. تسعير الفئة يسري على معداتها ما لم يكن للمعدة تسعير خاص.
/// <c>Equipment.Category</c> النصي يبقى كما هو للمقاولات (D9).
/// </summary>
public class RentalCategory : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public Guid? ParentId { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// سعر تأجير للعميل على فئة أو معدة (واحد منهما)، بأساسه وفترة سريانه. الأولوية: سعر سطر العقد ← المعدة ← الفئة. سعر المشغّل
/// إضافي على الأساس نفسه (فارغ = لا تُؤجَّر بمشغّل بهذا السعر)، والساعات المشمولة في الفترة تحدد الساعات الإضافية (0 = بلا حد).
/// منفصل عن <see cref="EquipmentRevenueRate"/> (إيراد المعدة داخل مشاريع المقاولات).
/// </summary>
public class RentalRate : BaseEntity
{
    public Guid? CategoryId { get; set; }
    public Guid? EquipmentId { get; set; }
    public string Basis { get; set; } = RentalRateBases.Day;
    public decimal Rate { get; set; }
    /// <summary>سعر المشغّل للفترة نفسها؛ فارغ = غير متاح بمشغّل.</summary>
    public decimal? OperatorRate { get; set; }
    /// <summary>ساعات التشغيل المشمولة في الفترة (للأساس اليومي فأكبر)؛ 0 = بلا حد.</summary>
    public decimal IncludedHours { get; set; }
    /// <summary>سعر ساعة التشغيل الإضافية فوق المشمول.</summary>
    public decimal OvertimeRate { get; set; }
    /// <summary>أقل عدد فترات يُحتسب (مثلاً يومان على الأقل).</summary>
    public decimal MinimumPeriods { get; set; } = 1;
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// حالة الحجز: مسودة (عرض سعر لا يحجز)، محجوز مؤقتاً حتى موعد (يحجز الفترة حتى انقضائه)، مؤكد (يحجز)، محوَّل لعقد، ملغى.
/// المؤقت بعد موعده يُعامل منتهياً فلا يحجز، وإن بقيت حالته المحفوظة.
/// </summary>
public static class RentalBookingStatuses
{
    public const string Draft = "draft";
    public const string Held = "held";
    public const string Confirmed = "confirmed";
    public const string Converted = "converted";
    public const string Cancelled = "cancelled";

    /// <summary>الحالات التي تحجز فترات معداتها.</summary>
    public static readonly string[] Blocking = { Held, Confirmed };
}

/// <summary>حجز (أو عرض سعر) تأجير لعميل بفرعه التشغيلي (الموقع/الموقع، D1). يُحوَّل لعقد تأجير في المرحلة التالية.</summary>
public class RentalBooking : BaseEntity
{
    public string BookingNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public Guid? WarehouseId { get; set; }
    public string Status { get; set; } = RentalBookingStatuses.Draft;
    /// <summary>نهاية الحجز المؤقت (وقت محلي).</summary>
    public DateTime? HoldUntil { get; set; }
    public string? Notes { get; set; }
    public string? CancelReason { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    /// <summary>عقد التأجير المحوَّل إليه.</summary>
    public Guid? ContractId { get; set; }
    public string? CreatedBy { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public virtual ICollection<RentalBookingLine> Lines { get; set; } = new List<RentalBookingLine>();
}

/// <summary>
/// سطر حجز: معدة محددة (يحجزها) أو فئة بعدد (للعرض فقط حتى تُعيَّن معداتها)، بفترته (وقت محلي) وأساس سعره ومع مشغّل أو بدونه،
/// والسعر والتقدير لقطة من الخادم عند الحفظ.
/// </summary>
public class RentalBookingLine : BaseEntity
{
    public Guid BookingId { get; set; }
    public Guid? EquipmentId { get; set; }
    public Guid? CategoryId { get; set; }
    public int Quantity { get; set; } = 1;
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public string Basis { get; set; } = RentalRateBases.Day;
    public bool WithOperator { get; set; }
    /// <summary>لقطة سعر الأساس والمشغّل عند الحفظ، والتقدير للفترة (قبل الضريبة).</summary>
    public decimal Rate { get; set; }
    public decimal OperatorRate { get; set; }
    public decimal EstimatedAmount { get; set; }
    public int SortOrder { get; set; }
    public string? Notes { get; set; }
    public virtual RentalBooking Booking { get; set; } = null!;
}
