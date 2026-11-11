namespace ERP.Core.DTOs.Accounting.Sales;

public sealed class ZatcaOnboardingStatusDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public ZatcaEnvironment Environment { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ComplianceRequestId { get; set; }
    public string? ProductionRequestId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<ZatcaComplianceTestDto> Tests { get; set; } = new();
}

public sealed class ZatcaComplianceTestDto
{
    public Guid Id { get; set; }
    public string DocumentKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid Uuid { get; set; }
    public int AttemptCount { get; set; }
    public int? HttpStatus { get; set; }
    public string? ResponseMessage { get; set; }
    public List<string> Errors { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
