namespace ERP.Core.Models.Accounting;

// ملكية المعدة: EquipmentOwnerships نفسها في مكتبة المقاولات (owned / rented).

/// <summary>
/// حالة المعدة: متاحة، مسندة لمشروع، في الصيانة، خارج الخدمة، أو مُعادة للمؤجر. «مسندة» و«في الصيانة» يضبطهما النظام من الإسناد
/// وأوامر العمل؛ «خارج الخدمة» و«مُعادة» يدويان لمعدة غير مسندة.
/// </summary>
public static class EquipmentStatuses
{
    public const string Available = "available";
    public const string Assigned = "assigned";
    public const string Maintenance = "maintenance";
    public const string OutOfService = "out_of_service";
    public const string Returned = "returned";
    public static readonly string[] Manual = { Available, OutOfService, Returned };
}

/// <summary>
/// سجل المعدات والآليات (على مستوى المنشأة): الهوية والتصنيف والمواصفات والملكية والحالة. المملوكة تُربط بأصلها الثابت (قيمته
/// وإهلاكه من الأصول الثابتة، بلا تكرار)، والمستأجرة بمؤجرها وعقد إيجارها. تكلفة الساعة تُنسخ لمورد المشروع عند الإسناد
/// (<see cref="ProjectResource"/>) فتُحمَّل على المشروع من التقرير اليومي كما هو قائم.
/// </summary>
public class Equipment : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    /// <summary>التصنيف (حفار، شيول، رافعة، مولد...).</summary>
    public string? Category { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public int? ManufactureYear { get; set; }
    public string? SerialNumber { get; set; }
    public string? PlateNumber { get; set; }
    /// <summary>الطاقة أو الحمولة (نص حر: 20 طن، 1.2 م3...).</summary>
    public string? Capacity { get; set; }
    public string? Specifications { get; set; }

    public string Ownership { get; set; } = EquipmentOwnerships.Owned;
    /// <summary>الأصل الثابت للمعدة المملوكة (أصل واحد لمعدة واحدة).</summary>
    public Guid? FixedAssetId { get; set; }
    /// <summary>المؤجر للمعدة المستأجرة.</summary>
    public Guid? SupplierId { get; set; }
    /// <summary>المورد المعياري في مكتبة المقاولات (اختياري).</summary>
    public Guid? LibraryResourceId { get; set; }
    /// <summary>مركز تكلفة المعدة: يتحمّل مصاريفها الفعلية (وقود، صيانة) غير المحمّلة على مشروع.</summary>
    public Guid? CostCenterId { get; set; }
    public string Status { get; set; } = EquipmentStatuses.Available;

    // تكلفة التشغيل الافتراضية (تُنسخ لمورد المشروع عند إسناد المملوكة؛ المستأجرة من عقد إيجارها)
    public string RateBasis { get; set; } = ProjectRateBases.Hour;
    public decimal Rate { get; set; }
    public decimal FuelPerHour { get; set; }
    public decimal MaintenancePerHour { get; set; }
    public decimal OperatorPerHour { get; set; }
    public decimal HoursPerDay { get; set; } = 8;
    public decimal DaysPerMonth { get; set; } = 26;

    /// <summary>عدّاد ساعات التشغيل الحالي (آخر قراءة مسجَّلة).</summary>
    public decimal MeterHours { get; set; }
    /// <summary>الصيانة الوقائية: كل كم ساعة تشغيل، وكل كم يوماً (أيهما أسبق).</summary>
    public decimal? ServiceIntervalHours { get; set; }
    public int? ServiceIntervalDays { get; set; }
    public decimal? LastServiceMeter { get; set; }
    public DateTime? LastServiceDate { get; set; }
    public string? Notes { get; set; }
}

public static class EquipmentAssignmentStatuses
{
    public const string Active = "active";
    public const string Returned = "returned";
    public const string Transferred = "transferred";
}

