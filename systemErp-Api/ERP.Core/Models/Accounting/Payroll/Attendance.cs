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
