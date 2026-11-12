namespace ERP.Core.DTOs.Accounting.Contracts;

/// <summary>بند أساسي في القائمة: بياناته وتكلفته وسعره وهامشه (محسوبة من الخادم بتكاليف الموارد الحالية).</summary>
public class WorkItemSummaryDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? SectionName { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string? UnitDescription { get; set; }
    public string LineKind { get; set; } = "measured";
    public bool IsActive { get; set; }
    public int SubItemCount { get; set; }
    public int ComponentCount { get; set; }
    /// <summary>تكلفة الوحدة المخططة من الاحتياجات لكل وحدة (0 = لا تحليل)، بلا الثابت.</summary>
    public decimal UnitCost { get; set; }
    /// <summary>
    /// التكلفة الثابتة (احتياج ثابت أو بند فرعي بكمية ثابتة): مرة واحدة لكل سطر في جدول الكميات أياً كانت كميته، كما تحسبها الدراسة.
    /// تكلفة السطر التقديرية = الكمية × تكلفة الوحدة + هذه.
    /// </summary>
    public decimal FixedCost { get; set; }
    /// <summary>سعر البيع للوحدة (يدوي، أو التكلفة + النسبة).</summary>
    public decimal SellPrice { get; set; }
    public decimal Margin { get; set; }
    public decimal MarginPercent { get; set; }
    public DateTime? UpdatedAt { get; set; }
    /// <summary>الصنف الخدمي المرتبط (يبيع البند في بقية الوحدات).</summary>
    public Guid? ProductId { get; set; }
    public string? ProductSku { get; set; }
    public string? ProductName { get; set; }
}

/// <summary>البند كاملاً للتعديل والعرض: المدخلات وما يحسبه الخادم.</summary>
public class WorkItemDto : WorkItemSummaryDto
{
    public decimal StandardQuantity { get; set; } = 1m;
    /// <summary>manual | cost_plus</summary>
    public string PricingMode { get; set; } = "manual";
    /// <summary>المدخل في manual.</summary>
    public decimal ManualSellPrice { get; set; }
    public decimal MarkupPercent { get; set; }
    public string? Notes { get; set; }
    /// <summary>للقراءة: تكلفة التحليل للكمية القياسية.</summary>
    public decimal TotalCost { get; set; }
    public List<WorkItemSubItemDto> SubItems { get; set; } = new();
    /// <summary>احتياجات مباشرة على البند الأساسي.</summary>
    public List<WorkItemComponentDto> Components { get; set; } = new();
}

public class SaveWorkItemDto
{
    /// <summary>فارغ = يولّده الخادم.</summary>
    public string? Code { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? SectionName { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string? UnitDescription { get; set; }
    public decimal StandardQuantity { get; set; } = 1m;
    public string LineKind { get; set; } = "measured";
    public string PricingMode { get; set; } = "manual";
    public decimal SellPrice { get; set; }
    public decimal MarkupPercent { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>ربط بصنف خدمي قائم (فارغ = بلا ربط).</summary>
    public Guid? ProductId { get; set; }
    public List<WorkItemSubItemDto> SubItems { get; set; } = new();
    public List<WorkItemComponentDto> Components { get; set; } = new();
}

public class WorkItemSubItemDto
{
    public Guid Id { get; set; }
    public string? LineCode { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    /// <summary>per_unit | fixed</summary>
    public string QuantityMode { get; set; } = "per_unit";
    public decimal QuantityFactor { get; set; } = 1m;
    /// <summary>مُدخلة في fixed، ومحسوبة في per_unit (للقراءة).</summary>
    public decimal Quantity { get; set; }
    /// <summary>مُدخلة حين لا احتياجات، وإلا محسوبة منها (للقراءة).</summary>
    public decimal UnitCost { get; set; }
    /// <summary>للقراءة.</summary>
    public decimal TotalCost { get; set; }
    public List<WorkItemComponentDto> Components { get; set; } = new();
}

public class WorkItemComponentDto
{
    public Guid Id { get; set; }
    public Guid ResourceId { get; set; }
    /// <summary>per_unit | fixed</summary>
    public string QuantityMode { get; set; } = "per_unit";
    public decimal ConsumptionFactor { get; set; }
    public decimal FixedQuantity { get; set; }
    public decimal WastePercent { get; set; }
    /// <summary>
    /// في القراءة: تكلفة وحدة المورد الحالية. في الحفظ: null = بلا تغيير، وقيمة = تكلفة جديدة للمورد اليدوي تُحفظ عليه
    /// (فتتبعها كل البنود التي تستخدمه)؛ تُتجاهل لمورد تكلفته من الصنف أو شؤون الموظفين.
    /// </summary>
    public decimal? UnitCost { get; set; }
    // ---- للقراءة (من المورد والحساب) ----
    public string ResourceCode { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    /// <summary>مصدر تكلفة المورد: manual | product | hr_job | composite.</summary>
    public string CostSource { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal TotalCost { get; set; }
}

/// <summary>
/// إنشاء بند مكتبة من صنف قائم في شاشة الأصناف (بدل إعادة تعريفه): الاسم والوحدة وسعر البيع من الصنف، ومكوناته احتياجات البند.
/// </summary>
public class CreateWorkItemFromProductDto
{
    public Guid ProductId { get; set; }
}

/// <summary>بند مكتبة يستخدم صنفاً: صنفه الخدمي (service)، أو مادة مربوطة به في احتياجاته (material).</summary>
public class WorkItemProductUsageDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string Via { get; set; } = "material";
    public bool IsActive { get; set; }
}
