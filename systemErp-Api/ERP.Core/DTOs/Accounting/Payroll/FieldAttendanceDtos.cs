namespace ERP.Core.DTOs.Accounting;

/// <summary>
/// قراءة الجهاز عند الحضور أو الانصراف. الموظف لا يُرسَل: يُؤخذ من جلسة الدخول. المشروع اختياري حين يكون الموظف
/// مُسنداً لمشروع واحد، ويُتحقق في الخادم أنه من مشاريعه.
/// </summary>
public class GpsPunchRequestDto
{
    public Guid? ContractId { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public decimal AccuracyMeters { get; set; }
    /// <summary>وقت الالتقاط في الجهاز (للتدقيق فقط؛ المعتمد وقت الخادم).</summary>
    public DateTime? CapturedAt { get; set; }
    /// <summary>سبب التسجيل من خارج النطاق (لمشروع يقبل الاستثناء)؛ يراجعه مشرف المشروع.</summary>
    public string? ExceptionReason { get; set; }
}

/// <summary>شاشة حضور الموظف الميداني لليوم: مشاريعه المسندة، وسجل اليوم، وما يمكنه الآن.</summary>
public class MyFieldAttendanceDto
{
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string? EmployeeNameEn { get; set; }
    /// <summary>اليوم بالتوقيت المحلي للمنشأة.</summary>
    public DateTime Date { get; set; }
    public string TimeZoneId { get; set; } = string.Empty;
    public List<MyFieldProjectDto> Projects { get; set; } = new();
    public MyFieldAttendanceRecordDto? Record { get; set; }
    public bool CanCheckIn { get; set; }
    public bool CanCheckOut { get; set; }
}

/// <summary>مشروع مُسند للموظف (لا يكشف إحداثيات الموقع).</summary>
public class MyFieldProjectDto
{
    public Guid ContractId { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string? ProjectNameEn { get; set; }
    public bool GpsAttendanceEnabled { get; set; }
    public int RadiusMeters { get; set; }
    public int MaxAccuracyMeters { get; set; }
    /// <summary>التسجيل من خارج النطاق يُقبل استثناءً بسبب (وإلا يُرفض).</summary>
    public bool AllowsOutOfRangeException { get; set; }
    /// <summary>بداية الدوام HH:mm (منها التأخير).</summary>
    public string WorkdayStart { get; set; } = string.Empty;
    public string WorkdayEnd { get; set; } = string.Empty;
}

public class MyFieldAttendanceRecordDto
{
    public string Status { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public Guid? ContractId { get; set; }
    public string? CheckIn { get; set; }
    public string? CheckOut { get; set; }
    public DateTime? CheckInAt { get; set; }
    public DateTime? CheckOutAt { get; set; }
    public int LateMinutes { get; set; }
    public int? WorkedMinutes { get; set; }
    public string? GpsStatus { get; set; }
    public List<MyGpsPunchDto> Punches { get; set; } = new();
}

/// <summary>نتيجة تحقق الموقع لكل تسجيل (البعد والدقة وقرار الاستثناء، بلا الإحداثيات).</summary>
public class MyGpsPunchDto
{
    public string Kind { get; set; } = string.Empty;
    public DateTime RecordedAt { get; set; }
    public decimal AccuracyMeters { get; set; }
    public decimal DistanceMeters { get; set; }
    public int RadiusMeters { get; set; }
    public bool WithinGeofence { get; set; }
    public string? ExceptionReason { get; set; }
    public string? ReviewStatus { get; set; }
    public string? ReviewNote { get; set; }
}

/// <summary>
/// تسجيل ميداني تحقّق منه <c>FieldAttendanceService</c> (الهوية والإسناد والنطاق والدقة)، يكتبه سجل الحضور
/// بقواعده (قفل الشهر المرحّل، سجل واحد في اليوم). ليس طلباً من العميل.
/// </summary>
public class FieldPunchCommand
{
    public Guid EmployeeId { get; set; }
    public Guid ContractId { get; set; }
    /// <summary>اليوم المحلي للمنشأة.</summary>
    public DateTime Day { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    /// <summary>دقائق الوقت المحلي من منتصف الليل.</summary>
    public int LocalMinutes { get; set; }
    public int WorkdayStartMinutes { get; set; }
    public int? BreakStartMinutes { get; set; }
    public int? BreakEndMinutes { get; set; }
    public DateTime? DeviceCapturedAt { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public decimal AccuracyMeters { get; set; }
    public decimal DistanceMeters { get; set; }
    public int RadiusMeters { get; set; }
    /// <summary>خارج النطاق، مقبول استثناءً بسببه بانتظار المراجعة.</summary>
    public bool OutsideGeofence { get; set; }
    public string? ExceptionReason { get; set; }
}

/// <summary>قرار مراجع المشروع في استثناء خارج النطاق؛ الملاحظة إلزامية للرفض.</summary>
public class GpsExceptionReviewDto
{
    public string? Note { get; set; }
}
