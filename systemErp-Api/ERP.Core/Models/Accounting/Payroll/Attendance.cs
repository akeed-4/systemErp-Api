namespace ERP.Core.Models.Accounting;

/// <summary>
/// سجل حضور موظف في يوم: حالته، دقائق تأخره، وساعات عمله الإضافية. سجل واحد لكل موظف في اليوم.
/// الغياب والتأخير يُخصمان في مسير الشهر، والإضافي يُضاف إليه.
/// </summary>
public class AttendanceRecord : BaseEntity
{
    public Guid EmployeeId { get; set; }
    public DateTime Date { get; set; }
    public string Status { get; set; } = AttendanceStatuses.Present;
    public int LateMinutes { get; set; }
    public decimal OvertimeHours { get; set; }
    /// <summary>وقتا الحضور والانصراف بصيغة HH:mm (للتوثيق).</summary>
    public string? CheckIn { get; set; }
    public string? CheckOut { get; set; }
    public string? Notes { get; set; }

    // ---------- مصدر السجل والحضور الميداني ----------
    /// <summary><see cref="AttendanceSources"/>: كيف دخل السجل (كشف اليوم، الاستيراد، أو الجوال بالموقع).</summary>
    public string Source { get; set; } = AttendanceSources.Manual;
    /// <summary>مشروع المقاولات (العقد) الذي سُجّل عليه الحضور الميداني؛ فارغ لغيره.</summary>
    public Guid? ContractId { get; set; }
    /// <summary>لحظتا الحضور والانصراف الميدانيين (UTC)؛ <see cref="CheckIn"/>/<see cref="CheckOut"/> نسختهما بالتوقيت المحلي للمنشأة.</summary>
    public DateTime? CheckInAt { get; set; }
    public DateTime? CheckOutAt { get; set; }
    /// <summary>دقائق العمل الفعلية بين الحضور والانصراف الميدانيين بعد خصم استراحة المشروع (فارغ إن رُفض استثناء الانصراف).</summary>
    public int? WorkedMinutes { get; set; }
    /// <summary><see cref="GpsAttendanceStatuses"/> للسجل الميداني (فارغ لغيره): من حالة تسجيلاته.</summary>
    public string? GpsStatus { get; set; }

    /// <summary>دليل الموقع لكل حضور وانصراف ميداني (واحد لكل نوع).</summary>
    public virtual ICollection<AttendanceGpsPunch> Punches { get; set; } = new List<AttendanceGpsPunch>();
}

public static class AttendanceSources
{
    /// <summary>كشف اليوم في شؤون الموظفين (والسجلات السابقة لتمييز المصدر).</summary>
    public const string Manual = "manual";
    /// <summary>الاستيراد الجماعي (ملف جهاز البصمة أو Excel).</summary>
    public const string Import = "import";
    /// <summary>الموظف من الجوال، موثقاً بموقعه داخل نطاق المشروع.</summary>
    public const string MobileGps = "mobile_gps";
    public static readonly string[] All = { Manual, Import, MobileGps };
}

/// <summary>
/// دليل الموقع لحضور أو انصراف ميداني: الإحداثيات ودقتها وبعدها عن موقع المشروع ونطاقه وقت التسجيل.
/// بيانات موقع شخصية: تُجمع عند التسجيل وحده، ولا يطّلع عليها إلا شؤون الموظفين وإدارة المشروع.
/// </summary>
public class AttendanceGpsPunch : BaseEntity
{
    public Guid AttendanceRecordId { get; set; }
    /// <summary><see cref="AttendancePunchKinds"/>.</summary>
    public string Kind { get; set; } = AttendancePunchKinds.CheckIn;
    public Guid ContractId { get; set; }
    /// <summary>وقت الخادم (UTC): هو المعتمد.</summary>
    public DateTime RecordedAt { get; set; }
    /// <summary>وقت الالتقاط كما أرسله الجهاز (للتدقيق فقط، لا يُعتمد).</summary>
    public DateTime? DeviceCapturedAt { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public decimal AccuracyMeters { get; set; }
    /// <summary>البعد عن موقع المشروع كما حسبه الخادم.</summary>
    public decimal DistanceMeters { get; set; }
    /// <summary>نطاق المشروع وقت التسجيل (لا يتغير بتعديل النطاق لاحقاً).</summary>
    public int GeofenceRadiusMeters { get; set; }

    // ---------- استثناء خارج النطاق ----------
    /// <summary>خارج النطاق: لا يُقبل إلا استثناءً بسبب يراجعه مشرف المشروع (حين تسمح سياسة المشروع).</summary>
    public bool OutsideGeofence { get; set; }
    public string? ExceptionReason { get; set; }
    /// <summary><see cref="GpsExceptionReviews"/>؛ فارغ لتسجيل داخل النطاق.</summary>
    public string? ReviewStatus { get; set; }
    public string? ReviewNote { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }

    public virtual AttendanceRecord Record { get; set; } = null!;
}

public static class AttendancePunchKinds
{
    public const string CheckIn = "check_in";
    public const string CheckOut = "check_out";
}

/// <summary>حالة التحقق من موقع السجل الميداني.</summary>
public static class GpsAttendanceStatuses
{
    /// <summary>كل تسجيلاته داخل النطاق.</summary>
    public const string Valid = "valid";
    /// <summary>فيه استثناء خارج النطاق بانتظار المراجعة (يُحتسب حضوراً حتى يُبتّ فيه).</summary>
    public const string ExceptionPending = "exception_pending";
    /// <summary>استثناءاته اعتُمدت.</summary>
    public const string ExceptionApproved = "exception_approved";
    /// <summary>رُفض استثناء فيه: رفض الحضور يجعل اليوم غياباً، ورفض الانصراف يُسقط ساعات العمل.</summary>
    public const string ExceptionRejected = "exception_rejected";

    /// <summary>الساعات المعتمدة: سجل تحقق موقعه أو اعتُمدت استثناءاته.</summary>
    public static bool IsApproved(string? status) => status is Valid or ExceptionApproved;
}

public static class GpsExceptionReviews
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

public static class AttendanceStatuses
{
    public const string Present = "present";
    /// <summary>غياب بلا إذن: يُخصم أجر يومه.</summary>
    public const string Absent = "absent";
    /// <summary>في إجازة (أثرها المالي من طلب الإجازة لا من هنا).</summary>
    public const string Leave = "leave";
    /// <summary>راحة أسبوعية أو عطلة رسمية.</summary>
    public const string Off = "off";
    public static readonly string[] All = { Present, Absent, Leave, Off };
}
