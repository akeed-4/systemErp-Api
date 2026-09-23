using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class AppNotification : BaseEntity
{
    public Guid RecipientUserId { get; set; }
    public string? RecipientRole { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Type { get; set; } = "system_alert"; // approval_request, approval_approved, approval_rejected, system_alert
    
    public string? RelatedDocType { get; set; }
    public Guid? RelatedDocId { get; set; }
    public string? RelatedDocNumber { get; set; }
    public Guid? ApprovalRequestId { get; set; }
    public bool IsRead { get; set; }
}
