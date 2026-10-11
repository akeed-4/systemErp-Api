namespace ERP.Core.Models.Accounting;

/// <summary>
/// سلفة أو قرض لموظف يُسدَّد أقساطاً شهرية تُخصم من مسير الرواتب ابتداءً من شهر محدد.
/// صرف السلفة يرحّل قيداً (مدين سلف الموظفين، دائن حساب الصرف)، وكل قسط يُقفل جزءاً منها.
/// </summary>
public class EmployeeLoan : BaseEntity
{
    public string LoanNumber { get; set; } = string.Empty;
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    /// <summary>القسط الشهري المخصوم من المسير.</summary>
    public decimal InstallmentAmount { get; set; }
    /// <summary>أول شهر يُخصم فيه القسط (yyyy-MM).</summary>
    public string FirstDeductionPeriod { get; set; } = string.Empty;
    public decimal PaidAmount { get; set; }
    public string Status { get; set; } = LoanStatuses.Active;
    public string? Reason { get; set; }
    /// <summary>حساب الصرف (صندوق أو بنك) الذي خرجت منه السلفة.</summary>
    public string PaymentAccountCode { get; set; } = string.Empty;
    public Guid? JournalEntryId { get; set; }
}

/// <summary>سداد من سلفة: قسط مخصوم في مسير رواتب، أو تسوية الرصيد عند نهاية الخدمة.</summary>
public class EmployeeLoanRepayment : BaseEntity
{
    public Guid EmployeeLoanId { get; set; }
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    /// <summary>payroll | settlement</summary>
    public string Source { get; set; } = LoanRepaymentSources.Payroll;
    public string? Period { get; set; }
    public Guid? PayrollRunId { get; set; }
    public Guid? SettlementId { get; set; }
}

public static class LoanStatuses
{
    public const string Active = "active";
    public const string Settled = "settled";
    public const string Cancelled = "cancelled";
}

public static class LoanRepaymentSources
{
    public const string Payroll = "payroll";
    public const string Settlement = "settlement";
}

/// <summary>
/// تصفية مستحقات موظف عند نهاية خدمته: مكافأة نهاية الخدمة (نظام العمل، المادتان 84 و85) + بدل رصيد الإجازات
/// + مستحقات أخرى − خصومات − رصيد السلف. الأرقام تُحفظ كما حُسبت وقت التصفية.
/// </summary>
public class EndOfServiceSettlement : BaseEntity
{
    public string SettlementNumber { get; set; } = string.Empty;
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public DateTime HireDate { get; set; }
    public DateTime LastWorkingDay { get; set; }
    public string Reason { get; set; } = EndOfServiceReasons.Termination;
    public decimal ServiceYears { get; set; }
    /// <summary>الأجر الشهري الأخير (الأساسي والبدلات) الذي حُسبت عليه المكافأة.</summary>
    public decimal Wage { get; set; }
    /// <summary>المكافأة كاملة قبل تطبيق نسبة سبب انتهاء الخدمة.</summary>
    public decimal FullAward { get; set; }
    /// <summary>نسبة الاستحقاق من المكافأة (0، ثلث، ثلثان، 1).</summary>
    public decimal AwardFactor { get; set; }
    public decimal Award { get; set; }
    public decimal LeaveBalanceDays { get; set; }
    public decimal LeavePayout { get; set; }
    public decimal OtherAdditions { get; set; }
    public decimal OtherDeductions { get; set; }
    public decimal LoanDeduction { get; set; }
    public decimal Net { get; set; }
    public string Status { get; set; } = "posted"; // posted | reversed
    public Guid? JournalEntryId { get; set; }
    public string? Notes { get; set; }
}

public static class EndOfServiceReasons
{
    /// <summary>استقالة: لا مكافأة قبل سنتين، ثلثها حتى خمس، ثلثاها حتى عشر، ثم كاملة.</summary>
    public const string Resignation = "resignation";
    /// <summary>إنهاء من صاحب العمل.</summary>
    public const string Termination = "termination";
    public const string ContractEnd = "contract_end";
    public const string Retirement = "retirement";
    /// <summary>وفاة أو عجز أو قوة قاهرة: المكافأة كاملة.</summary>
    public const string ForceMajeure = "force_majeure";
    /// <summary>فصل بموجب المادة 80: بلا مكافأة.</summary>
    public const string Dismissal = "dismissal";
    public static readonly string[] All = { Resignation, Termination, ContractEnd, Retirement, ForceMajeure, Dismissal };
}
