namespace ERP.Core.DTOs.Accounting.Contracts;

public class ConstructionResourceDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    /// <summary>material | labor | equipment | subcontract | other</summary>
    public string ResourceType { get; set; } = "material";
    public string Unit { get; set; } = string.Empty;
    public decimal StandardCost { get; set; }
    /// <summary>manual | product | hr_job | composite</summary>
    public string CostSource { get; set; } = "manual";
    /// <summary>للقراءة: آخر ضبط للتكلفة.</summary>
    public DateTime? CostUpdatedAt { get; set; }
    public Guid? ProductId { get; set; }
    public string? HrJobTitle { get; set; }
    public Guid? HrDepartmentId { get; set; }
    public decimal HoursPerUnit { get; set; } = 1m;
    /// <summary>للقراءة: عدد موظفي المهنة في آخر حساب.</summary>
    public int HrEmployeeCount { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>للقراءة: تاريخ سريان التكلفة الحالية من سجلها.</summary>
    public DateTime? CostEffectiveFrom { get; set; }
    public string? Category { get; set; }
    public Guid? SupplierId { get; set; }
    /// <summary>owned | rented للمعدات (فارغ = غير محددة).</summary>
    public string? EquipmentOwnership { get; set; }
    /// <summary>مكونات المورد المركّب (فارغة لغيره).</summary>
    public List<ConstructionResourceComponentDto> Components { get; set; } = new();
}

public class CreateConstructionResourceDto
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string ResourceType { get; set; } = "material";
    public string Unit { get; set; } = string.Empty;
    /// <summary>تُتجاهل مع مصدر غير يدوي (تُحسب من المصدر).</summary>
    public decimal StandardCost { get; set; }
    public string CostSource { get; set; } = "manual";
    public Guid? ProductId { get; set; }
    public string? HrJobTitle { get; set; }
    public Guid? HrDepartmentId { get; set; }
    public decimal HoursPerUnit { get; set; } = 1m;
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Category { get; set; }
    /// <summary>للمادة والمعدة فقط.</summary>
    public Guid? SupplierId { get; set; }
    /// <summary>owned | rented؛ للمعدة فقط، ولازمة مع تفصيل التكلفة.</summary>
    public string? EquipmentOwnership { get; set; }
    /// <summary>تفصيل التكلفة؛ لازم مع مصدر breakdown (عمالة أو معدة) ويُتجاهل مع غيره.</summary>
    public ResourceCostBreakdownDto? Breakdown { get; set; }
    /// <summary>
    /// سريان التكلفة المدخلة (فارغ = اليوم). في هذا النموذج لا يتجاوز اليوم ولا يسبق آخر سطر في سجل المورد؛ السعر المجدول أو
    /// تصحيح الماضي من <c>POST {id}/costs</c>.
    /// </summary>
    public DateTime? EffectiveFrom { get; set; }
    /// <summary>سبب تغيير التكلفة (اختياري، يُحفظ في السجل).</summary>
    public string? CostReason { get; set; }
    /// <summary>مكونات المورد المركّب (فارغة لغيره).</summary>
    public List<ConstructionResourceComponentDto> Components { get; set; } = new();
}

public class UpdateConstructionResourceDto : CreateConstructionResourceDto { }

/// <summary>
/// تفصيل تكلفة العمالة أو المعدة: السعر بأساسه (hour | day | month) يُحوَّل للساعة بساعات اليوم وأيام الشهر، ثم تُضاف تكاليف الساعة.
/// تكلفة الوحدة = تكلفة الساعة × ساعات الوحدة (<see cref="CreateConstructionResourceDto.HoursPerUnit"/>).
/// </summary>
public class ResourceCostBreakdownDto
{
    public string RateBasis { get; set; } = "hour";
    /// <summary>الأجر، أو الإيجار، أو تكلفة التشغيل الأساسية للمعدة المملوكة، بأساسه.</summary>
    public decimal Rate { get; set; }
    public decimal HoursPerDay { get; set; } = 8m;
    public decimal DaysPerMonth { get; set; } = 26m;
    public decimal FuelPerHour { get; set; }
    public decimal MaintenancePerHour { get; set; }
    public decimal DepreciationPerHour { get; set; }
    public decimal OperatorPerHour { get; set; }
    public decimal OtherPerHour { get; set; }
    /// <summary>معامل الساعة الإضافية (1.5 = ساعة ونصف)؛ 1 = بلا إضافي.</summary>
    public decimal OvertimeMultiplier { get; set; } = 1.5m;
}

