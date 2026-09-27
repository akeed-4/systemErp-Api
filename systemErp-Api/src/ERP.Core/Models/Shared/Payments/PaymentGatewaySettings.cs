namespace ERP.Core.Models.Shared;

/// <summary>
/// إعدادات حساب Paymob الخاص بالمنشأة (روابط دفع الفواتير ونقطة البيع). المفاتيح السرية مشفّرة (Data Protection)
/// ولا تُعاد للواجهة أبداً.
/// </summary>
public class PaymentGatewaySettings : BaseEntity
{
    public string Provider { get; set; } = "paymob";
    public bool IsEnabled { get; set; }
    public string BaseUrl { get; set; } = "https://ksa.paymob.com";
    public string PublicKey { get; set; } = string.Empty;
    public string SecretKeyEncrypted { get; set; } = string.Empty;
    public string HmacSecretEncrypted { get; set; } = string.Empty;
    /// <summary>أرقام التكامل (بطاقات/مدى/Apple Pay) مفصولة بفواصل.</summary>
    public string IntegrationIds { get; set; } = string.Empty;
    /// <summary>حساب تحصيلات بوابة الدفع حتى تسوية Paymob للبنك.</summary>
    public string SettlementAccountCode { get; set; } = "1113";
}
