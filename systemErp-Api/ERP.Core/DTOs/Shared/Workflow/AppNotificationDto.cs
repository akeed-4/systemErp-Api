namespace ERP.Core.DTOs.Shared;

public partial class AppNotificationDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid RecipientUserId { get; set; }
    public string? RecipientRole { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Type { get; set; } = "system_alert";
    public string? RelatedDocType { get; set; }
    public Guid? RelatedDocId { get; set; }
    public string? RelatedDocNumber { get; set; }
    public Guid? ApprovalRequestId { get; set; }
    public bool IsRead { get; set; }
}
