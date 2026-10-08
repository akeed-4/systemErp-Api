namespace ERP.Core.Models.Accounting;

/// <summary>نوع إجازة: هل تُخصم من الرصيد السنوي، ونسبة الأجر المدفوع خلالها، وحدّها السنوي إن وُجد.</summary>
public class LeaveType : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    /// <summary>الإجازة السنوية: تُخصم أيامها من رصيد الموظف المتراكم.</summary>
    public bool IsAnnual { get; set; }
    /// <summary>نسبة الأجر المدفوع خلال الإجازة (100 = مدفوعة كاملة، 0 = بلا أجر).</summary>
    public decimal PayPercent { get; set; } = 100;
    /// <summary>أقصى أيام في السنة الميلادية لهذا النوع؛ فارغ = بلا حد.</summary>
    public int? MaxDaysPerYear { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// طلب إجازة لموظف. اسم الموظف ونوع الإجازة ونسبة أجرها تُنسخ وقت الطلب فلا يتغيّر الطلب بتعديل لاحق لها.
/// الأيام تقويمية وتشمل يومي البداية والنهاية.
/// </summary>
public class LeaveRequest : BaseEntity
{
    public string RequestNumber { get; set; } = string.Empty;
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public Guid LeaveTypeId { get; set; }
    public string LeaveTypeName { get; set; } = string.Empty;
    public bool IsAnnual { get; set; }
    public decimal PayPercent { get; set; } = 100;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int Days { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = LeaveStatuses.Pending;
    /// <summary>من اعتمد الطلب أو رفضه أو ألغاه، ومتى، وملاحظته.</summary>
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
}

public static class LeaveStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
}
