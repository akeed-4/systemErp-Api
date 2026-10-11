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
    public string? Street { get; set; }
    public string? BuildingNumber { get; set; }
    public string? District { get; set; }
    public string? PostalCode { get; set; }
}

/// <summary>طلب الربط مع الهيئة: رمز التحقق (OTP) الذي تصدره بوابة «فاتورة» لوحدة إصدار الفواتير.</summary>
public class ZatcaOnboardRequestDto
{
    public string Otp { get; set; } = string.Empty;
    /// <summary>المستخدم الذي تُربط الوحدة باسمه؛ فارغ = صاحب الجلسة. ربط مستخدم آخر للمالك ومدير النظام فقط.</summary>
    public Guid? UserId { get; set; }
}
