
namespace ERP.Core.Models.Shared;

public class ApprovalRequest : BaseEntity
{
    public Guid PolicyId { get; set; }
    public string PolicyNameAr { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public Guid DocumentId { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public decimal DocumentAmount { get; set; }
    public string? DocumentDataSnapshot { get; set; }

    /// <summary>عملية محجوزة (IApprovalGate): معالجها وبياناتها، وتُنفَّذ بها عند الاعتماد النهائي. لا تُعرض للواجهة.</summary>
    public bool IsDeferred { get; set; }
    public string? Handler { get; set; }
    public string? Payload { get; set; }

    public Guid RequesterUserId { get; set; }
    public string RequesterName { get; set; } = string.Empty;
    
    public int CurrentLevel { get; set; }
    public int TotalLevels { get; set; }
    public string Status { get; set; } = ApprovalRequestStatuses.Pending;
    
    public string? RequiredApproverRole { get; set; }
    public Guid? RequiredApproverUserId { get; set; }
    
    public string? RejectionReason { get; set; }

    /// <summary>موعد قرار المستوى الحالي (من مهلة خطوة السياسة)؛ فارغ = بلا مهلة. يُعاد احتسابه عند الانتقال لمستوى.</summary>
    public DateTime? DueAt { get; set; }
    /// <summary>آخر إجراء على الطلب (قرار أو سحب)؛ يُحدَّث أول القرار فيحجز الصف ويكشف التزامن.</summary>
    public DateTime? LastActionAt { get; set; }
    /// <summary>رمز تزامن: قراران متزامنان على الطلب نفسه لا ينجحان معاً.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public virtual ICollection<ApprovalHistoryItem> History { get; set; } = new List<ApprovalHistoryItem>();
}

/// <summary>حالات طلب الاعتماد. «معاد» = أُعيد لمقدّمه للتعديل (لا يُنفَّذ، ويُعاد إرساله بطلب جديد).</summary>
public static class ApprovalRequestStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Returned = "returned";
    public const string Cancelled = "cancelled";
}

/// <summary>نوع حدث سجل الطلب (التدقيق الكامل): التقديم، والقرارات، والسحب.</summary>
public static class ApprovalHistoryActions
{
    public const string Submitted = "submitted";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Returned = "returned";
    public const string Withdrawn = "withdrawn";
}
