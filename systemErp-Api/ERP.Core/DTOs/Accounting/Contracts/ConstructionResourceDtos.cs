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
    /// <summary>مكونات المورد المركّب (فارغة لغيره).</summary>
    public List<ConstructionResourceComponentDto> Components { get; set; } = new();
}

public class UpdateConstructionResourceDto : CreateConstructionResourceDto { }

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
