namespace ERP.Core.Models.Accounting;

/// <summary>
/// طريقة فوترة إيجار العقد (T1/Q1): من كشوف الساعات المعتمدة من العميل (الافتراضي للعقود الجديدة)، أو بالمدة المنقضية من التسليم
/// (السلوك السابق، وتبقى عليه العقود القائمة).
/// </summary>
public static class RentalBillingModes
{
    public const string Timesheet = "timesheet";
    public const string Time = "time";
    public static bool IsValid(string? v) => v is Timesheet or Time;
}

/// <summary>
/// حالة كشف الساعات: مسودة ← مقدَّم للعميل ← معتمد (بصورة الكشف الموقّع، والتوقيع الإلكتروني اختياري) ← مفوتر، أو مرفوض فيعود للتعديل.
/// المعتمد غير المفوتر يُعاد فتحه بسبب.
/// </summary>
public static class RentalTimesheetStatuses
{
    public const string Draft = "draft";
    public const string Submitted = "submitted";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Billed = "billed";

    /// <summary>الكشوف التي تحجز أيامها (لا يتداخل معها كشف آخر للسطر).</summary>
    public static readonly string[] Occupying = { Draft, Submitted, Approved, Billed };
    /// <summary>القابلة للتعديل والحذف.</summary>
    public static readonly string[] Editable = { Draft, Rejected };
}

/// <summary>
/// نوع اليوم في الكشف: تشغيل (ساعاته الفعلية، والحد الأدنى اليومي يسري)، انتظار (المعدة جاهزة والعميل لم يشغّلها، بنسبة الانتظار)،
/// عطل (علينا فلا يُحاسب)، إجازة (الجمعة والأعياد، لا تُحاسب، وتشغيلها يوم تشغيل إضافي).
/// </summary>
public static class RentalTimesheetDayKinds
{
    public const string Work = "work";
    public const string Standby = "standby";
    public const string Breakdown = "breakdown";
    public const string Off = "off";
    public static bool IsValid(string? v) => v is Work or Standby or Breakdown or Off;
}

/// <summary>
/// كشف ساعات معدة في عقد تأجير لفترة (يوم أو أسبوع أو شهر) بأيامها. يعتمده ممثل العميل (اسمه وصفته، وصورة الكشف الورقي إلزامية بالإعداد،
/// والتوقيع الإلكتروني اختياري)، فتُجمَّد مبالغه بشروط السطر لحظة الاعتماد وتُفوتر منها. لا يتداخل كشفان لسطر واحد.
/// </summary>
public class RentalTimesheet : BaseEntity
{
    public string TimesheetNumber { get; set; } = string.Empty;
    public Guid ContractId { get; set; }
    public Guid LineId { get; set; }
    public Guid EquipmentId { get; set; }
    /// <summary>أول يوم وآخر يوم في الكشف (تاريخ بلا وقت).</summary>
    public DateTime PeriodFrom { get; set; }
    public DateTime PeriodTo { get; set; }
    public string Status { get; set; } = RentalTimesheetStatuses.Draft;
    public Guid? OperatorEmployeeId { get; set; }

    // اعتماد العميل
    public string? ClientApproverName { get; set; }
    public string? ClientApproverTitle { get; set; }
    /// <summary>صورة التوقيع الإلكتروني (data URL) إن وُقّع داخل النظام.</summary>
    public string? ClientSignature { get; set; }
    public DateTime? ClientApprovedAt { get; set; }

    // المجاميع والمبالغ المجمّدة عند الاعتماد (تُحسب حية للمسودة)
    public decimal WorkDays { get; set; }
    public decimal StandbyDays { get; set; }
    public decimal BreakdownDays { get; set; }
    public decimal WorkedHours { get; set; }
    public decimal StandbyHours { get; set; }
    public decimal BreakdownHours { get; set; }
    /// <summary>الساعات المحاسبة بالحد الأدنى (أساس الساعة) أو أيام التشغيل (غيره).</summary>
    public decimal BilledQuantity { get; set; }
    public decimal OvertimeHours { get; set; }
    public decimal RentAmount { get; set; }
    public decimal StandbyAmount { get; set; }
    public decimal OvertimeAmount { get; set; }
    public decimal OperatorAmount { get; set; }
    public decimal Amount { get; set; }

    public Guid? BillingId { get; set; }
    public string? RejectReason { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public virtual ICollection<RentalTimesheetDay> Days { get; set; } = new List<RentalTimesheetDay>();
}

/// <summary>يوم في كشف الساعات: نوعه وساعات تشغيله وانتظاره وعطله، وقراءتا العدّاد اختيارياً.</summary>
public class RentalTimesheetDay : BaseEntity
{
    public Guid TimesheetId { get; set; }
    public DateTime Date { get; set; }
    public string Kind { get; set; } = RentalTimesheetDayKinds.Work;
    public decimal WorkedHours { get; set; }
    public decimal StandbyHours { get; set; }
    public decimal BreakdownHours { get; set; }
    public decimal? MeterStart { get; set; }
    public decimal? MeterEnd { get; set; }
    public string? Notes { get; set; }
    public virtual RentalTimesheet Timesheet { get; set; } = null!;
}
