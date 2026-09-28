namespace ERP.Core.Models.Shared;

public class SupportTicketReply : BaseEntity
{
    public Guid SupportTicketId { get; set; }
    public string Author { get; set; } = string.Empty;
    public Guid? AuthorUserId { get; set; }
    /// <summary>رد من فريق الدعم (أو الرد الآلي) لا من المنشأة.</summary>
    public bool IsStaff { get; set; }
    public string Message { get; set; } = string.Empty;

    public virtual SupportTicket SupportTicket { get; set; } = null!;
}
