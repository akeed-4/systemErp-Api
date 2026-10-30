using System.Text.RegularExpressions;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Service.Data;
using Microsoft.Extensions.Options;

namespace ERP.Service.Services.Shared;

/// <summary>إعداد تطبيق Meta الخاص بالمنصة (الدليل وحده). السرّ ورمز التحقق يُحفظان مشفّرين ولا يُعادان.</summary>
public class WhatsAppPlatformSettingsService : IWhatsAppPlatformSettingsService
{
    private static readonly Regex GraphId = new(@"^\d{1,32}$");
    private static readonly Regex GraphVersion = new(@"^v\d{1,3}\.\d$");

    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;
    private readonly ISecretProtector _secrets;
    private readonly WhatsAppOptions _server;

    public WhatsAppPlatformSettingsService(ErpDbContext db, ICurrentUser user, IAuditService audit, ISecretProtector secrets, IOptions<WhatsAppOptions> server)
    {
        _db = db; _user = user; _audit = audit; _secrets = secrets; _server = server.Value;
    }

    public async Task<PlatformWhatsAppSettingsDto> GetAsync(CancellationToken ct = default)
    {
        var (app, rows) = await PlatformWhatsApp.ResolveAsync(_db, _server, _secrets, ct);
        var last = rows.OrderByDescending(r => r.UpdatedAt).FirstOrDefault();
        return new PlatformWhatsAppSettingsDto
        {
            AppId = app.AppId, ConfigId = app.ConfigId, GraphVersion = app.GraphVersion,
            HasAppSecret = !string.IsNullOrEmpty(app.AppSecret), HasVerifyToken = !string.IsNullOrEmpty(app.VerifyToken),
            IsConfigured = app.IsConfigured,
            WebhookUrl = string.IsNullOrWhiteSpace(_server.PublicApiUrl) ? string.Empty : $"{_server.PublicApiUrl.TrimEnd('/')}/api/v1/whatsapp/webhook",
            IsCustomized = last != null, UpdatedAt = last?.UpdatedAt, UpdatedBy = last?.UpdatedBy,
        };
    }

    public async Task<PlatformWhatsAppSettingsDto> UpdateAsync(UpdatePlatformWhatsAppSettingsDto r, CancellationToken ct = default)
    {
        var appId = r.AppId?.Trim() ?? string.Empty;
        var configId = r.ConfigId?.Trim() ?? string.Empty;
        var version = r.GraphVersion?.Trim() ?? string.Empty;
        if (!GraphId.IsMatch(appId) || !GraphId.IsMatch(configId) || !GraphVersion.IsMatch(version))
            throw new ValidationFailedException(Messages.WhatsAppPlatformSettingsInvalid);

        var catalog = _db.Catalog;
        var rows = await catalog.Set<PlatformSetting>().Where(s => PlatformSetting.WhatsAppKeys.Contains(s.Key)).ToListAsync(ct);
        var now = DateTime.UtcNow;
        void Set(string key, string value)
        {
            var row = rows.FirstOrDefault(s => s.Key == key);
            if (row == null) { catalog.Add(row = new PlatformSetting { Key = key }); rows.Add(row); }
            row.Value = value;
            row.UpdatedAt = now;
            row.UpdatedBy = _user.Email;
        }
        Set(PlatformSetting.WhatsAppAppId, appId);
        Set(PlatformSetting.WhatsAppConfigId, configId);
        Set(PlatformSetting.WhatsAppGraphVersion, version);
        // الحقل الفارغ يُبقي المحفوظ (أو قيمة الخادم إن لم يُحفظ شيء بعد)
        if (!string.IsNullOrWhiteSpace(r.AppSecret)) Set(PlatformSetting.WhatsAppAppSecret, _secrets.Protect(r.AppSecret.Trim()));
        if (!string.IsNullOrWhiteSpace(r.VerifyToken)) Set(PlatformSetting.WhatsAppVerifyToken, _secrets.Protect(r.VerifyToken.Trim()));
        await catalog.SaveChangesAsync(ct);

        var changed = string.Join(", ", new[] { "AppId", "ConfigId", "GraphVersion" }
            .Concat(string.IsNullOrWhiteSpace(r.AppSecret) ? Array.Empty<string>() : new[] { "AppSecret" })
            .Concat(string.IsNullOrWhiteSpace(r.VerifyToken) ? Array.Empty<string>() : new[] { "VerifyToken" }));
        await _audit.LogAsync("PLATFORM_WHATSAPP_SETTINGS_CHANGED", nameof(PlatformSetting), "WhatsApp", changed, ct);
        return await GetAsync(ct);
    }
}
