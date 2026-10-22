namespace ERP.Core.Models.Shared;

/// <summary>
/// إعداد على مستوى المنصة يغيّره مدير المنصة من لوحته (القاعدة المشتركة وحدها، لا يتبع منشأة).
/// غياب الإعداد = القيمة المضبوطة في الإعدادات (appsettings) كما هي.
/// </summary>
public class PlatformSetting
{
    public const string DefaultDatabaseMode = "DefaultDatabaseMode";

    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }
}
