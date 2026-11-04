
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
    public string Status { get; set; } = "pending"; // pending, approved, rejected
    
    public string? RequiredApproverRole { get; set; }
    public Guid? RequiredApproverUserId { get; set; }
    
    public string? RejectionReason { get; set; }

    public virtual ICollection<ApprovalHistoryItem> History { get; set; } = new List<ApprovalHistoryItem>();
}
