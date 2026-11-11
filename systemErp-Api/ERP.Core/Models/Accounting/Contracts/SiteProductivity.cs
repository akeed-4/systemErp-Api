namespace ERP.Core.Models.Accounting;

/// <summary>نوع مورد المشروع الذي تُحسب له تكلفة الساعة.</summary>
public static class ProjectResourceKinds
{
    /// <summary>آلية مملوكة: تكلفة تشغيل الساعة + الوقود + الصيانة + المشغّل.</summary>
    public const string OwnedEquipment = "owned_equipment";
    /// <summary>آلية مستأجرة: الإيجار (ساعة/يوم/شهر) محوّلاً للساعة + تكلفة التشغيل الإضافية.</summary>
    public const string RentedEquipment = "rented_equipment";
    /// <summary>مهندس: التكلفة الشهرية (الراتب والبدلات) ÷ ساعات الشهر.</summary>
    public const string Engineer = "engineer";
    /// <summary>عمالة يومية (عامل أو فئة): الأجر اليومي أو بالساعة، والإضافي بمعامل.</summary>
    public const string Labor = "labor";

    public static readonly string[] All = { OwnedEquipment, RentedEquipment, Engineer, Labor };
    public static bool IsValid(string? kind) => kind != null && All.Contains(kind);
    public static bool IsEquipment(string? kind) => kind is OwnedEquipment or RentedEquipment;
}

/// <summary>أساس السعر: بالساعة أو اليوم (ساعات اليوم) أو الشهر (ساعات اليوم × أيام الشهر).</summary>
public static class ProjectRateBases
{
    public const string Hour = "hour";
    public const string Day = "day";
    public const string Month = "month";
    public static bool IsValid(string? basis) => basis is Hour or Day or Month;
}

/// <summary>
/// مورد مُسند للمشروع بتكلفة ساعته: آلية مملوكة أو مستأجرة، أو مهندس، أو عمالة يومية. تكلفة الساعة يحسبها الخادم عند الحفظ
/// (السعر ÷ ساعات أساسه + الوقود + الصيانة + المشغّل + الإضافي)، وتُثبَّت على سطر الاستخدام في التقرير اليومي فلا يتغير الماضي.
/// هذه تكلفة تشغيلية لتوزيع التكلفة والإنتاجية، وليست قيداً محاسبياً.
/// </summary>
public class ProjectResource : BaseEntity
{
    public Guid ContractId { get; set; }
    public string Kind { get; set; } = ProjectResourceKinds.Labor;
    public string Name { get; set; } = string.Empty;
    /// <summary>رقم الآلية (أو رقم العامل).</summary>
    public string? Code { get; set; }
    /// <summary>نوع الآلية (حفار، شيول...) أو التخصص (للمهندس والعامل).</summary>
    public string? Category { get; set; }
    /// <summary>المورد المعياري في مكتبة المقاولات (اختياري).</summary>
    public Guid? LibraryResourceId { get; set; }
    /// <summary>الأصل الثابت للآلية المملوكة (اختياري).</summary>
    public Guid? FixedAssetId { get; set; }
    /// <summary>المؤجر للآلية المستأجرة.</summary>
    public Guid? SupplierId { get; set; }
    /// <summary>الموظف للمهندس أو العامل (اختياري للعمالة).</summary>
    public Guid? EmployeeId { get; set; }

    public string RateBasis { get; set; } = ProjectRateBases.Hour;
    /// <summary>السعر بأساسه: تكلفة التشغيل بالساعة (المملوكة)، أو الإيجار، أو التكلفة الشهرية (المهندس)، أو الأجر.</summary>
    public decimal Rate { get; set; }
    public decimal FuelPerHour { get; set; }
    public decimal MaintenancePerHour { get; set; }
    public decimal OperatorPerHour { get; set; }
    /// <summary>تكلفة تشغيل إضافية بالساعة (للمستأجرة خاصة).</summary>
    public decimal ExtraCostPerHour { get; set; }
    public decimal HoursPerDay { get; set; } = 8;
    public decimal DaysPerMonth { get; set; } = 26;
    /// <summary>معامل الساعة الإضافية (1.5 = ساعة ونصف).</summary>
    public decimal OvertimeMultiplier { get; set; } = 1.5m;
    public DateTime? RentalStart { get; set; }
    public DateTime? RentalEnd { get; set; }
    /// <summary>
    /// شروط فوترة الآلية المستأجرة في عقد إيجارها: نسبة ما يُحتسب من سعر الساعة عن ساعات التوقف (100 = تُحتسب كاملة، 0 = لا تُحتسب)
    /// وعن ساعات الاستعداد (الآلية في الموقع جاهزة بلا تشغيل). تطبَّق على تكلفة سطر التقرير اليومي ومنها استحقاق الإيجار.
    /// </summary>
    public decimal IdleBillingPercent { get; set; } = 100m;
    public decimal StandbyBillingPercent { get; set; }

