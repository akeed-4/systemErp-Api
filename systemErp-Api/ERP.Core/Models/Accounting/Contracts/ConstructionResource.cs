namespace ERP.Core.Models.Accounting;

/// <summary>
/// مورد في مكتبة المقاولات (مادة/عمالة/معدة/مقاول باطن/أخرى) بوحدته وتكلفته المعيارية: يُسعَّر في مكان واحد وتستخدمه
/// البنود القياسية في تحليلها. مصدر التكلفة (<see cref="ResourceCostSources"/>): يدوي، أو من الصنف المخزني للمادة،
/// أو من شؤون الموظفين للعمالة (متوسط التكلفة المحمّلة لموظفي المهنة). العقود تنسخ التحليل بتكلفته يوم النسخ،
/// فتغيّر تكلفة المورد لا يغيّر عقداً قائماً.
/// </summary>
public class ConstructionResource : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    /// <summary><see cref="BoqResourceTypes"/>.</summary>
    public string ResourceType { get; set; } = BoqResourceTypes.Material;
    /// <summary>رمز وحدة القياس (<see cref="Shared.UnitOfMeasure.Code"/>).</summary>
    public string Unit { get; set; } = string.Empty;
    /// <summary>تكلفة الوحدة المعيارية: مُدخلة في manual، ومحسوبة من مصدرها في غيره.</summary>
    public decimal StandardCost { get; set; }
    public string CostSource { get; set; } = ResourceCostSources.Manual;
    public DateTime? CostUpdatedAt { get; set; }

    // ---- المادة: الصنف المخزني ----
    /// <summary>الصنف المخزني لمورد المادة (اختياري؛ لازم مع مصدر product).</summary>
    public Guid? ProductId { get; set; }

    // ---- العمالة: شؤون الموظفين ----
    /// <summary>المهنة: المسمى الوظيفي كما في ملفات الموظفين (مصدر hr_job).</summary>
    public string? HrJobTitle { get; set; }
    /// <summary>حصر المهنة في قسم (اختياري).</summary>
    public Guid? HrDepartmentId { get; set; }
    /// <summary>ساعات العمل في وحدة واحدة من المورد (1 للساعة، 8 لليوم...).</summary>
    public decimal HoursPerUnit { get; set; } = 1m;
    /// <summary>عدد الموظفين الذين حُسب منهم متوسط التكلفة عند آخر تحديث.</summary>
    public int HrEmployeeCount { get; set; }

    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>مكونات المورد المركّب (مصدر composite): موارد أخرى بكمياتها لكل وحدة واحدة منه.</summary>
    public virtual ICollection<ConstructionResourceComponent> Components { get; set; } = new List<ConstructionResourceComponent>();
}

/// <summary>
/// مكوّن مورد مركّب (فرقة عمل، خلطة...): مورد من المكتبة بكميته بوحدته هو لكل وحدة واحدة من المركّب، ونسبة هدره.
/// المكوّن قد يكون مركّباً بدوره، والخدمة تمنع الدورات.
/// </summary>
public class ConstructionResourceComponent : BaseEntity
{
    public Guid ResourceId { get; set; }
    public Guid ComponentResourceId { get; set; }
    /// <summary>الكمية بوحدة المكوّن لكل وحدة واحدة من المورد المركّب.</summary>
    public decimal Quantity { get; set; }
    public decimal WastePercent { get; set; }
    public int SortOrder { get; set; }

    public virtual ConstructionResource Resource { get; set; } = null!;
}

/// <summary>مصدر تكلفة المورد.</summary>
public static class ResourceCostSources
{
    /// <summary>تكلفة مُدخلة يدوياً.</summary>
    public const string Manual = "manual";
    /// <summary>من الصنف المخزني (المعيارية، ثم المتوسط، ثم آخر شراء) محوّلة لوحدة المورد؛ للمادة.</summary>
    public const string Product = "product";
    /// <summary>من شؤون الموظفين: متوسط التكلفة المحمّلة لموظفي المهنة الفعّالين × ساعات الوحدة؛ للعمالة.</summary>
    public const string HrJob = "hr_job";
    /// <summary>مورد مركّب: مجموع تكلفة مكوناته × كمياتها × (1 + الهدر)؛ لأي نوع.</summary>
    public const string Composite = "composite";

    public static bool IsValid(string? source) => source is Manual or Product or HrJob or Composite;
}