/// <summary>
/// إسناد معدة لمشروع وموقع بمشغّلها (موظف من شؤون الموظفين). الإسناد ينشئ مورد المشروع (<see cref="ProjectResourceId"/>) فتُسجَّل
/// ساعاتها وتوقفها في التقرير اليومي وتُحمَّل تكلفتها على مركز المشروع؛ الإرجاع أو التحويل يوقف المورد ولا يحذفه.
/// </summary>
public class EquipmentAssignment : BaseEntity
{
    public Guid EquipmentId { get; set; }
    public Guid ContractId { get; set; }
    public Guid? ProjectResourceId { get; set; }
    public string? SiteName { get; set; }
    public Guid? OperatorEmployeeId { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string Status { get; set; } = EquipmentAssignmentStatuses.Active;
    public decimal? MeterStart { get; set; }
    public decimal? MeterEnd { get; set; }
    /// <summary>الإسناد الذي حُوِّلت منه المعدة (للتحويل بين المشاريع).</summary>
    public Guid? TransferredFromId { get; set; }
    public string? Notes { get; set; }
    public string? ReturnNotes { get; set; }
}

/// <summary>نوع سطر سجل التشغيل: ساعات تشغيل خارج التقرير اليومي، وقود، مصروف، أو توقف.</summary>
public static class EquipmentLogKinds
{
    public const string Operation = "operation";
    public const string Fuel = "fuel";
    public const string Expense = "expense";
    public const string Downtime = "downtime";
    public static readonly string[] All = { Operation, Fuel, Expense, Downtime };
    public static bool HasAmount(string kind) => kind is Fuel or Expense;
}

/// <summary>
/// سجل تشغيل المعدة: ساعات، وقود، مصاريف، وتوقف. ساعات المعدة المسندة لمشروع مصدرها التقرير اليومي المعتمد ولا تُكرَّر هنا.
/// الوقود والمصروف يُقيَّدان (مدين حساب المصروف بمركز المشروع أو مركز المعدة / دائن حساب السداد) فقط إن حُدِّد حساب السداد؛
/// وبدونه يبقى السطر للمتابعة (المصروف مقيَّد من مستنده: فاتورة شراء أو سند صرف) فلا يتكرر القيد.
/// </summary>
public class EquipmentLog : BaseEntity
{
    public Guid EquipmentId { get; set; }
    public string Kind { get; set; } = EquipmentLogKinds.Operation;
    public DateTime Date { get; set; }
    /// <summary>المشروع وقت التسجيل (من الإسناد النشط).</summary>
    public Guid? ContractId { get; set; }
    public decimal Hours { get; set; }
    /// <summary>كمية الوقود (لتر).</summary>
    public decimal Quantity { get; set; }
    public decimal Amount { get; set; }
    public decimal? MeterReading { get; set; }
    /// <summary>سبب التوقف أو نوع المصروف.</summary>
    public string? Reason { get; set; }
    public string? Notes { get; set; }
    /// <summary>يُحمَّل على مركز المشروع (وإلا فعلى مركز المعدة).</summary>
    public bool ChargeToProject { get; set; }
    public string? ExpenseAccountCode { get; set; }
    public string? CreditAccountCode { get; set; }
    public Guid? CostCenterId { get; set; }
    public Guid? JournalEntryId { get; set; }
    public string? CreatedBy { get; set; }
}

public static class EquipmentMaintenanceTypes
{
    public const string Preventive = "preventive";
    public const string Corrective = "corrective";
    public static bool IsValid(string? v) => v is Preventive or Corrective;
}

/// <summary>أمر العمل: مفتوح ← قيد التنفيذ (المعدة في الصيانة) ← مكتمل (تُصرف القطع وتُقيَّد التكلفة)، أو ملغى (يعكس أثر المكتمل).</summary>
public static class EquipmentMaintenanceStatuses
{
    public const string Open = "open";
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
}

/// <summary>
/// أمر عمل صيانة (وقائية أو تصحيحية) بقطع غياره من المخزون. الإكمال يصرف القطع من المستودع بتكلفتها الفعلية ويقيد مدين مصروف
/// الصيانة / دائن المخزون؛ وتكلفة العمالة والورشة الخارجية تُقيَّد فقط إن حُدِّد حسابها الدائن (وإلا فهي من فاتورتها).
/// </summary>
public class EquipmentMaintenanceOrder : BaseEntity
{
    public string OrderNumber { get; set; } = string.Empty;
    public Guid EquipmentId { get; set; }
    public string Type { get; set; } = EquipmentMaintenanceTypes.Corrective;
    public string Status { get; set; } = EquipmentMaintenanceStatuses.Open;
    public DateTime ScheduledDate { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public decimal? MeterReading { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? WorkDone { get; set; }
    public Guid? TechnicianEmployeeId { get; set; }
    /// <summary>الورشة الخارجية (مورد).</summary>
    public Guid? SupplierId { get; set; }
    /// <summary>مستودع قطع الغيار.</summary>
    public Guid? WarehouseId { get; set; }
    public decimal LaborCost { get; set; }
    public decimal ExternalCost { get; set; }
    public decimal PartsCost { get; set; }
    public decimal TotalCost { get; set; }
    public decimal DowntimeHours { get; set; }
    /// <summary>المشروع الذي كانت عليه المعدة (من الإسناد النشط).</summary>
    public Guid? ContractId { get; set; }
    public bool ChargeToProject { get; set; }
    public string? ExpenseAccountCode { get; set; }
    public string? CreditAccountCode { get; set; }
    public Guid? CostCenterId { get; set; }
    public Guid? JournalEntryId { get; set; }
    /// <summary>حالة المعدة قبل بدء الصيانة (تعود إليها عند الإكمال أو الإلغاء).</summary>
    public string? StatusBefore { get; set; }
    public string? CompletedBy { get; set; }
    public string? CancelReason { get; set; }

    public virtual ICollection<EquipmentMaintenancePart> Parts { get; set; } = new List<EquipmentMaintenancePart>();
}

public class EquipmentMaintenancePart : BaseEntity
{
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string? Unit { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>التكلفة الفعلية من حركة المخزون عند الإكمال.</summary>
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public int SortOrder { get; set; }

    public virtual EquipmentMaintenanceOrder Order { get; set; } = null!;
}

public static class EquipmentRentalStatuses
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Closed = "closed";
}

/// <summary>
/// عقد إيجار معدة من مؤجر: المدة والسعر بأساسه وشروط فوترة التوقف والاستعداد. شروطه تُنسخ لمورد المشروع عند إسناد المعدة، ومنها
/// يُحسب استحقاق الإيجار من التقارير اليومية ويُسوَّى بفواتير المؤجر (الآلية القائمة <see cref="SiteRentalAccrual"/>)؛ العقد لا يقيّد شيئاً.
/// </summary>
public class EquipmentRentalContract : BaseEntity
{
    public string ContractNumber { get; set; } = string.Empty;
    public Guid EquipmentId { get; set; }
    public Guid SupplierId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string RateBasis { get; set; } = ProjectRateBases.Month;
    public decimal Rate { get; set; }
    public decimal IdleBillingPercent { get; set; } = 100m;
    public decimal StandbyBillingPercent { get; set; }
    public decimal HoursPerDay { get; set; } = 8;
    public decimal DaysPerMonth { get; set; } = 26;
    public decimal DepositAmount { get; set; }
    public string Status { get; set; } = EquipmentRentalStatuses.Draft;
    public string? Terms { get; set; }
    public DateTime? ClosedAt { get; set; }
}