    /// <summary>محسوبة عند الحفظ.</summary>
    public decimal HourlyCost { get; set; }
    public decimal OvertimeHourlyCost { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}

/// <summary>خطة الإنجاز اليومية: المستهدف لكل ساعة من بند في جدول الكميات، ساريةً من تاريخ (الأحدث قبل اليوم هو المعتمد له).</summary>
public class ProjectDailyPlan : BaseEntity
{
    public Guid ContractId { get; set; }
    public Guid ContractItemId { get; set; }
    public decimal TargetPerHour { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public string? Notes { get; set; }
}

/// <summary>حالات التقرير اليومي: مسودة ← مقدَّم (من المشرف) ← معتمد (من المهندس المسؤول)، أو إعادة للمسودة بسبب.</summary>
public static class SiteDailyReportStatuses
{
    public const string Draft = "draft";
    public const string Submitted = "submitted";
    public const string Approved = "approved";
}

/// <summary>أسباب التوقف وانخفاض الإنتاجية.</summary>
public static class SiteDowntimeReasons
{
    public const string EquipmentBreakdown = "equipment_breakdown";
    public const string MaterialShortage = "material_shortage";
    public const string LaborShortage = "labor_shortage";
    public const string Weather = "weather";
    public const string WaitingInspection = "waiting_inspection";
    public const string Safety = "safety";
    public const string Other = "other";

    public static readonly string[] All = { EquipmentBreakdown, MaterialShortage, LaborShortage, Weather, WaitingInspection, Safety, Other };
    public static bool IsValid(string? reason) => reason != null && All.Contains(reason);
}

/// <summary>التقرير اليومي للموقع: واحد لكل مشروع في اليوم، بفترات إنجازه بالساعة، والموارد المستخدمة، والمواد المستهلكة.</summary>
public class SiteDailyReport : BaseEntity
{
    public Guid ContractId { get; set; }
    public DateTime ReportDate { get; set; }
    public string Status { get; set; } = SiteDailyReportStatuses.Draft;
    public string? Notes { get; set; }
    public string? ReturnNote { get; set; }
    public Guid? PreparedByUserId { get; set; }
    public string? PreparedBy { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    /// <summary>
    /// قيد تحميل العمالة والمعدات المملوكة على المشروع عند الاعتماد (مدين تكلفة عمالة/معدات المشاريع بمركز المشروع / دائن التكاليف المحمّلة)
    /// بتكلفة الساعة المثبّتة على الأسطر. المعدات المستأجرة لا تُحمَّل (تكلفتها من فاتورة الإيجار). فارغ = لم يُرحَّل (عقد بلا مركز، أو لا تكلفة).
    /// </summary>
    public Guid? CostJournalEntryId { get; set; }
    public decimal PostedLaborCost { get; set; }
    public decimal PostedEquipmentCost { get; set; }
    /// <summary>
    /// عدد مرات ترحيل تكلفة التقرير (الاعتماد الأول = 1، وكل اعتماد بعد إعادة فتح يزيده). قيد الاعتماد التالي فرق فقط عن المرحّل،
    /// وأسطر الاستحقاق مفتاحها (التقرير، المورد، المرة) فلا يتكرر قيد للمرة نفسها.
    /// </summary>
    public int CostRevision { get; set; }
    /// <summary>سبب آخر إعادة فتح للتقرير المعتمد.</summary>
    public string? ReopenNote { get; set; }

    public virtual ICollection<SiteHourEntry> Hours { get; set; } = new List<SiteHourEntry>();
    public virtual ICollection<SiteResourceUsage> Resources { get; set; } = new List<SiteResourceUsage>();
    public virtual ICollection<SiteMaterialUsage> Materials { get; set; } = new List<SiteMaterialUsage>();
}

/// <summary>إنجاز فترة (ساعة افتراضاً، يعدّلها المشرف) لبند: المستهدف والمنفَّذ والتوقف وسببه.</summary>
public class SiteHourEntry : BaseEntity
{
    public Guid ReportId { get; set; }
    /// <summary>بداية ونهاية الفترة بالدقائق من منتصف الليل.</summary>
    public int SlotStart { get; set; }
    public int SlotEnd { get; set; }
    public Guid ContractItemId { get; set; }
    public decimal TargetQuantity { get; set; }
    public decimal ActualQuantity { get; set; }
    public int DowntimeMinutes { get; set; }
    public string? DowntimeReason { get; set; }
    public string? Notes { get; set; }
    public int SortOrder { get; set; }

