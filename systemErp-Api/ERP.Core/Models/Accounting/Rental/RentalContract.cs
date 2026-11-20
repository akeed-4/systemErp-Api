namespace ERP.Core.Models.Accounting;

/// <summary>حالة عقد التأجير: مسودة (تحجز فترات معداتها) ← ساري ← مغلق، أو ملغى بسبب.</summary>
public static class RentalContractStatuses
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Closed = "closed";
    public const string Cancelled = "cancelled";

    /// <summary>العقود التي تحجز فترات أسطرها.</summary>
    public static readonly string[] Blocking = { Draft, Active };
}

/// <summary>حالة سطر العقد: بانتظار التسليم، خارج (مسلَّمة للعميل)، مُعادة، ملغاة.</summary>
public static class RentalLineStatuses
{
    public const string Pending = "pending";
    public const string Out = "out";
    public const string Returned = "returned";
    public const string Cancelled = "cancelled";

    public static readonly string[] Blocking = { Pending, Out };
}

/// <summary>مدة العقد: قصير (أيام/أسابيع) أو طويل (شهور).</summary>
public static class RentalTerms
{
    public const string Short = "short";
    public const string Long = "long";
    public static bool IsValid(string? v) => v is Short or Long;
}

/// <summary>دورة الفوترة: عند نهاية العقد، أسبوعياً، شهرياً (O3: الطويل شهري مقدماً، والقصير مؤخراً افتراضياً).</summary>
public static class RentalBillingCycles
{
    public const string EndOfContract = "end";
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";
    public static bool IsValid(string? v) => v is EndOfContract or Weekly or Monthly;
}

/// <summary>نوع تغيير العقد المسجّل: تمديد نهاية الأسطر، أو تجديد بعقد لاحق.</summary>
public static class RentalContractChangeKinds
{
    public const string Extension = "extension";
    public const string Renewal = "renewal";
}

/// <summary>
/// عقد تأجير معدات لعميل (D8: مستقل عن <see cref="CommercialContract"/>) بموقعه التشغيلي، ومدته، وشروط فوترته، وتأمينه المطلوب.
/// أسطره معدات بفتراتها وأسعارها المثبّتة؛ التجديد عقد جديد مرتبط بسابقه (<see cref="ParentContractId"/>).
/// </summary>
public class RentalContract : BaseEntity
{
    public string ContractNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public Guid? WarehouseId { get; set; }
    /// <summary>الحجز الذي حُوِّل إليه، إن وُجد.</summary>
    public Guid? BookingId { get; set; }
    public Guid? ParentContractId { get; set; }
    public string Status { get; set; } = RentalContractStatuses.Draft;
    public string Term { get; set; } = RentalTerms.Short;
    public string BillingCycle { get; set; } = RentalBillingCycles.EndOfContract;
    /// <summary>الفوترة مقدماً لكل دورة (وإلا مؤخراً).</summary>
    public bool BillInAdvance { get; set; }
    /// <summary>التأمين النقدي المطلوب قبل التسليم (يُستلم بسند قبض في الفوترة).</summary>
    public decimal DepositRequired { get; set; }
    /// <summary>أول بداية وآخر نهاية مخططة بين أسطره (تُحدَّث مع الأسطر والتمديد).</summary>
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? Terms { get; set; }
    public string? Notes { get; set; }
    public string? CancelReason { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public string? ActivatedBy { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? CreatedBy { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public virtual ICollection<RentalContractLine> Lines { get; set; } = new List<RentalContractLine>();
}

/// <summary>
/// معدة في عقد التأجير: فترتها المخططة والفعلية (التسليم والاستلام)، وعدّادها عند الخروج والعودة، وسعرها وشروطه لقطة عند الحفظ
/// (الأساس والسعر وسعر المشغّل والساعات المشمولة وسعر الإضافي والحد الأدنى)، والمشغّل إن كانت بمشغّل. فوترتها تتقدم بـ <see cref="BilledThrough"/>.
/// </summary>
public class RentalContractLine : BaseEntity
{
    public Guid ContractId { get; set; }
    public Guid EquipmentId { get; set; }
    public string Status { get; set; } = RentalLineStatuses.Pending;
    public DateTime PlannedFrom { get; set; }
    public DateTime PlannedTo { get; set; }
    public DateTime? ActualOut { get; set; }
    public DateTime? ActualIn { get; set; }
    public decimal? MeterOut { get; set; }
    public decimal? MeterIn { get; set; }
    public string Basis { get; set; } = RentalRateBases.Day;
    public decimal Rate { get; set; }
    /// <summary>السعر مُدخل يدوياً بخلاف سعر القائمة (يتطلب «خصم يدوي» على شاشة العقود).</summary>
    public bool RateOverridden { get; set; }
    public bool WithOperator { get; set; }
    public decimal OperatorRate { get; set; }
    public Guid? OperatorEmployeeId { get; set; }
    public decimal IncludedHours { get; set; }
    public decimal OvertimeRate { get; set; }
    public decimal MinimumPeriods { get; set; }
    public decimal EstimatedAmount { get; set; }
    /// <summary>آخر لحظة فُوترت حتى نهايتها (الفوترة الدورية).</summary>
    public DateTime? BilledThrough { get; set; }
    public int SortOrder { get; set; }
    public string? Notes { get; set; }
    public virtual RentalContract Contract { get; set; } = null!;
}

/// <summary>سجل تغيير العقد: تمديد (النهاية القديمة والجديدة لكل سطر) أو تجديد (العقد الجديد)، بسببه ومن أجراه.</summary>
public class RentalContractChange : BaseEntity
{
    public Guid ContractId { get; set; }
    public string Kind { get; set; } = RentalContractChangeKinds.Extension;
    public Guid? LineId { get; set; }
    public DateTime? OldEnd { get; set; }
    public DateTime? NewEnd { get; set; }
    public Guid? NewContractId { get; set; }
    public string? Reason { get; set; }
    public string? CreatedBy { get; set; }
}
