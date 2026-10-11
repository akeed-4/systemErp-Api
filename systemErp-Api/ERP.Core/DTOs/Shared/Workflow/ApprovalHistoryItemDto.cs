namespace ERP.Core.DTOs.Shared;

public partial class ApprovalHistoryItemDto
{
    public Guid Id { get; set; }
    public int Level { get; set; }
    public Guid ApproverUserId { get; set; }
    public string ApproverName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public DateTime Timestamp { get; set; }
}
