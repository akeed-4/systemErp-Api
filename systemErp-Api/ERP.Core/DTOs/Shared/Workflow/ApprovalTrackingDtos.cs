namespace ERP.Core.DTOs.Shared;

/// <summary>مستند يُعتمد من شاشته: حالته ومقدّمه ومن ينتظر اعتماده (لشاشة «مستنداتي بانتظار الاعتماد» و«بانتظار اعتمادي»).</summary>
public class ApprovalTrackItemDto
{
    public Guid Id { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string DocumentNameAr { get; set; } = string.Empty;
    public string DocumentNameEn { get; set; } = string.Empty;
    public Guid DocumentId { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string ScreenId { get; set; } = string.Empty;
    public Guid? RequesterUserId { get; set; }
    public string RequesterName { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
    /// <summary>pending | approved | rejected | returned | cancelled</summary>
    public string Status { get; set; } = string.Empty;
    public string? DecidedByName { get; set; }
    public DateTime? DecidedAt { get; set; }
    /// <summary>رابط شاشة الواجهة التي يُعتمد منها المستند (مسار + query).</summary>
    public string Url { get; set; } = string.Empty;
    /// <summary>الأدوار المخوّلة باعتماده (للمعلّق).</summary>
    public List<ApproverRoleDto> ApproverRoles { get; set; } = new();
    /// <summary>هل يملك المستخدم الحالي اعتماده.</summary>
    public bool CanApprove { get; set; }
}

public class ApprovalTrackSummaryDto
{
    /// <summary>مستندات قدّمها المستخدم ولم تُعتمد بعد.</summary>
    public int MinePending { get; set; }
    /// <summary>مستندات معلّقة على شاشات يملك المستخدم اعتمادها.</summary>
    public int AwaitingMe { get; set; }
}
