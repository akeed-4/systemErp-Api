namespace ERP.Core.Models.Accounting;

/// <summary>
/// بند أساسي في مكتبة البنود (المقاولات): ما يُباع للعميل بوحدته وسعره، وتحته بنوده الفرعية (العمل التفصيلي)، ولكلٍّ منهما
/// اختيارياً ما يحتاجه من موارد المكتبة لكل وحدة. التكلفة تُحسب عند القراءة من تكاليف الموارد الحالية، فلا تتقادم.
/// العقد ينسخ البند بتحليله، فتغيّر المكتبة لا يغيّر عقداً قائماً.
/// </summary>
public class WorkItem : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    /// <summary>الباب/القسم (كهرباء، خرسانة...): نص يجمع البنود في القائمة والتقارير.</summary>
    public string? SectionName { get; set; }
    /// <summary>القسم بالإنجليزية (اختياري): يُنسخ لسطر العقد ويُطبع حين تكون الواجهة بالإنجليزية.</summary>
    public string? SectionNameEn { get; set; }
    /// <summary>رمز وحدة القياس.</summary>
    public string Unit { get; set; } = string.Empty;
    public string? UnitDescription { get; set; }
    /// <summary>
    /// الكمية القياسية التي يُحلَّل عليها البند (1 افتراضياً)؛ تكلفة الوحدة = تكلفة الاحتياجات لكل وحدة ÷ هذه الكمية، والثابت
    /// (<see cref="BoqQuantityModes.Fixed"/>) خارجها: مرة لكل سطر في جدول الكميات كما في الدراسة.
    /// </summary>
    public decimal StandardQuantity { get; set; } = 1m;
    /// <summary><see cref="ContractBoqLineKinds"/>.</summary>
    public string LineKind { get; set; } = ContractBoqLineKinds.Measured;
    /// <summary><see cref="WorkItemPricingModes"/>.</summary>
    public string PricingMode { get; set; } = WorkItemPricingModes.Manual;
    /// <summary>سعر البيع للوحدة في manual.</summary>
    public decimal SellPrice { get; set; }
    /// <summary>نسبة المصاريف العامة والربح فوق التكلفة في cost_plus.</summary>
    public decimal MarkupPercent { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>
    /// الصنف الخدمي الذي يبيع هذا البند في بقية الوحدات (فواتير، عروض أسعار، نقاط البيع) بوحدته وسعره؛ اختياري.
    /// يُنشأ من البند أو يُربط بصنف خدمي قائم، و«مزامنة» تحدّث اسمه وسعره وتكلفته من البند.
    /// </summary>
    public Guid? ProductId { get; set; }

    public virtual ICollection<WorkItemSubItem> SubItems { get; set; } = new List<WorkItemSubItem>();
    /// <summary>كل احتياجات البند: المباشرة (<c>SubItemId = null</c>) والتابعة لبنوده الفرعية.</summary>
    public virtual ICollection<WorkItemComponent> Components { get; set; } = new List<WorkItemComponent>();
}

/// <summary>بند فرعي تحت البند الأساسي: كميته نسبة من كمية البند (per_unit) أو مستقلة (fixed).</summary>
public class WorkItemSubItem : BaseEntity
{
    public Guid WorkItemId { get; set; }
    public string? LineCode { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string QuantityMode { get; set; } = BoqQuantityModes.PerUnit;
    public decimal QuantityFactor { get; set; } = 1m;
    /// <summary>مُدخلة في fixed.</summary>
    public decimal Quantity { get; set; }
    /// <summary>تكلفة الوحدة حين لا احتياجات للبند الفرعي.</summary>
    public decimal UnitCost { get; set; }
    public int SortOrder { get; set; }

    public virtual WorkItem WorkItem { get; set; } = null!;
}

/// <summary>
/// احتياج من مورد في المكتبة لكل وحدة من مالكه (البند أو بند فرعي منه)، أو كمية ثابتة، مع الهدر. وحدته وحدة المورد،
/// وتكلفته من المورد عند القراءة.
/// </summary>
public class WorkItemComponent : BaseEntity
{
    public Guid WorkItemId { get; set; }
    /// <summary>البند الفرعي المالك؛ null = على البند الأساسي. مرجع بلا قيد (تحفظه الخدمة) حتى لا يتعدد مسار الحذف.</summary>
    public Guid? SubItemId { get; set; }
    public Guid ResourceId { get; set; }
    public string QuantityMode { get; set; } = BoqQuantityModes.PerUnit;
    public decimal ConsumptionFactor { get; set; }
    /// <summary>مُدخلة في fixed.</summary>
    public decimal FixedQuantity { get; set; }
    public decimal WastePercent { get; set; }
    public int SortOrder { get; set; }

    public virtual WorkItem WorkItem { get; set; } = null!;
}

/// <summary>طريقة سعر بيع البند.</summary>
public static class WorkItemPricingModes
{
    /// <summary>سعر يُدخل يدوياً.</summary>
    public const string Manual = "manual";
    /// <summary>تكلفة الوحدة × (1 + نسبة المصاريف والربح).</summary>
    public const string CostPlus = "cost_plus";

    public static bool IsValid(string? mode) => mode is Manual or CostPlus;
}
