namespace ERP.Core.Models.Accounting;

/// <summary>نوع المحضر: تسليم المعدات للعميل (تخرج) أو استلامها منه (تعود).</summary>
public static class RentalHandoverKinds
{
    public const string Delivery = "delivery";
    public const string Return = "return";
    public static bool IsValid(string? v) => v is Delivery or Return;
}

/// <summary>أنواع مالكي المرفقات المشتركة في التأجير: صور المحضر وتوقيعه، ومستندات العقد الموقّعة.</summary>
public static class RentalAttachmentOwnerTypes
{
    public const string Handover = "rental_handover";
    public const string Contract = "rental_contract";
    /// <summary>صورة كشف الساعات الموقّع من العميل (T1).</summary>
    public const string Timesheet = "rental_timesheet";
}

public static class RentalHandoverStatuses
{
    public const string Posted = "posted";
    /// <summary>أُلغي بسبب فعاد أثره على الأسطر والمعدات.</summary>
    public const string Cancelled = "cancelled";
}

/// <summary>حالة المعدة في المحضر: سليمة، ملاحظات بسيطة، تالفة (يُبنى عليها رسم الضرر في الفوترة).</summary>
public static class RentalConditions
{
    public const string Good = "good";
    public const string Fair = "fair";
    public const string Damaged = "damaged";
    public static bool IsValid(string? v) => v is Good or Fair or Damaged;
}

/// <summary>
/// محضر تسليم أو استلام معدات عقد تأجير في لحظة فعلية، بمستلمه أو مسلّمه من طرف العميل. يُرحَّل عند الحفظ: التسليم يُخرج أسطره
/// (<see cref="RentalLineStatuses.Out"/>) والاستلام يعيدها، ويحدّث عدّاد المعدة وحالتها. الصور والتوقيع مرفقات مشتركة على المحضر.
/// الإلغاء بسبب يعكس أثره ما دام آخر حركة على أسطره.
/// </summary>
public class RentalHandover : BaseEntity
{
    public string HandoverNumber { get; set; } = string.Empty;
    public Guid ContractId { get; set; }
    public string Kind { get; set; } = RentalHandoverKinds.Delivery;
    public string Status { get; set; } = RentalHandoverStatuses.Posted;
    /// <summary>لحظة التسليم أو الاستلام الفعلية (توقيت المنشأة).</summary>
    public DateTime HandoverAt { get; set; }
    /// <summary>الموقع (المستودع) الذي خرجت منه أو عادت إليه.</summary>
    public Guid? WarehouseId { get; set; }
    /// <summary>مكان التسليم لدى العميل (الموقع أو العنوان).</summary>
    public string? SiteLocation { get; set; }
    /// <summary>ممثل العميل المستلم أو المسلّم وهويته وجواله.</summary>
    public string? CounterpartyName { get; set; }
    public string? CounterpartyIdNumber { get; set; }
    public string? CounterpartyPhone { get; set; }
    public string? Notes { get; set; }
    public string? CancelReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CreatedBy { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public virtual ICollection<RentalHandoverLine> Lines { get; set; } = new List<RentalHandoverLine>();
}

/// <summary>
/// معدة في المحضر: قراءة العدّاد ومستوى الوقود (نسبة) وحالتها وملحقاتها. <see cref="MeterBefore"/> عدّاد المعدة قبل المحضر لإعادته عند
/// الإلغاء. <see cref="CarriedFromLineId"/>: تسليم سطر عقد مجدِّد والمعدة ما زالت عند العميل على العقد السابق، فيُستلم سطر السابق في
/// اللحظة والعدّاد نفسيهما دون حركة فعلية.
/// </summary>
public class RentalHandoverLine : BaseEntity
{
    public Guid HandoverId { get; set; }
    public Guid ContractLineId { get; set; }
    public Guid EquipmentId { get; set; }
    public decimal? Meter { get; set; }
    public decimal? MeterBefore { get; set; }
    /// <summary>مستوى الوقود نسبةً من 0 إلى 100.</summary>
    public decimal? FuelPercent { get; set; }
    public string Condition { get; set; } = RentalConditions.Good;
    public string? ConditionNotes { get; set; }
    /// <summary>الملحقات والمعدات المرافقة (نص حر).</summary>
    public string? Accessories { get; set; }
    public Guid? CarriedFromLineId { get; set; }
    public int SortOrder { get; set; }
    public virtual RentalHandover Handover { get; set; } = null!;
}
