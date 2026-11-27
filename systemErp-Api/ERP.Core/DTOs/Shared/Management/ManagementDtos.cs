namespace ERP.Core.DTOs.Shared;

/// <summary>سطر سجل التدقيق في شاشة الإدارة. الوقت بتوقيت السعودية.</summary>
public class AuditLogRowDto
{
    public Guid Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string PerformedBy { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public string Details { get; set; } = string.Empty;
    public string? ClientIpAddress { get; set; }
}
