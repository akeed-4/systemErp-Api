namespace ERP.Core.Models.Shared;

/// <summary>
/// إعداد على مستوى المنصة يغيّره مدير المنصة من لوحته (القاعدة المشتركة وحدها، لا يتبع منشأة).
/// غياب الإعداد = القيمة المضبوطة في الإعدادات (appsettings) كما هي.
/// </summary>
public class PlatformSetting
{
    public const string DefaultDatabaseMode = "DefaultDatabaseMode";

    // حساب Paymob الخاص بالمنصة (دفع الاشتراكات)؛ المفتاحان السريان مشفّران بـ ISecretProtector
    public const string PaymobBaseUrl = "Paymob.BaseUrl";
    public const string PaymobPublicKey = "Paymob.PublicKey";
    public const string PaymobSecretKey = "Paymob.SecretKey";
    public const string PaymobHmacSecret = "Paymob.HmacSecret";
    public const string PaymobIntegrationIds = "Paymob.IntegrationIds";
    public static readonly string[] PaymobKeys = { PaymobBaseUrl, PaymobPublicKey, PaymobSecretKey, PaymobHmacSecret, PaymobIntegrationIds };

    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }
}
