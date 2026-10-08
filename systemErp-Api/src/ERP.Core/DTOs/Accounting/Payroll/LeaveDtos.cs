namespace ERP.Core.DTOs.Accounting;

public class CreateLeaveTypeDto
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public bool IsAnnual { get; set; }
    public decimal PayPercent { get; set; } = 100;
    public int? MaxDaysPerYear { get; set; }
    public bool IsActive { get; set; } = true;
}

public class UpdateLeaveTypeDto : CreateLeaveTypeDto
{
}

public class LeaveTypeDto : CreateLeaveTypeDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateLeaveRequestDto
{
    public Guid EmployeeId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? Reason { get; set; }
}

public class UpdateLeaveRequestDto : CreateLeaveRequestDto
{
}

public class LeaveDecisionDto
{
    public string? Note { get; set; }
}

public class LeaveRequestDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public Guid LeaveTypeId { get; set; }
    public string LeaveTypeName { get; set; } = string.Empty;
    public bool IsAnnual { get; set; }
    public decimal PayPercent { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int Days { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
}

/// <summary>رصيد الإجازة السنوية لموظف حتى تاريخ: الافتتاحي + المتراكم − المعتمد.</summary>
public class LeaveBalanceDto
{
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public DateTime AsOf { get; set; }
    /// <summary>الاستحقاق السنوي الحالي بالأيام.</summary>
    public int AnnualEntitlement { get; set; }
    public decimal Opening { get; set; }
    public decimal Accrued { get; set; }
    /// <summary>أيام الإجازة السنوية المعتمدة.</summary>
    public decimal Taken { get; set; }
    /// <summary>أيام طلبات سنوية بانتظار الاعتماد.</summary>
    public decimal Pending { get; set; }
    public decimal Balance { get; set; }
    /// <summary>الرصيد بعد طرح الطلبات المعلّقة.</summary>
    public decimal Available { get; set; }
}