    public virtual SiteDailyReport Report { get; set; } = null!;
}

/// <summary>مورد استُخدم في اليوم: الساعات المنتجة والإضافية والتوقف، وتكلفة الساعة مثبّتة من المورد وقت التسجيل.</summary>
public class SiteResourceUsage : BaseEntity
{
    public Guid ReportId { get; set; }
    public Guid ProjectResourceId { get; set; }
    /// <summary>البند الذي عمل عليه (فارغ = يُوزَّع على بنود اليوم بقيمة إنتاجها).</summary>
    public Guid? ContractItemId { get; set; }
    public decimal Hours { get; set; }
    public decimal OvertimeHours { get; set; }
    /// <summary>ساعات توقف مدفوعة (تدخل التكلفة وتُعرض منفصلة).</summary>
    public decimal IdleHours { get; set; }
    public string? IdleReason { get; set; }
    /// <summary>ساعات استعداد الآلية المستأجرة (في الموقع بلا تشغيل): تُحتسب بنسبة الاستعداد في شروط إيجارها.</summary>
    public decimal StandbyHours { get; set; }
    public decimal HourlyCost { get; set; }
    public decimal OvertimeHourlyCost { get; set; }
    /// <summary>
    /// التكلفة مثبّتة عند الحفظ: الساعات × السعر + الإضافي × سعره + التوقف (والاستعداد للمستأجرة) بنسب الفوترة.
    /// نسب الفوترة مثبّتة هنا أيضاً (<see cref="IdleBillingPercent"/>) فلا يغيّر تعديل شروط الإيجار لاحقاً تقريراً محفوظاً.
    /// </summary>
    public decimal Cost { get; set; }
    public decimal IdleBillingPercent { get; set; } = 100m;
    public decimal StandbyBillingPercent { get; set; }
    public string? Notes { get; set; }

    public virtual SiteDailyReport Report { get; set; } = null!;
}

/// <summary>مادة استُهلكت في اليوم على بند (تشغيلي للمقارنة؛ الأثر المحاسبي من الصرف للمشروع). تكلفة الوحدة تقديرية مثبّتة.</summary>
public class SiteMaterialUsage : BaseEntity
{
    public Guid ReportId { get; set; }
    public Guid ContractItemId { get; set; }
    /// <summary>مادة مكتبة المقاولات المستهلكة (فارغة في أسطر قديمة سبقت الربط بالمكتبة).</summary>
    public Guid? ResourceId { get; set; }
    /// <summary>الصنف المخزني لمادة المكتبة وقت الحفظ (فارغ للمادة غير المربوطة).</summary>
    public Guid? ProductId { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>وحدة الكمية: وحدة المادة وقت الحفظ.</summary>
    public string Unit { get; set; } = string.Empty;
    /// <summary>تكلفة وحدة المادة في المكتبة وقت الحفظ.</summary>
    public decimal UnitCost { get; set; }
    public string? Notes { get; set; }

    public virtual SiteDailyReport Report { get; set; } = null!;
}
