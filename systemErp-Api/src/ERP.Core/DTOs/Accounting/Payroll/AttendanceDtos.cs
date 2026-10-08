namespace ERP.Core.DTOs.Accounting;

/// <summary>صف موظف في كشف حضور يوم. Status فارغ = لم يُسجَّل بعد.</summary>
public class AttendanceDayRowDto
{
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string? Status { get; set; }
    public int LateMinutes { get; set; }
    public decimal OvertimeHours { get; set; }
    public string? CheckIn { get; set; }
    public string? CheckOut { get; set; }
    public string? Notes { get; set; }
    /// <summary>للموظف إجازة معتمدة تغطي هذا اليوم.</summary>
    public bool OnApprovedLeave { get; set; }
}

public class AttendanceEntryDto
{
    public Guid EmployeeId { get; set; }
    /// <summary>فارغ = حذف سجل اليوم.</summary>
    public string? Status { get; set; }
    public int LateMinutes { get; set; }
    public decimal OvertimeHours { get; set; }
    public string? CheckIn { get; set; }
    public string? CheckOut { get; set; }
    public string? Notes { get; set; }
}

public class SaveAttendanceDayDto
{
    public DateTime Date { get; set; }
    public List<AttendanceEntryDto> Entries { get; set; } = new();
}

/// <summary>صف استيراد (Excel): الموظف برقمه الوظيفي.</summary>
public class AttendanceImportRowDto
{
    public string EmployeeCode { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string? Status { get; set; }
    public int LateMinutes { get; set; }
    public decimal OvertimeHours { get; set; }
    public string? CheckIn { get; set; }
    public string? CheckOut { get; set; }
    public string? Notes { get; set; }
}

/// <summary>ملخص حضور موظف في شهر وأثره المتوقع على المسير.</summary>
public class AttendanceSummaryRowDto
{
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public int PresentDays { get; set; }
    public int AbsentDays { get; set; }
    public int LeaveDays { get; set; }
    public int OffDays { get; set; }
    public int LateMinutes { get; set; }
    public decimal OvertimeHours { get; set; }
    public decimal AttendanceDeduction { get; set; }
    public decimal OvertimePay { get; set; }
}

/// <summary>ملف للتنزيل يُنشأ في الخادم (نص).</summary>
public class TextFileDto
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "text/csv";
    public string Content { get; set; } = string.Empty;
}
