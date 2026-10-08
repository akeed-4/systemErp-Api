namespace ERP.Core.DTOs.Accounting;

public class CreateDepartmentDto
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public Guid? ManagerEmployeeId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class UpdateDepartmentDto : CreateDepartmentDto
{
}

public class DepartmentDto : CreateDepartmentDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>وثيقة أو عقد موظف يقترب انتهاؤه أو انتهى.</summary>
public class EmployeeAlertDto
{
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    /// <summary>id | passport | contract | probation</summary>
    public string Type { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    /// <summary>الأيام المتبقية حتى التاريخ؛ سالب = انتهى.</summary>
    public int DaysLeft { get; set; }
}

public class DepartmentHeadcountDto
{
    public Guid? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public int Count { get; set; }
}

/// <summary>مؤشرات شؤون الموظفين: الأعداد ونسبة التوطين وتكلفة الرواتب الشهرية للموظفين على رأس العمل.</summary>
public class EmployeeSummaryDto
{
    public int ActiveCount { get; set; }
    public int InactiveCount { get; set; }
    public int TerminatedCount { get; set; }
    /// <summary>الموظفون على رأس العمل بهوية وطنية.</summary>
    public int SaudiCount { get; set; }
    public int NonSaudiCount { get; set; }
    public decimal SaudizationPercent { get; set; }
    /// <summary>إجمالي الأساسي والبدلات الشهرية للموظفين على رأس العمل.</summary>
    public decimal MonthlyGross { get; set; }
    /// <summary>حصة المنشأة الشهرية في التأمينات.</summary>
    public decimal MonthlyEmployerGosi { get; set; }
    public List<DepartmentHeadcountDto> Departments { get; set; } = new();
}
