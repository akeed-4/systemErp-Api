namespace ERP.Core.Models.Accounting;

/// <summary>Durable, resumable state for one user's ZATCA compliance onboarding attempt.</summary>
public class ZatcaOnboardingSession : BaseEntity
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public ZatcaEnvironment Environment { get; set; } = ZatcaEnvironment.Sandbox;
    public string DeviceSerial { get; set; } = string.Empty;
    public string InvoiceTypes { get; set; } = "1100";
    public string Csr { get; set; } = string.Empty;
    public string Status { get; set; } = "preparing";
    public string? ComplianceRequestId { get; set; }
    public string? ComplianceBinarySecurityToken { get; set; }
    public string? ComplianceSecret { get; set; }
    public string? CertificatePem { get; set; }
    public string? PrivateKeyPem { get; set; }
    public string? ProductionRequestId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
