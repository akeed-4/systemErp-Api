using ERP.Core.Contracts.Shared;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// تطبيق Meta الخاص بالمنصة من إعدادات الخادم (قسم WhatsApp). ما يحفظه مدير المنصة من لوحته (PlatformSetting) يغلبه حقلاً حقلاً.
/// </summary>
public class WhatsAppOptions
{
    public const string Section = "WhatsApp";
    public string AppId { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string ConfigId { get; set; } = string.Empty;
    public string VerifyToken { get; set; } = string.Empty;
    /// <summary>إصدار Graph API المثبّت (يُرفع يدوياً بعد مراجعة سجل تغييرات Meta).</summary>
    public string GraphVersion { get; set; } = "v26.0";
    public string GraphBaseUrl { get; set; } = "https://graph.facebook.com";
    /// <summary>العنوان العام للـ API (يصل إليه Meta لإرسال الإشعارات).</summary>
    public string PublicApiUrl { get; set; } = string.Empty;
}

/// <summary>يدمج إعداد مدير المنصة مع إعداد الخادم. يُقرأ عند كل عملية فيسري تغييره فوراً.</summary>
public static class PlatformWhatsApp
{
    public static async Task<(WhatsAppAppCredentials App, List<PlatformSetting> Rows)> ResolveAsync(
        ErpDbContext db, WhatsAppOptions server, ISecretProtector secrets, CancellationToken ct)
    {
        var rows = await db.Catalog.Set<PlatformSetting>().AsNoTracking()
            .Where(s => PlatformSetting.WhatsAppKeys.Contains(s.Key)).ToListAsync(ct);
        string Value(string key, string fallback, bool secret = false)
        {
            var v = rows.FirstOrDefault(r => r.Key == key)?.Value;
            return string.IsNullOrEmpty(v) ? fallback : secret ? secrets.Unprotect(v) : v;
        }
        return (new WhatsAppAppCredentials(
            Value(PlatformSetting.WhatsAppAppId, server.AppId),
            Value(PlatformSetting.WhatsAppAppSecret, server.AppSecret, secret: true),
            Value(PlatformSetting.WhatsAppConfigId, server.ConfigId),
            Value(PlatformSetting.WhatsAppVerifyToken, server.VerifyToken, secret: true),
            Value(PlatformSetting.WhatsAppGraphVersion, server.GraphVersion)), rows);
    }
}
