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
/// احتياج مادة (صنف مخزني) لبند في جدول الكميات: البند الرئيسي (<see cref="BoqItemId"/>) والبند الفرعي (<see cref="SubItemId"/>)
/// سطران في العقد نفسه. الكمية بوحدة الصنف المخزنية. الكمية المعتمدة = الأساسية + التعديلات المعتمدة (لاحقاً).
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
    public Guid ProductId { get; set; }
    /// <summary>مورد المكتبة الذي اشتُق منه الاحتياج (للتتبع فقط).</summary>
    public Guid? ResourceId { get; set; }
    /// <summary>وحدة الصنف المخزنية وقت الحفظ.</summary>
    public string Unit { get; set; } = string.Empty;
    public decimal BaselineQuantity { get; set; }
    public string Source { get; set; } = ProjectMaterialSources.Manual;
    public string? Notes { get; set; }
    public int SortOrder { get; set; }
    /// <summary>رمز تزامن: اعتماد طلبات شراء متزامنة على الاحتياج نفسه لا يتجاوز حده دون اكتشاف.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public virtual ProjectMaterialPlan Plan { get; set; } = null!;
}
