namespace ERP.Core.DTOs.Shared;

public partial class ZatcaConfigDto
{
    public ZatcaEnvironment Environment { get; set; } = ZatcaEnvironment.Sandbox;
    public ComplianceStatus ComplianceStatus { get; set; } = ComplianceStatus.NotEnrolled;
    public string? Csid { get; set; }
    public string SolutionName { get; set; } = "RayahAccounting";
    public string SolutionVersion { get; set; } = "1.0.0";
    public string? RegisteredDevice { get; set; }
    public bool AutoSendInvoices { get; set; }
    public string? ApiKey { get; set; }
    public string? CertificatePem { get; set; }
    public string? Otp { get; set; }
    public bool IsEnabled { get; set; }
    public string Phase { get; set; } = "phase1";
    public string? TaxRegistrationNumber { get; set; }
    public string? CsrCommonName { get; set; }
    public string? OrganizationUnit { get; set; }
    public string? OrganizationName { get; set; }
    public string CountryCode { get; set; } = "SA";
    public string? DeviceSn { get; set; }
    public string? BusinessCategory { get; set; }
    public bool AutoSubmitOnInvoiceSave { get; set; }
    public string? CustomEndpointUrl { get; set; }
    public DateTime? LastTestDate { get; set; }
    public ZatcaSubmissionStatus? LastTestStatus { get; set; }
    public int? LastTestLatencyMs { get; set; }
    public string? LastTestMessage { get; set; }
}
