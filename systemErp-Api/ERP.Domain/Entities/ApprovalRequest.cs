using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class ApprovalRequest : BaseEntity
{
    public Guid PolicyId { get; set; }
    public string PolicyNameAr { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public Guid DocumentId { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public decimal DocumentAmount { get; set; }
    
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

public class ApprovalHistoryItem : BaseEntity
{
    public Guid ApprovalRequestId { get; set; }
    public int Level { get; set; }
    public Guid ApproverUserId { get; set; }
    public string ApproverName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // approved, rejected
    public string? Comment { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public virtual ApprovalRequest ApprovalRequest { get; set; } = null!;
}
