namespace ERP.Core.DTOs.Accounting.Contracts;

/// <summary>طلب نقل العقد إلى حالة؛ الملاحظة إلزامية للانتقالات التي تتطلب سبباً.</summary>
public class MoveContractRequestDto
{
    public string ToStatus { get; set; } = string.Empty;
    public string? Note { get; set; }
}

/// <summary>قرار مراجع: kind = legal | financial، decision = approve | return (الإعادة تتطلب ملاحظة).</summary>
public class ContractReviewRequestDto
{
    public string Kind { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public string? Note { get; set; }
}

/// <summary>بيانات التوقيع والضمان البنكي (تُعدَّل قبل التفعيل فقط).</summary>
public class ContractSigningDto
{
    public DateTime? SignedDate { get; set; }
    public string? SignedByCompany { get; set; }
    public string? SignedByParty { get; set; }
    public string? GuaranteeNumber { get; set; }
    public string? GuaranteeBank { get; set; }
    public decimal? GuaranteeAmount { get; set; }
    public DateTime? GuaranteeExpiry { get; set; }
}

/// <summary>حالة العقد وما يستطيعه المستخدم الحالي عليه: الانتقالات المسموحة له (وما ينقص كلاً منها)، المراجعات، السجل، المرفقات.</summary>
public class ContractWorkflowDto
{
    public Guid ContractId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string StatusNameAr { get; set; } = string.Empty;
    public string StatusNameEn { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public DateTime? StatusSince { get; set; }
    public int DaysInStatus { get; set; }
    public int? SlaDays { get; set; }
    /// <summary>أيام التأخر عن المدة المستهدفة للحالة؛ null = غير متأخر.</summary>
    public int? OverdueDays { get; set; }
    public string ContractType { get; set; } = string.Empty;
    public string Workflow { get; set; } = string.Empty;
    public string Billing { get; set; } = string.Empty;
    public bool IsSimplified { get; set; }
    public bool RequiresGuarantee { get; set; }
    public int ReviewRound { get; set; }
    /// <summary>للمراجعين: يستطيع تسجيل قراره القانوني/المالي الآن (والعقد في المراجعة ولم يسجّله في هذه الجولة).</summary>
    public bool CanReviewLegal { get; set; }
    public bool CanReviewFinancial { get; set; }
    public bool CanEditSigning { get; set; }
    public List<ContractTransitionOptionDto> Transitions { get; set; } = new();
    public List<ContractApprovalDto> Approvals { get; set; } = new();
    public List<ContractStatusLogDto> Log { get; set; } = new();
    public List<ContractAttachmentDto> Attachments { get; set; } = new();
}

public class ContractTransitionOptionDto
{
    public int Id { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public bool RequiresNote { get; set; }
    /// <summary>primary | danger | neutral</summary>
    public string Tone { get; set; } = "primary";
    /// <summary>ما ينقص قبل تنفيذه (فارغ = جاهز).</summary>
    public List<string> Blockers { get; set; } = new();
}

public class ContractApprovalDto
{
    public string Kind { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime DecidedAt { get; set; }
}

public class ContractStatusLogDto
{
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime At { get; set; }
}

public class ContractAttachmentDto
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public string? UploadedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed record ContractFileDto(string FileName, string ContentType, byte[] Content);
