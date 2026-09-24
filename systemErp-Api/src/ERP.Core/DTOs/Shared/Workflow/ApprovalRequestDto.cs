namespace ERP.Core.DTOs.Shared;

public partial class ApprovalRequestDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
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
    public string Status { get; set; } = "pending";
    public string? RequiredApproverRole { get; set; }
    public Guid? RequiredApproverUserId { get; set; }
    public string? RejectionReason { get; set; }
    public List<ApprovalHistoryItemDto> History { get; set; } = new();
}
