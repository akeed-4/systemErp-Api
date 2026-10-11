namespace ERP.Core.Models.Shared;

/// <summary>تذكرة دعم فني تفتحها المنشأة، مع سلسلة الردود.</summary>
public class SupportTicket : BaseEntity
{
    public string TicketNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    /// <summary>accounting | inventory | zatca | showroom | technical</summary>
    public string Department { get; set; } = "technical";
    /// <summary>low | medium | high | urgent</summary>
    public string Priority { get; set; } = "medium";
    /// <summary>open | in_progress | resolved | closed</summary>
    public string Status { get; set; } = "open";
    public string Description { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string? ClientEmail { get; set; }
    public Guid? CreatedByUserId { get; set; }

    public virtual ICollection<SupportTicketReply> Replies { get; set; } = new List<SupportTicketReply>();
}
