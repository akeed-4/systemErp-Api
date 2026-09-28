namespace ERP.Core.DTOs.Shared;

public class SupportTicketDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string TicketNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Department { get; set; } = "technical";
    public string Priority { get; set; } = "medium";
    public string Status { get; set; } = "open";
    public string Description { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string? ClientEmail { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public List<SupportTicketReplyDto> Replies { get; set; } = new();
}

public class SupportTicketReplyDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Author { get; set; } = string.Empty;
    public Guid? AuthorUserId { get; set; }
    public bool IsStaff { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>ما تُدخله المنشأة؛ الرقم والحالة والعميل والردود يديرها الخادم.</summary>
public class CreateSupportTicketDto
{
    public string Title { get; set; } = string.Empty;
    public string Department { get; set; } = "technical";
    public string Priority { get; set; } = "medium";
    public string Description { get; set; } = string.Empty;
}

public class UpdateSupportTicketDto : CreateSupportTicketDto
{
}

public class AddSupportTicketReplyDto
{
    public string Message { get; set; } = string.Empty;
}

public class ChangeSupportTicketStatusDto
{
    /// <summary>open | in_progress | resolved | closed</summary>
    public string Status { get; set; } = string.Empty;
}
