
namespace ERP.Core.Models.Shared;

public class ZatcaConfig
{
    public ZatcaEnvironment Environment { get; set; } = ZatcaEnvironment.Sandbox;
    public ComplianceStatus ComplianceStatus { get; set; } = ComplianceStatus.NotEnrolled;
    public string? Csid { get; set; }
    public string? BinarySecurityToken { get; set; }
    public string? SecretKey { get; set; }
    public string SolutionName { get; set; } = "NumoAccounting";
    public string SolutionVersion { get; set; } = "1.0.0";
    public string? RegisteredDevice { get; set; }
    public bool AutoSendInvoices { get; set; }
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }
    public string? CertificatePem { get; set; }
    public string? PrivateKeyPem { get; set; }
    public string? Otp { get; set; }
    public bool IsEnabled { get; set; }
    /// <summary>phase1 | phase2</summary>
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

    // ---------- العنوان الوطني للمنشأة (يظهر في الفاتورة الإلكترونية وفي طلب الشهادة) ----------
    public string? Street { get; set; }
    /// <summary>رقم المبنى (4 أرقام).</summary>
    public string? BuildingNumber { get; set; }
    public string? District { get; set; }
    /// <summary>الرمز البريدي (5 أرقام).</summary>
    public string? PostalCode { get; set; }

    // ---------- نتيجة الربط (المرحلة الثانية) ----------
    /// <summary>رقم طلب شهادة الامتثال الذي أُصدرت منه شهادة الإنتاج.</summary>
    public string? ComplianceRequestId { get; set; }
    /// <summary>متى اكتمل الربط وأُصدرت شهادة الإنتاج.</summary>
    public DateTime? OnboardedAt { get; set; }
}
