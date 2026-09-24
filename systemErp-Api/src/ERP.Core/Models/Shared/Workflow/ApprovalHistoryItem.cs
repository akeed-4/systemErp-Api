
namespace ERP.Core.Models.Shared;

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
