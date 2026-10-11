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
    public int AttachmentCount { get; set; }
}

/// <summary>بيانات مرفق طلب الإجازة دون محتواه.</summary>
public class LeaveAttachmentDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid LeaveRequestId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public string? UploadedBy { get; set; }
}

/// <summary>محتوى المرفق للتنزيل.</summary>
public class LeaveAttachmentFileDto
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[] Content { get; set; } = Array.Empty<byte>();
}

/// <summary>طلب إجازة يقدّمه الموظف لنفسه من البوابة: الموظف يُؤخذ من جلسة الدخول لا من الطلب.</summary>
public class CreateMyLeaveRequestDto
{
    public Guid LeaveTypeId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? Reason { get; set; }
}

public class MyLeaveRequestDto : LeaveRequestDto
{
    public List<LeaveAttachmentDto> Attachments { get; set; } = new();
}

/// <summary>كل ما تعرضه بوابة الموظف: بياناته المختصرة ورصيده وأنواع الإجازات المتاحة وطلباته.</summary>
public class MyLeaveOverviewDto
{
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string? JobTitle { get; set; }
    public LeaveBalanceDto Balance { get; set; } = new();
    public List<LeaveTypeDto> LeaveTypes { get; set; } = new();
    public List<MyLeaveRequestDto> Requests { get; set; } = new();
}

/// <summary>
/// حالة دخول الموظف للبوابة: none (لم يُدعَ) | invited (دُعي ولم يعيّن كلمة مرور) | active | disabled.
/// رمز الدعوة يُعاد مرة واحدة عند إنشائها فقط ليُبنى منه الرابط.
/// </summary>
public class PortalAccessDto
{
    public string Status { get; set; } = PortalAccessStatuses.None;
    public string? Email { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public string? InviteToken { get; set; }
    public DateTime? InviteExpiresAt { get; set; }
    public bool EmailSent { get; set; }
}

public static class PortalAccessStatuses
{
    public const string None = "none";
    public const string Invited = "invited";
    public const string Active = "active";
    public const string Disabled = "disabled";
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
