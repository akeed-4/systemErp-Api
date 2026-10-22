using ERP.Core.Contracts.Shared;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// حساب Paymob الخاص بالمنصة (دفع الاشتراكات): ما حفظه مدير المنصة من لوحته (PlatformSetting في الدليل) يغلب
/// إعدادات الخادم (قسم Paymob) حقلاً حقلاً. يُقرأ عند كل عملية، فتغييره يسري فوراً. روابط الإشعار والعودة من الخادم دائماً.
/// </summary>
public static class PlatformPaymob
{
    public static async Task<(PaymobOptions Options, List<PlatformSetting> Rows)> ResolveAsync(
        ErpDbContext db, PaymobOptions server, ISecretProtector secrets, CancellationToken ct)
    {
        var rows = await db.Catalog.Set<PlatformSetting>().AsNoTracking()
            .Where(s => PlatformSetting.PaymobKeys.Contains(s.Key)).ToListAsync(ct);
        string Value(string key, string fallback, bool secret = false)
        {
            var v = rows.FirstOrDefault(r => r.Key == key)?.Value;
            return string.IsNullOrEmpty(v) ? fallback : secret ? secrets.Unprotect(v) : v;
        }
        return (new PaymobOptions
        {
            BaseUrl = Value(PlatformSetting.PaymobBaseUrl, server.BaseUrl),
            PublicKey = Value(PlatformSetting.PaymobPublicKey, server.PublicKey),
            SecretKey = Value(PlatformSetting.PaymobSecretKey, server.SecretKey, secret: true),
            HmacSecret = Value(PlatformSetting.PaymobHmacSecret, server.HmacSecret, secret: true),
            IntegrationIds = Value(PlatformSetting.PaymobIntegrationIds, server.IntegrationIds),
            PublicApiUrl = server.PublicApiUrl,
            FrontendUrl = server.FrontendUrl,
        }, rows);
    }
}
