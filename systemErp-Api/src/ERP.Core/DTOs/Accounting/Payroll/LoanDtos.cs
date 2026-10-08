namespace ERP.Core.DTOs.Accounting;

public class CreateEmployeeLoanDto
{
    public Guid EmployeeId { get; set; }
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public decimal InstallmentAmount { get; set; }
    /// <summary>أول شهر خصم (yyyy-MM).</summary>
    public string FirstDeductionPeriod { get; set; } = string.Empty;
    public string? Reason { get; set; }
    /// <summary>حساب الصرف؛ فارغ = الصندوق.</summary>
    public string? PaymentAccountCode { get; set; }
}

/// <summary>إعادة جدولة الأقساط المتبقية.</summary>
public class RescheduleEmployeeLoanDto
{
    public decimal InstallmentAmount { get; set; }
    public string FirstDeductionPeriod { get; set; } = string.Empty;
}

public class EmployeeLoanRepaymentDto
{
    public Guid Id { get; set; }
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? Period { get; set; }
}

public class EmployeeLoanDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string LoanNumber { get; set; } = string.Empty;
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public decimal InstallmentAmount { get; set; }
    public string FirstDeductionPeriod { get; set; } = string.Empty;
    public decimal PaidAmount { get; set; }
    public decimal Remaining { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string PaymentAccountCode { get; set; } = string.Empty;
    public Guid? JournalEntryId { get; set; }
    /// <summary>تُملأ عند قراءة سلفة واحدة.</summary>
    public List<EmployeeLoanRepaymentDto> Repayments { get; set; } = new();
}

public class EndOfServiceRequestDto
{
    public Guid EmployeeId { get; set; }
    public DateTime LastWorkingDay { get; set; }
    /// <summary>resignation | termination | contract_end | retirement | force_majeure | dismissal</summary>
    public string Reason { get; set; } = string.Empty;
    /// <summary>مستحقات أخرى (مثل راتب أيام الشهر الأخير).</summary>
    public decimal OtherAdditions { get; set; }
    public decimal OtherDeductions { get; set; }
    public string? Notes { get; set; }
}

public class EndOfServiceDto
{
    /// <summary>فارغ في المعاينة قبل الترحيل.</summary>
    public Guid? Id { get; set; }
    public string? SettlementNumber { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public DateTime HireDate { get; set; }
    public DateTime LastWorkingDay { get; set; }
    public string Reason { get; set; } = string.Empty;
    public decimal ServiceYears { get; set; }
    public decimal Wage { get; set; }
    public decimal FullAward { get; set; }
    public decimal AwardFactor { get; set; }
    public decimal Award { get; set; }
    public decimal LeaveBalanceDays { get; set; }
    public decimal LeavePayout { get; set; }
    public decimal OtherAdditions { get; set; }
    public decimal OtherDeductions { get; set; }
    public decimal LoanDeduction { get; set; }
    public decimal Net { get; set; }
    public string Status { get; set; } = "preview";
    public Guid? JournalEntryId { get; set; }
    public string? Notes { get; set; }
}
