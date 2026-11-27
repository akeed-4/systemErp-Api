using System.Text.RegularExpressions;
using ERP.Core.Contracts.Shared;
using ERP.Core.Models.Shared;
using ERP.Service.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ERP.Service.Services.Shared;

/// <summary>
/// يكتب أحداث المراقبة في سياق مستقل قصير العمر (بلا اعتراض تدقيق ولا نسخ دليل)، خارج معاملة العملية الأصلية:
/// فشل الكتابة لا يتراجع بالعملية ولا يرمي إليها؛ يُسجَّل في سجل الخادم فقط.
/// - الدخول وأخطاء الخادم: القاعدة المشتركة (على مستوى المنصة).
/// - محاولات الهيئة: قاعدة المنشأة الحالية (المشتركة بمرشّح المنشأة، أو المستقلة).
/// </summary>
public sealed class MonitoringRecorder : IMonitoringRecorder
{
    private readonly ITenantConnectionResolver _resolver;
    private readonly ITenantContext _tenant;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<MonitoringRecorder> _log;

    public MonitoringRecorder(ITenantConnectionResolver resolver, ITenantContext tenant, IHttpContextAccessor http, ILogger<MonitoringRecorder> log)
    {
        _resolver = resolver; _tenant = tenant; _http = http; _log = log;
    }

    public Task RecordLoginAsync(LoginEvent entry, CancellationToken ct = default)
    {
        entry.Id = Guid.NewGuid();
        if (entry.OccurredAt == default) entry.OccurredAt = DateTime.UtcNow;
        entry.Identifier = MonitoringText.Clip(entry.Identifier, 128) ?? string.Empty;
        var request = _http.HttpContext;
        entry.IpAddress ??= MonitoringText.Clip(request?.Connection.RemoteIpAddress?.ToString(), 64);
        entry.UserAgent ??= MonitoringText.Clip(request?.Request.Headers.UserAgent.ToString(), 256);
        return SaveAsync(_resolver.CatalogConnectionString, db => db.Set<LoginEvent>().Add(entry), "login event");
    }

    public Task RecordApiErrorAsync(ApiErrorLog entry, CancellationToken ct = default)
    {
        entry.Id = Guid.NewGuid();
        if (entry.OccurredAt == default) entry.OccurredAt = DateTime.UtcNow;
        entry.TraceId = MonitoringText.Clip(entry.TraceId, 64) ?? string.Empty;
        entry.Method = MonitoringText.Clip(entry.Method, 10) ?? string.Empty;
        entry.Path = MonitoringText.Clip(entry.Path, 256) ?? string.Empty;
        entry.ExceptionType = MonitoringText.Clip(entry.ExceptionType, 256) ?? string.Empty;
        entry.Source = MonitoringText.Clip(entry.Source, 256);
        entry.Message = MonitoringText.Clip(MonitoringText.Scrub(entry.Message), 500);
        return SaveAsync(_resolver.CatalogConnectionString, db => db.Set<ApiErrorLog>().Add(entry), "API error");
    }

    public async Task RecordZatcaAttemptAsync(ZatcaSubmissionAttempt entry, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not { } tenantId || tenantId == Guid.Empty) return; // محاولة بلا منشأة لا معنى لها
        entry.InvoiceNumber = MonitoringText.Clip(entry.InvoiceNumber, 64) ?? string.Empty;
        entry.DocumentKind = MonitoringText.Clip(entry.DocumentKind, 32) ?? string.Empty;
        entry.UserName = MonitoringText.Clip(entry.UserName, 256);
        entry.IssueCodes = MonitoringText.Clip(entry.IssueCodes, 500);
        entry.Message = MonitoringText.Clip(MonitoringText.Scrub(entry.Message), 1000);
        try
        {
            var target = await _resolver.ResolveAsync(tenantId, CancellationToken.None);
            await SaveAsync(target.ConnectionString, db => db.Set<ZatcaSubmissionAttempt>().Add(entry), "ZATCA attempt");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Monitoring: could not resolve the database to record a ZATCA attempt for tenant {TenantId}", tenantId);
        }
    }

    /// <summary>
    /// سياق جديد على القاعدة المطلوبة، بلا اعتراضات وبلا رمز إلغاء الطلب (انقطاع المتصفح لا يُسقط سجلاً عن عملية تمّت).
    /// </summary>
    private async Task SaveAsync(string connectionString, Action<ErpDbContext> add, string what)
    {
        try
        {
            await using var db = new ErpDbContext(_resolver.OptionsFor(connectionString), _tenant);
            add(db);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Monitoring: could not record {What}", what);
        }
    }
}

/// <summary>نصوص المراقبة الآمنة: قص الطول، إخفاء أنماط الأسرار، وتقنيع معرّف الدخول.</summary>
public static partial class MonitoringText
{
    public static string? Clip(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        return text.Length <= max ? text : text[..max];
    }

    /// <summary>يخفي قيم «كلمة/مفتاح=قيمة» الشائعة (نصوص الاتصال، الرموز) والرموز الطويلة الشبيهة بالمفاتيح.</summary>
    public static string? Scrub(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var text = SecretPairs().Replace(value, m => m.Groups[1].Value + "=***");
        return LongTokens().Replace(text, "***");
    }

    /// <summary>البريد: أول حرفين ثم *** والنطاق؛ الجوال: آخر 3 أرقام. يكفي للمتابعة ولا يكشف الحساب.</summary>
    public static string MaskIdentifier(string? identifier)
    {
        var text = (identifier ?? string.Empty).Trim();
        if (text.Length == 0) return "—";
        var at = text.IndexOf('@');
        if (at > 0) return (at <= 2 ? text[..1] : text[..2]) + "***" + text[at..];
        var digits = new string(text.Where(char.IsDigit).ToArray());
        return digits.Length >= 3 ? "***" + digits[^3..] : "***";
    }

    [GeneratedRegex(@"(?i)\b(password|pwd|secret|token|apikey|api_key|key|otp|authorization|bearer)\s*[=:]\s*[^;,\s]+")]
    private static partial Regex SecretPairs();

    [GeneratedRegex(@"[A-Za-z0-9+/=_\-]{40,}")]
    private static partial Regex LongTokens();
}
