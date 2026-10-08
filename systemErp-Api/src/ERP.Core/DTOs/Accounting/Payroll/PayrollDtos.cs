namespace ERP.Core.DTOs.Accounting;

public class CreateEmployeeDto
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string? Nationality { get; set; }
    public string? Gender { get; set; }
    public DateTime? BirthDate { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? NationalId { get; set; }
    public string IdType { get; set; } = "national_id";
    public DateTime? IdExpiryDate { get; set; }
    public string? PassportNumber { get; set; }
    public DateTime? PassportExpiryDate { get; set; }
    public Guid? DepartmentId { get; set; }
    public string? JobTitle { get; set; }
    public DateTime HireDate { get; set; }
    public string ContractType { get; set; } = "unlimited";
    public DateTime? ContractStartDate { get; set; }
    public DateTime? ContractEndDate { get; set; }
    public DateTime? ProbationEndDate { get; set; }
    public int? AnnualLeaveDays { get; set; }
    public decimal OpeningLeaveBalance { get; set; }
    public DateTime? OpeningLeaveBalanceDate { get; set; }
    public decimal BasicSalary { get; set; }
    public decimal HousingAllowance { get; set; }
    public decimal TransportAllowance { get; set; }
    public decimal OtherAllowances { get; set; }
    public decimal EmployeeGosiRate { get; set; }
    public decimal EmployerGosiRate { get; set; }
    public string? GosiNumber { get; set; }
    public string? BankName { get; set; }
    public string? Iban { get; set; }
    public Guid? CostCenterId { get; set; }
    public string Status { get; set; } = "active";
    public DateTime? TerminationDate { get; set; }
    public string? TerminationReason { get; set; }
    public string? Notes { get; set; }
}

public class UpdateEmployeeDto : CreateEmployeeDto
{
}

public class EmployeeDto : CreateEmployeeDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>إضافات وخصومات موظف في شهر المسير.</summary>
public class PayrollAdjustmentDto
{
    public Guid EmployeeId { get; set; }
    public decimal Additions { get; set; }
    public decimal Deductions { get; set; }
}

public class PayrollRunRequestDto
{
    /// <summary>الشهر بصيغة yyyy-MM.</summary>
    public string Period { get; set; } = string.Empty;
    public List<PayrollAdjustmentDto> Adjustments { get; set; } = new();
    public string? Notes { get; set; }
}

public class PayrollLineDto
{
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public decimal BasicSalary { get; set; }
    public decimal HousingAllowance { get; set; }
    public decimal TransportAllowance { get; set; }
    public decimal OtherAllowances { get; set; }
    public decimal Additions { get; set; }
    /// <summary>كل خصومات الشهر: اليدوية مع خصم الإجازة غير المدفوعة.</summary>
    public decimal Deductions { get; set; }
    /// <summary>الخصم اليدوي المُدخل في المسير.</summary>
    public decimal ManualDeductions { get; set; }
    public decimal AbsentDays { get; set; }
    public int LateMinutes { get; set; }
    /// <summary>خصم الغياب والتأخير، وهو ضمن Deductions.</summary>
    public decimal AttendanceDeduction { get; set; }
    public decimal OvertimeHours { get; set; }
    /// <summary>أجر العمل الإضافي، وهو ضمن Gross.</summary>
    public decimal OvertimePay { get; set; }
    /// <summary>أيام الإجازة بلا أجر في الشهر (يوم بنصف أجر = نصف يوم).</summary>
    public decimal UnpaidLeaveDays { get; set; }
    /// <summary>خصم الإجازة غير المدفوعة، وهو ضمن Deductions.</summary>
    public decimal LeaveDeduction { get; set; }
    /// <summary>أقساط السلف المخصومة من الصافي.</summary>
    public decimal LoanDeduction { get; set; }
    public decimal Gross { get; set; }
    public decimal EmployeeGosi { get; set; }
    public decimal EmployerGosi { get; set; }
    public decimal Net { get; set; }
}

public class PayrollRunDto
{
    /// <summary>فارغ في المعاينة قبل الترحيل.</summary>
    public Guid? Id { get; set; }
    public string? RunNumber { get; set; }
    public string Period { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public decimal TotalGross { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal TotalEmployeeGosi { get; set; }
    public decimal TotalEmployerGosi { get; set; }
    public decimal TotalLoanDeductions { get; set; }
    public decimal TotalNet { get; set; }
    public Guid? JournalEntryId { get; set; }
    public string Status { get; set; } = "preview";
    public string? Notes { get; set; }
    public List<PayrollLineDto> Lines { get; set; } = new();
}
