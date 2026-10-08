namespace ERP.Core.Models.Accounting;

/// <summary>
/// ملف الموظف: بياناته الشخصية ووثائقه وعقده وبنود راتبه الشهري الثابتة. نسب التأمينات تُضبط لكل موظف
/// (تختلف بحسب الجنسية وتاريخ الالتحاق).
/// </summary>
public class Employee : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }

    // ---------- البيانات الشخصية والاتصال ----------
    /// <summary>الجنسية (نص حر أو رمز دولة).</summary>
    public string? Nationality { get; set; }
    public string? Gender { get; set; } // male | female
    public DateTime? BirthDate { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }

    // ---------- الهوية والوثائق ----------
    /// <summary>رقم الهوية الوطنية أو الإقامة.</summary>
    public string? NationalId { get; set; }
    public string IdType { get; set; } = EmployeeIdTypes.NationalId;
    /// <summary>تاريخ انتهاء الهوية/الإقامة.</summary>
    public DateTime? IdExpiryDate { get; set; }
    public string? PassportNumber { get; set; }
    public DateTime? PassportExpiryDate { get; set; }

    // ---------- الوظيفة والعقد ----------
    public Guid? DepartmentId { get; set; }
    public string? JobTitle { get; set; }
    public DateTime HireDate { get; set; }
    public string ContractType { get; set; } = EmployeeContractTypes.Unlimited;
    public DateTime? ContractStartDate { get; set; }
    /// <summary>نهاية العقد محدد المدة.</summary>
    public DateTime? ContractEndDate { get; set; }
    public DateTime? ProbationEndDate { get; set; }
    public Guid? CostCenterId { get; set; }

    // ---------- الإجازة السنوية ----------
    /// <summary>استحقاق الإجازة السنوية بالأيام؛ فارغ = حسب نظام العمل (21 يوماً، و30 بعد خمس سنوات خدمة).</summary>
    public int? AnnualLeaveDays { get; set; }
    /// <summary>رصيد إجازات افتتاحي في تاريخه (لموظف سابق على استخدام النظام)؛ التراكم يبدأ من ذلك التاريخ.</summary>
    public decimal OpeningLeaveBalance { get; set; }
    public DateTime? OpeningLeaveBalanceDate { get; set; }

    // ---------- الراتب والتأمينات ----------
    public decimal BasicSalary { get; set; }
    public decimal HousingAllowance { get; set; }
    public decimal TransportAllowance { get; set; }
    public decimal OtherAllowances { get; set; }
    /// <summary>نسبة حصة الموظف في التأمينات الاجتماعية (%) من الأساسي + السكن.</summary>
    public decimal EmployeeGosiRate { get; set; }
    /// <summary>نسبة حصة المنشأة في التأمينات الاجتماعية (%) من الأساسي + السكن.</summary>
    public decimal EmployerGosiRate { get; set; }
    /// <summary>رقم المشترك في التأمينات الاجتماعية.</summary>
    public string? GosiNumber { get; set; }
    public string? BankName { get; set; }
    public string? Iban { get; set; }

    // ---------- الحالة ----------
    public string Status { get; set; } = EmployeeStatuses.Active;
    /// <summary>آخر يوم عمل عند انتهاء الخدمة.</summary>
    public DateTime? TerminationDate { get; set; }
    public string? TerminationReason { get; set; }
    public string? Notes { get; set; }
}

public static class EmployeeStatuses
{
    public const string Active = "active";
    public const string Inactive = "inactive";
    public const string Terminated = "terminated";
    public static readonly string[] All = { Active, Inactive, Terminated };
}

public static class EmployeeIdTypes
{
    public const string NationalId = "national_id";
    public const string Iqama = "iqama";
    public static readonly string[] All = { NationalId, Iqama };
}

public static class EmployeeContractTypes
{
    public const string Unlimited = "unlimited";
    public const string Fixed = "fixed";
    public static readonly string[] All = { Unlimited, Fixed };
}
