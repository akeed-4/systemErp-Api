namespace ERP.Core.DTOs.Shared;

public class NotifyRequestDto
{
    public Guid RecipientUserId { get; set; }
    public string? RecipientRole { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Type { get; set; } = "system_alert";
    public string? RelatedDocType { get; set; }
    public Guid? RelatedDocId { get; set; }
    public string? RelatedDocNumber { get; set; }
}
