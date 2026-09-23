using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public class ZatcaConfig
{
    public ZatcaEnvironment Environment { get; set; } = ZatcaEnvironment.Sandbox;
    public ComplianceStatus ComplianceStatus { get; set; } = ComplianceStatus.NotEnrolled;
    public string? Csid { get; set; }
    public string? BinarySecurityToken { get; set; }
    public string? SecretKey { get; set; }
    public string SolutionName { get; set; } = "RayahAccounting";
    public string SolutionVersion { get; set; } = "1.0.0";
    public string? RegisteredDevice { get; set; }
    public bool AutoSendInvoices { get; set; }
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }
    public string? CertificatePem { get; set; }
    public string? PrivateKeyPem { get; set; }
    public string? Otp { get; set; }
    public string? CustomEndpointUrl { get; set; }
    public DateTime? LastTestDate { get; set; }
    public ZatcaSubmissionStatus? LastTestStatus { get; set; }
    public int? LastTestLatencyMs { get; set; }
    public string? LastTestMessage { get; set; }
}
