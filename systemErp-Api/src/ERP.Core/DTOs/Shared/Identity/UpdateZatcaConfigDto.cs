namespace ERP.Core.DTOs.Shared;

/// <summary>تحديث إعدادات الربط مع هيئة الزكاة (تشمل الأسرار - لا تُعاد في القراءة أبداً).</summary>
public class UpdateZatcaConfigDto
{
    public bool IsEnabled { get; set; }
    public ZatcaEnvironment Environment { get; set; } = ZatcaEnvironment.Sandbox;
    public string? Csid { get; set; }
    public string? BinarySecurityToken { get; set; }
    public string? SecretKey { get; set; }
    public string? SolutionName { get; set; }
    public string? SolutionVersion { get; set; }
    public string? RegisteredDevice { get; set; }
    public bool AutoSendInvoices { get; set; }
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }
    public string? CertificatePem { get; set; }
    public string? PrivateKeyPem { get; set; }
    public string? Otp { get; set; }
    public string? CustomEndpointUrl { get; set; }
    public string? TaxRegistrationNumber { get; set; }
    public string? CsrCommonName { get; set; }
    public string? OrganizationUnit { get; set; }
    public string? OrganizationName { get; set; }
    public string? CountryCode { get; set; }
    public string? DeviceSn { get; set; }
    public string? BusinessCategory { get; set; }
    public bool AutoSubmitOnInvoiceSave { get; set; }
    public string? Phase { get; set; }
}
