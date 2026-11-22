namespace ERP.Core.DTOs.Accounting;

public class SaveRentalTimesheetDayDto
{
    public DateTime Date { get; set; }
    /// <summary>work | standby | breakdown | off.</summary>
    public string Kind { get; set; } = "work";
    public decimal WorkedHours { get; set; }
    public decimal StandbyHours { get; set; }
    public decimal BreakdownHours { get; set; }
    public decimal? MeterStart { get; set; }
    public decimal? MeterEnd { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// كشف ساعات لسطر عقد في فترة. الأيام الناقصة من الفترة تُضاف: الجمعة إجازة، وغيرها تشغيل بالساعات المشمولة لليوم (تعبئة مبدئية تُعدَّل).
/// </summary>
public class SaveRentalTimesheetDto
{
    public Guid ContractId { get; set; }
    public Guid LineId { get; set; }
    public DateTime PeriodFrom { get; set; }
    public DateTime PeriodTo { get; set; }
    public Guid? OperatorEmployeeId { get; set; }
    public string? Notes { get; set; }
    public List<SaveRentalTimesheetDayDto> Days { get; set; } = new();
}

/// <summary>اعتماد ممثل العميل: اسمه (إلزامي) وصفته، والتوقيع الإلكتروني اختياري (data URL لصورة PNG).</summary>
public class ApproveRentalTimesheetDto
{
    public string ClientApproverName { get; set; } = string.Empty;
    public string? ClientApproverTitle { get; set; }
    public string? ClientSignature { get; set; }
    public DateTime? ClientApprovedAt { get; set; }
}

public class RentalTimesheetDayDto
{
    public Guid Id { get; set; }
    public DateTime Date { get; set; }
    public string Kind { get; set; } = string.Empty;
    public decimal WorkedHours { get; set; }
    public decimal StandbyHours { get; set; }
    public decimal BreakdownHours { get; set; }
    public decimal? MeterStart { get; set; }
    public decimal? MeterEnd { get; set; }
    public string? Notes { get; set; }
}

public class RentalTimesheetDto
{
    public Guid Id { get; set; }
    public string TimesheetNumber { get; set; } = string.Empty;
    public Guid ContractId { get; set; }
    public string? ContractNumber { get; set; }
    public string? CustomerName { get; set; }
    public Guid LineId { get; set; }
    public Guid EquipmentId { get; set; }
    public string? EquipmentCode { get; set; }
    public string? EquipmentName { get; set; }
    public DateTime PeriodFrom { get; set; }
    public DateTime PeriodTo { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid? OperatorEmployeeId { get; set; }
    public string? OperatorName { get; set; }

    /// <summary>شروط السطر المسعَّر بها (للعرض).</summary>
    public string Basis { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public decimal IncludedHoursPerDay { get; set; }
    public decimal DailyMinimumHours { get; set; }
    public decimal StandbyPercent { get; set; }

    public string? ClientApproverName { get; set; }
    public string? ClientApproverTitle { get; set; }
    public bool HasSignature { get; set; }
    public string? ClientSignature { get; set; }
    public DateTime? ClientApprovedAt { get; set; }

    public decimal WorkDays { get; set; }
    public decimal StandbyDays { get; set; }
    public decimal BreakdownDays { get; set; }
    public decimal WorkedHours { get; set; }
    public decimal StandbyHours { get; set; }
    public decimal BreakdownHours { get; set; }
    public decimal BilledQuantity { get; set; }
    public decimal OvertimeHours { get; set; }
    public decimal RentAmount { get; set; }
    public decimal StandbyAmount { get; set; }
    public decimal OvertimeAmount { get; set; }
    public decimal OperatorAmount { get; set; }
    public decimal Amount { get; set; }
    /// <summary>المبالغ مجمّدة (معتمد/مفوتر) أو محسوبة الآن (مسودة).</summary>
    public bool AmountsFrozen { get; set; }

    public int PhotoCount { get; set; }
    public Guid? BillingId { get; set; }
    public string? BillingNumber { get; set; }
    public string? RejectReason { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<RentalTimesheetDayDto> Days { get; set; } = new();
}
