namespace ERP.Core.DTOs.Shared;

public partial class AuditLogDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string PerformedBy { get; set; } = "system";
    public string Details { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? ClientIpAddress { get; set; }
}
