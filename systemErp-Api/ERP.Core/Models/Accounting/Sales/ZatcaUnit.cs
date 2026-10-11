namespace ERP.Core.Models.Accounting;

/// <summary>
/// وحدة إصدار فواتير مربوطة مع هيئة الزكاة باسم مستخدم: شهادتها ومفتاحها، وسلسلة فواتيرها (عدّاد ICV وتجزئة آخر
/// فاتورة تصير PIH للتالية). المستخدم المربوط يرسل فواتيره بوحدته؛ <see cref="Version"/> يمنع فاتورتين من أخذ العدّاد نفسه.
/// </summary>
public class ZatcaUnit : BaseEntity
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public ZatcaEnvironment Environment { get; set; } = ZatcaEnvironment.Sandbox;
    /// <summary>الرقم التسلسلي للوحدة في طلب الشهادة؛ يبقى ثابتاً عند إعادة ربط المستخدم نفسه.</summary>
    public string DeviceSerial { get; set; } = string.Empty;
    public string? ComplianceRequestId { get; set; }
    /// <summary>رقم طلب شهادة الإنتاج لدى الهيئة.</summary>
    public string? Csid { get; set; }
    public string BinarySecurityToken { get; set; } = string.Empty;
    public string CertificatePem { get; set; } = string.Empty;
    /// <summary>سرّ الواجهة والمفتاح الخاص محفوظان مشفّرين ولا يخرجان من السرفر.</summary>
    public string SecretKey { get; set; } = string.Empty;
    public string PrivateKeyPem { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime OnboardedAt { get; set; }

    public long Counter { get; set; }
    public string? LastHash { get; set; }
    public int Version { get; set; }
}
