namespace ERP.Core.DTOs.Accounting.Contracts;

/// <summary>
/// حضور فريق المشروع في يوم: الموظفون المسندون (ومن سجّل على المشروع ولم يعد مسنداً)، وحالتهم وأوقاتهم وتحقق مواقعهم واستثناءاتهم.
/// موظف حضر على مشروع آخر يُعلَّم بلا تفاصيل سجله.
/// </summary>
public class ProjectAttendanceDayDto
{
    public DateTime Date { get; set; }
    public bool GpsAttendanceEnabled { get; set; }
    public string OutOfRangePolicy { get; set; } = string.Empty;
    public ProjectAttendanceTotalsDto Totals { get; set; } = new();
    public List<ProjectAttendanceRowDto> Rows { get; set; } = new();
}

public class ProjectAttendanceTotalsDto
{
    public int Assigned { get; set; }
    public int CheckedIn { get; set; }
    public int CheckedOut { get; set; }
    /// <summary>مسندون بلا سجل في اليوم ولا إجازة معتمدة.</summary>
    public int NotRecorded { get; set; }
    public int OnLeave { get; set; }
    /// <summary>استثناءات المشروع بانتظار المراجعة (كل الأيام).</summary>
    public int PendingExceptions { get; set; }
    /// <summary>دقائق العمل في السجلات المعتمدة على المشروع في اليوم (تحقق موقعها أو اعتُمد استثناؤها).</summary>
    public int ApprovedWorkedMinutes { get; set; }
}

public class ProjectAttendanceRowDto
{
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string? EmployeeNameEn { get; set; }
    /// <summary>اسم مورد المشروع ونوعه (عمالة/مهندس)؛ فارغ لمن لم يعد مسنداً.</summary>
    public string? ResourceName { get; set; }
    public string? ResourceKind { get; set; }
    public bool Assigned { get; set; }
    public bool OnApprovedLeave { get; set; }
    /// <summary>حضر اليوم على مشروع آخر (لا تُعرض تفاصيل سجله).</summary>
    public bool OnOtherProject { get; set; }
    public string? Status { get; set; }
    public string? Source { get; set; }
    public string? CheckIn { get; set; }
    public string? CheckOut { get; set; }
    public int LateMinutes { get; set; }
    public int? WorkedMinutes { get; set; }
    public string? GpsStatus { get; set; }
    public List<ProjectAttendancePunchDto> Punches { get; set; } = new();
}

/// <summary>تسجيل ميداني؛ الإحداثيات تُعرض لاستثناء خارج النطاق وحده (ليحكم المراجع)، لا لكل تسجيل.</summary>
public class ProjectAttendancePunchDto
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public DateTime RecordedAt { get; set; }
    public decimal AccuracyMeters { get; set; }
    public decimal DistanceMeters { get; set; }
    public int RadiusMeters { get; set; }
    public bool WithinGeofence { get; set; }
    public string? ExceptionReason { get; set; }
    public string? ReviewStatus { get; set; }
    public string? ReviewNote { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
}

/// <summary>استثناء بانتظار المراجعة في قائمة المشروع (كل الأيام).</summary>
public class ProjectAttendanceExceptionDto
{
    public Guid PunchId { get; set; }
    public DateTime Date { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string? EmployeeNameEn { get; set; }
    public string Kind { get; set; } = string.Empty;
    public DateTime RecordedAt { get; set; }
    public string? LocalTime { get; set; }
    public decimal AccuracyMeters { get; set; }
    public decimal DistanceMeters { get; set; }
    public int RadiusMeters { get; set; }
    public string? ExceptionReason { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
}