/// <summary>سطر في سجل تكلفة المورد (للقراءة).</summary>
public class ConstructionResourceCostDto
{
    public Guid Id { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public decimal UnitCost { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal HoursPerUnit { get; set; }
    /// <summary>التفصيل لمصدر breakdown، وفارغ لغيره.</summary>
    public ResourceCostBreakdownDto? Breakdown { get; set; }
    public decimal HourlyCost { get; set; }
    public decimal OvertimeHourlyCost { get; set; }
    public string? Reason { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>past = سطر سابق، current = الساري اليوم، scheduled = يسري لاحقاً.</summary>
    public string Status { get; set; } = "past";
}

/// <summary>سجل تكلفة المورد: الأسطر من الأحدث، والساري اليوم.</summary>
public class ConstructionResourceCostHistoryDto
{
    public Guid ResourceId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string CostSource { get; set; } = string.Empty;
    public ConstructionResourceCostDto? Current { get; set; }
    public List<ConstructionResourceCostDto> Entries { get; set; } = new();
}

/// <summary>
/// سعر جديد في سجل المورد بتاريخ سريان (ماضٍ أو مستقبلي)، للمصدر اليدوي (<see cref="UnitCost"/>) أو التفصيل
/// (<see cref="Breakdown"/>). المصادر المحسوبة (الصنف، شؤون الموظفين، المركّب) تُحدَّث من مصدرها.
/// </summary>
public class AddResourceCostDto
{
    public DateTime EffectiveFrom { get; set; }
    public decimal? UnitCost { get; set; }
    public ResourceCostBreakdownDto? Breakdown { get; set; }
    public string? Reason { get; set; }
}

/// <summary>التكلفة السارية لمورد في تاريخ: القيمة وسطر السجل الذي جاءت منه (مرجع النسخة المجمّدة).</summary>
public class ResourceCostAtDto
{
    public Guid ResourceId { get; set; }
    public Guid? CostId { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public decimal UnitCost { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
}

/// <summary>ملخص مكتبة التكاليف: الموارد الفعّالة لكل نوع، وما ينقصه.</summary>
public class ConstructionCostLibrarySummaryDto
{
    public List<ConstructionCostTypeSummaryDto> Types { get; set; } = new();
    /// <summary>موارد فعّالة بلا تكلفة.</summary>
    public int WithoutCost { get; set; }
    /// <summary>مواد فعّالة غير مربوطة بصنف مخزني.</summary>
    public int UnlinkedMaterials { get; set; }
    /// <summary>معدات فعّالة بلا ملكية محددة.</summary>
    public int EquipmentWithoutOwnership { get; set; }
    /// <summary>أسعار مجدولة تسري بعد اليوم.</summary>
    public int Scheduled { get; set; }
    public string Currency { get; set; } = string.Empty;
    /// <summary>الفئات المستعملة لكل نوع (اقتراحات الإدخال).</summary>
    public Dictionary<string, List<string>> Categories { get; set; } = new();
}

public class ConstructionCostTypeSummaryDto
{
    public string ResourceType { get; set; } = string.Empty;
    public int Active { get; set; }
    public int Inactive { get; set; }
}

/// <summary>مكوّن مورد مركّب: مورد من المكتبة بكميته (بوحدته) لكل وحدة من المركّب.</summary>
public class ConstructionResourceComponentDto
{
    public Guid Id { get; set; }
    public Guid ComponentResourceId { get; set; }
    public decimal Quantity { get; set; }
    public decimal WastePercent { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>نتيجة تحديث تكاليف الموارد من مصادرها (الأصناف وشؤون الموظفين).</summary>
public class ResourceCostRefreshDto
{
    public int Updated { get; set; }
    /// <summary>موارد تعذّر حسابها (صنف بلا تكلفة أو وحدة لا تتحول، أو مهنة بلا موظفين فعّالين)، أو لا صلاحية لقراءة مصدرها.</summary>
    public List<string> Skipped { get; set; } = new();
}

/// <summary>
/// خيارات ربط العمالة بشؤون الموظفين: المهن (المسميات الوظيفية) والأقسام بعدد الموظفين الفعّالين، بلا أي رواتب.
/// </summary>
public class HrLaborOptionsDto
{
    public List<HrJobTitleOptionDto> JobTitles { get; set; } = new();
    public List<HrDepartmentOptionDto> Departments { get; set; } = new();
}

public class HrJobTitleOptionDto
{
    public string JobTitle { get; set; } = string.Empty;
    public int EmployeeCount { get; set; }
}

public class HrDepartmentOptionDto
{
    public Guid Id { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public int EmployeeCount { get; set; }
}

/// <summary>مادة في المكتبة غير مربوطة بصنف مخزني، والأصناف المرشحة لربطها (الأقوى أولاً).</summary>
public class ResourceLinkSuggestionDto
{
    public Guid ResourceId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    /// <summary>عدد بنود المكتبة التي تستخدمه.</summary>
    public int UsageCount { get; set; }
    public List<ResourceLinkCandidateDto> Candidates { get; set; } = new();
}

public class ResourceLinkCandidateDto
{
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Cost { get; set; }
    /// <summary>sku = الرمز أو الباركود يطابق رمز المادة، name = الاسم مطابق، similar = الاسم متشابه.</summary>
    public string Match { get; set; } = "similar";
    /// <summary>وحدة المادة تتحوّل لوحدة الصنف (شرط الربط).</summary>
    public bool UnitCompatible { get; set; }
}

public class LinkResourcesToProductsDto
{
    public List<ResourceProductLinkDto> Links { get; set; } = new();
}

public class ResourceProductLinkDto
{
    public Guid ResourceId { get; set; }
    public Guid ProductId { get; set; }
}

public class ResourceLinkResultDto
{
    public int Linked { get; set; }
    public List<string> Skipped { get; set; } = new();
}
