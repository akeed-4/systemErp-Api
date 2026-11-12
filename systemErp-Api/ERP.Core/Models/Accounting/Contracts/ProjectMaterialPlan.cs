namespace ERP.Core.Models.Accounting;

/// <summary>حالات خطة احتياجات المشروع: مسودة ← مقدَّمة ← معتمدة (الإعادة من المقدَّمة للمسودة بسبب).</summary>
public static class ProjectMaterialPlanStatuses
{
    public const string Draft = "draft";
    public const string Submitted = "submitted";
    public const string Approved = "approved";
}

/// <summary>مصدر سطر الاحتياج: مشتق من تحليل بنود جدول الكميات في المكتبة، أو مُدخل يدوياً.</summary>
public static class ProjectMaterialSources
{
    public const string Derived = "derived";
    public const string Manual = "manual";

    public static bool IsValid(string? source) => source is Derived or Manual;
}

/// <summary>
/// تشخيص الاشتقاق لكل سطر في جدول الكميات: حالة السطر، وسبب التخطي، وتنبيهات مواده. رموز تترجمها الواجهة.
/// </summary>
public static class ProjectMaterialDiagnostics
{
    // حالة السطر
    public const string Derived = "derived";
    public const string Warning = "warning";
    public const string Skipped = "skipped";

    // أسباب التخطي
    /// <summary>البند غير مرتبط ببند في المكتبة (مضاف يدوياً أو مستورد).</summary>
    public const string NotLinked = "not_linked";
    /// <summary>بند المكتبة المصدر لم يعد موجوداً.</summary>
    public const string WorkItemMissing = "work_item_missing";
    /// <summary>البند الفرعي بلا مصدر في المكتبة (لا بند فرعي مرتبط).</summary>
    public const string SubItemNoSource = "sub_no_source";
    /// <summary>البند الرئيسي للفرعي غير مرتبط بالمكتبة.</summary>
    public const string ParentNotLinked = "parent_not_linked";
    /// <summary>تحليل البند في المكتبة بلا مواد (عمالة ومعدات فقط، أو فارغ).</summary>
    public const string NoMaterials = "no_materials";

    // تنبيهات المادة (تُشتق رغمها)
    /// <summary>المادة غير مربوطة بصنف مخزني: لا تُصرف ولا تُشترى حتى تُربط.</summary>
    public const string NotStocked = "not_stocked";
    /// <summary>وحدة المادة لا تتحول لوحدة صنفها المخزني: الرصيد لا يُقارن.</summary>
    public const string UnitMismatch = "unit_mismatch";
}

/// <summary>نطاق اختيار المادة: تحليل البند في المكتبة، أو مواد المكتبة عامة (لسطر غير مرتبط أو عند الطلب).</summary>
public static class ProjectMaterialCandidateScopes
{
    public const string Item = "item";
    public const string Library = "library";
}

/// <summary>
/// احتياجات المواد المعتمدة لمشروع (عقد مقاولات بجدول كميات؛ العقد هو المشروع): خطة واحدة لكل عقد. تُشتق من تحليل البنود في
/// المكتبة أو تُدخل يدوياً، ثم تُعتمد فتصبح النسخة الأساسية (Baseline) التي تُقارن بها طلبات الشراء. نسخة مستقلة: تعديل المكتبة
/// لا يغيّرها، والمعتمدة لا تُعدَّل إلا بتعديل معتمد مسجَّل.
/// </summary>
public class ProjectMaterialPlan : BaseEntity
{
    public Guid ContractId { get; set; }
    public string Status { get; set; } = ProjectMaterialPlanStatuses.Draft;
    public string? Notes { get; set; }
    /// <summary>سبب آخر إعادة للمسودة.</summary>
    public string? ReturnNote { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public Guid? ApprovedByUserId { get; set; }

    public virtual ICollection<ProjectMaterialRequirement> Lines { get; set; } = new List<ProjectMaterialRequirement>();
}

/// <summary>
/// احتياج مادة من مكتبة المقاولات (<see cref="ResourceId"/>: مورد من نوع مادة) لبند في جدول الكميات: البند الرئيسي
/// (<see cref="BoqItemId"/>) والبند الفرعي (<see cref="SubItemId"/>) سطران في العقد نفسه. الكمية بوحدة المادة (<see cref="Unit"/>)،
/// والصنف المخزني اختياري يُؤخذ من ربط المادة: المادة غير المربوطة احتياج صحيح لا يُصرف ولا يُشترى حتى تُربط.
/// الكمية المعتمدة = الأساسية + التعديلات المعتمدة (لاحقاً).
/// </summary>
public class ProjectMaterialRequirement : BaseEntity
{
    public Guid PlanId { get; set; }
    /// <summary>العقد (مكرر من الخطة للاستعلام المباشر من طلبات الشراء والتقارير).</summary>
    public Guid ContractId { get; set; }
    /// <summary>سطر البند الرئيسي في جدول الكميات (أو البند العادي)، وفارغ لاحتياج عام على المشروع.</summary>
    public Guid? BoqItemId { get; set; }
    /// <summary>سطر البند الفرعي تحت <see cref="BoqItemId"/> إن وُجد.</summary>
    public Guid? SubItemId { get; set; }
    /// <summary>الصنف المخزني لمادة المكتبة وقت الحفظ (فارغ للمادة غير المربوطة؛ العرض يأخذ ربط المادة الحالي).</summary>
    public Guid? ProductId { get; set; }
    /// <summary>مادة المكتبة (مورد من نوع مادة): هوية الاحتياج. فارغة في أسطر قديمة سبقت الربط بالمكتبة.</summary>
    public Guid? ResourceId { get; set; }
    /// <summary>وحدة الكمية: وحدة مادة المكتبة وقت الحفظ (وفي الأسطر القديمة وحدة الصنف).</summary>
    public string Unit { get; set; } = string.Empty;
    public decimal BaselineQuantity { get; set; }
    public string Source { get; set; } = ProjectMaterialSources.Manual;
    public string? Notes { get; set; }
    public int SortOrder { get; set; }
    /// <summary>مرحلة التنفيذ التي يتبعها الاحتياج (من خطة التنفيذ؛ بلا قيد).</summary>
    public Guid? PhaseId { get; set; }
    /// <summary>تكلفة الوحدة المخططة من تحليل الدراسة المجمّد (0 للأسطر القديمة).</summary>
    public decimal PlannedUnitCost { get; set; }
    /// <summary>الكمية لكل وحدة من بند جدول الكميات (للتتبع).</summary>
    public decimal QuantityPerUnit { get; set; }
    /// <summary>رمز تزامن: اعتماد طلبات شراء متزامنة على الاحتياج نفسه لا يتجاوز حده دون اكتشاف.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public virtual ProjectMaterialPlan Plan { get; set; } = null!;
}
