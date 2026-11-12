namespace ERP.Core.DTOs.Accounting.Contracts;

/// <summary>
/// تغطية العمالة والمعدات المخططة (احتياجات خطة التنفيذ) بالموارد الفعلية المسندة للمشروع، لكل مورد مكتبة في كل مرحلة، بالساعات.
/// الساعات المتاحة زمنية: داخل تواريخ المرحلة، بساعات دوام المشروع وأيام عمل المورد، مطروحاً منها الإجازات المعتمدة وما بعد
/// استبعاد الأصل أو خارج فترة الإيجار، ومقسومة مع المشاريع الأخرى التي يتداخل فيها إسناد الموظف أو الأصل نفسه.
/// </summary>
public class ProjectResourceCoverageDto
{
    public Guid ContractId { get; set; }
    /// <summary>ساعات الدوام اليومية للمشروع (من ملف المشروع)؛ 0 = غير محددة فلا يُحكم.</summary>
    public decimal DailyHours { get; set; }
    public DateTime? ContractStart { get; set; }
    public DateTime? ContractEnd { get; set; }
    /// <summary>صف لكل (مورد مكتبة، مرحلة).</summary>
    public List<ResourceCoverageRowDto> Rows { get; set; } = new();
    /// <summary>مجمّعة لكل مورد عبر المراحل.</summary>
    public List<ResourceCoverageTotalDto> ByResource { get; set; } = new();
    /// <summary>العمالة مجمّعة بالتخصص (الفئة).</summary>
    public List<ResourceCoverageTotalDto> ByCategory { get; set; } = new();
    /// <summary>موارد فعلية لا تطابق احتياجاً (بلا مورد مكتبة، أو موردها غير مطلوب في الخطة): لا تُحسب في التغطية.</summary>
    public List<AssignedResourceCoverageDto> Unmatched { get; set; } = new();
    public List<ProjectSupplyAlertDto> Alerts { get; set; } = new();
}

public class ResourceCoverageRowDto
{
    /// <summary>labor | equipment</summary>
    public string ResourceType { get; set; } = string.Empty;
    public Guid? ResourceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    /// <summary>ملكية المعدة في الخطة: owned | rented | undecided.</summary>
    public string? Ownership { get; set; }
    public Guid? PhaseId { get; set; }
    public string? PhaseName { get; set; }
    public DateTime? PhaseStart { get; set; }
    public DateTime? PhaseEnd { get; set; }
    public decimal RequiredHours { get; set; }
    /// <summary>العدد المقترح في الخطة (مجموع أسطرها).</summary>
    public decimal SuggestedCount { get; set; }
    public int AssignedCount { get; set; }
    public decimal AvailableHours { get; set; }
    public decimal CoveredHours { get; set; }
    public decimal ShortageHours { get; set; }
    /// <summary>فارغة حين لا يُحكم (لا نسبة مضللة).</summary>
    public decimal? CoveragePercent { get; set; }
    /// <summary>وحدات إضافية تلزم لسد النقص بدوام المرحلة كاملاً (للمعدات: ما يلزم استئجاره).</summary>
    public int AdditionalUnitsNeeded { get; set; }
    /// <summary><see cref="MaterialCoverageStatuses"/>: full | partial | none | unverifiable.</summary>
    public string Status { get; set; } = MaterialCoverageStatuses.Unverifiable;
    public List<AssignedResourceCoverageDto> Assigned { get; set; } = new();
    public List<ProjectSupplyAlertDto> Alerts { get; set; } = new();
}

/// <summary>مورد فعلي وما أتاحه للصف: الساعات بعد المشاركة والإجازات، وما فُقد بسببها.</summary>
public class AssignedResourceCoverageDto
{
    public Guid ProjectResourceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public Guid? EmployeeId { get; set; }
    public Guid? FixedAssetId { get; set; }
    public Guid? LibraryResourceId { get; set; }
    public decimal AvailableHours { get; set; }
    /// <summary>ساعات ذهبت لمشاريع أخرى متداخلة على الموظف أو الأصل نفسه.</summary>
    public decimal SharedHours { get; set; }
    /// <summary>ساعات فُقدت بإجازة معتمدة أو استبعاد الأصل أو خارج فترة الإيجار.</summary>
    public decimal UnavailableHours { get; set; }
    /// <summary>أرقام المشاريع الأخرى المتداخلة.</summary>
    public List<string> SharedWith { get; set; } = new();
    /// <summary>بيانات ناقصة تمنع حساب إتاحته (فترة الإيجار، تواريخ العقد).</summary>
    public bool Unverifiable { get; set; }
}

public class ResourceCoverageTotalDto
{
    public string ResourceType { get; set; } = string.Empty;
    public Guid? ResourceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public decimal RequiredHours { get; set; }
    public decimal AvailableHours { get; set; }
    public decimal CoveredHours { get; set; }
    public decimal ShortageHours { get; set; }
    public decimal? CoveragePercent { get; set; }
    public int AssignedCount { get; set; }
    public string Status { get; set; } = MaterialCoverageStatuses.Unverifiable;
}
