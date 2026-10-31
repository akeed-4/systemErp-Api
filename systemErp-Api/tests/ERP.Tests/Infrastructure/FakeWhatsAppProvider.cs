using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ERP.Core.Contracts.Shared;
using ERP.Service.Services.Shared;
using Microsoft.Extensions.Options;

namespace ERP.Tests.Infrastructure;

/// <summary>
/// بديل Meta في الاختبارات: حسابات وأرقام في الذاكرة، ورموز تُسحب، وقوالب وإرسال مسجَّل، دون أي اتصال بالإنترنت.
/// التحقق من التوقيع وقراءة الإشعار من المزوّد الحقيقي نفسه، فيُختبر كما يعمل في الإنتاج.
/// </summary>
public sealed class FakeWhatsAppProvider : IWhatsAppProvider
{
    public const string AppId = "1000000001";
    public const string AppSecret = "test-app-secret";
    public const string ConfigId = "2000000002";
    public const string VerifyToken = "test-verify-token";
    public const string BadCode = "bad-code";

    public static readonly FakeWhatsAppProvider Instance = new();

    private static long _ids = 7_000_000_000;
    private readonly MetaWhatsAppProvider _meta = new(new NoHttp(), Options.Create(new WhatsAppOptions()));

    /// <summary>الحساب ← أرقامه.</summary>
    public readonly ConcurrentDictionary<string, string[]> Accounts = new();
    /// <summary>بادئات الرموز المسحوبة (الرمز يُشتق من رمز Embedded Signup: token-{code}-...).</summary>
    private readonly ConcurrentDictionary<string, bool> _revoked = new();
    /// <summary>الرقم ← رمز التسجيل الذي سُجِّل به (null = لم يُسجَّل).</summary>
    public readonly ConcurrentDictionary<string, string> RegisteredPins = new();
    public readonly ConcurrentDictionary<string, bool> SubscribedAccounts = new();
    public readonly ConcurrentDictionary<string, ConcurrentDictionary<string, WhatsAppTemplateInfo>> Templates = new();
    /// <summary>تعريفات القوالب كما أُرسلت لـ Meta (الاسم:اللغة).</summary>
    public readonly ConcurrentDictionary<string, WhatsAppTemplateDefinition> Definitions = new();
    public readonly ConcurrentQueue<(string PhoneNumberId, WhatsAppTemplateMessage Message, string MessageId)> Sent = new();
    public readonly ConcurrentDictionary<string, RenderedDocument> Uploaded = new();
    /// <summary>المستلم ← خطأ يُرمى عند الإرسال إليه (كود، ونقل منقطع).</summary>
    public readonly ConcurrentDictionary<string, (string Code, bool Transport)> SendFailures = new();

    public string Name => "meta";

    public static string NewId() => Interlocked.Increment(ref _ids).ToString();

    /// <summary>حساب جديد برقم جديد، ورمز Embedded Signup يُستبدل برمز يخصه.</summary>
    public (string WabaId, string PhoneNumberId, string Code) NewAccount()
    {
        var waba = NewId();
        var phone = NewId();
        Accounts[waba] = new[] { phone };
        return (waba, phone, "code-" + waba);
    }

    /// <summary>يسحب الرمز المستبدَل من رمز Embedded Signup هذا (كما يحدث عند إزالة الصلاحية من Meta).</summary>
    public void RevokeTokensOf(string code) => _revoked["token-" + code + "-"] = true;

    private void Ensure(string token)
    {
        if (_revoked.Keys.Any(token.StartsWith)) throw new WhatsAppProviderException("190", "Error validating access token: Session has expired", isAuth: true);
    }

    public Task<string> ExchangeCodeAsync(WhatsAppAppCredentials app, string code, string? redirectUri = null, CancellationToken ct = default)
    {
        if (code == BadCode || app.AppSecret != AppSecret) throw new WhatsAppProviderException("100", "Invalid verification code format.");
        return Task.FromResult("token-" + code + "-" + NewId());
    }

    /// <summary>الرمز token-code-{waba}-...: الحساب الممنوح هو حساب رمز Embedded Signup.</summary>
    public Task<IReadOnlyList<string>> ListSharedWabaIdsAsync(WhatsAppAppCredentials app, string token, CancellationToken ct = default)
    {
        Ensure(token);
        return Task.FromResult<IReadOnlyList<string>>(Accounts.Keys.Where(w => token.StartsWith("token-code-" + w + "-")).ToList());
    }

    public Task<IReadOnlyList<string>> ListPhoneNumberIdsAsync(WhatsAppAppCredentials app, string token, string wabaId, CancellationToken ct = default)
    {
        Ensure(token);
        return Task.FromResult<IReadOnlyList<string>>(Accounts.TryGetValue(wabaId, out var p) ? p : Array.Empty<string>());
    }

    public Task<WhatsAppPhoneInfo> GetPhoneNumberAsync(WhatsAppAppCredentials app, string token, string phoneNumberId, CancellationToken ct = default)
    {
        Ensure(token);
        return Task.FromResult(new WhatsAppPhoneInfo(phoneNumberId, "+966 50 " + phoneNumberId[^7..], "شركة اختبار", "GREEN", "TIER_250"));
    }

    public Task SubscribeAppAsync(WhatsAppAppCredentials app, string token, string wabaId, CancellationToken ct = default)
    {
        Ensure(token);
        SubscribedAccounts[wabaId] = true;
        return Task.CompletedTask;
    }

    public Task UnsubscribeAppAsync(WhatsAppAppCredentials app, string token, string wabaId, CancellationToken ct = default)
    {
        Ensure(token);
        SubscribedAccounts.TryRemove(wabaId, out _);
        return Task.CompletedTask;
    }

    public Task RegisterPhoneAsync(WhatsAppAppCredentials app, string token, string phoneNumberId, string pin, CancellationToken ct = default)
    {
        Ensure(token);
        // الرقم المسجَّل برمز تحقق لا يُعاد تسجيله برمز آخر (كما في Meta)
        if (RegisteredPins.TryGetValue(phoneNumberId, out var existing) && existing != pin)
            throw new WhatsAppProviderException("133005", "Two step verification PIN Mismatch.");
        RegisteredPins[phoneNumberId] = pin;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WhatsAppTemplateInfo>> ListTemplatesAsync(WhatsAppAppCredentials app, string token, string wabaId, CancellationToken ct = default)
    {
        Ensure(token);
        return Task.FromResult<IReadOnlyList<WhatsAppTemplateInfo>>(Templates.TryGetValue(wabaId, out var t) ? t.Values.ToList() : new List<WhatsAppTemplateInfo>());
    }

    public Task<WhatsAppTemplateInfo> CreateDocumentTemplateAsync(WhatsAppAppCredentials app, string token, string wabaId, WhatsAppTemplateDefinition t, CancellationToken ct = default)
    {
        Ensure(token);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(t.SampleDocument.Content, 0, 4)); // مثال الرأس مستند PDF فعلي
        var info = new WhatsAppTemplateInfo(t.Name, t.Language, "PENDING", "UTILITY");
        Templates.GetOrAdd(wabaId, _ => new())[$"{t.Name}:{t.Language}"] = info;
        Definitions[$"{t.Name}:{t.Language}"] = t;
        return Task.FromResult(info);
    }

    public Task DeleteTemplateAsync(WhatsAppAppCredentials app, string token, string wabaId, string name, CancellationToken ct = default)
    {
        Ensure(token);
        var templates = Templates.GetOrAdd(wabaId, _ => new());
        var keys = templates.Where(p => p.Value.Name == name).Select(p => p.Key).ToList();
        if (keys.Count == 0) throw new WhatsAppProviderException("100", "Template not found.");
        foreach (var key in keys) templates.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public void SetTemplateStatus(string wabaId, string name, string status)
    {
        foreach (var (key, info) in Templates.GetOrAdd(wabaId, _ => new()))
            if (info.Name == name) Templates[wabaId][key] = info with { Status = status };
    }

    public Task<string> UploadMediaAsync(WhatsAppAppCredentials app, string token, string phoneNumberId, RenderedDocument document, CancellationToken ct = default)
    {
        Ensure(token);
        var id = "media-" + NewId();
        Uploaded[id] = document;
        return Task.FromResult(id);
    }

    public Task<string> SendDocumentTemplateAsync(WhatsAppAppCredentials app, string token, string phoneNumberId, WhatsAppTemplateMessage m, CancellationToken ct = default)
    {
        Ensure(token);
        if (SendFailures.TryGetValue(m.To, out var failure))
            throw new WhatsAppProviderException(failure.Code, failure.Transport ? "connection reset" : "Message undeliverable", isTransport: failure.Transport);
        var id = "wamid." + NewId();
        Sent.Enqueue((phoneNumberId, m, id));
        return Task.FromResult(id);
    }

    public bool VerifySignature(WhatsAppAppCredentials app, byte[] body, string? signature) => _meta.VerifySignature(app, body, signature);
    public WhatsAppWebhookBatch ParseWebhook(byte[] body) => _meta.ParseWebhook(body);

    // ---------- إشعارات Meta كما تصل ----------

    public static string Sign(string body, string secret = AppSecret)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return "sha256=" + Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }

    public static string StatusWebhook(string wabaId, string phoneNumberId, string messageId, string status, long timestamp, string? errorCode = null)
    {
        var s = new JsonObject
        {
            ["id"] = messageId, ["status"] = status, ["timestamp"] = timestamp.ToString(), ["recipient_id"] = "966500000000",
            ["pricing"] = new JsonObject { ["billable"] = true, ["pricing_model"] = "PMP", ["category"] = "utility" },
        };
        if (errorCode != null)
            s["errors"] = new JsonArray(new JsonObject { ["code"] = int.Parse(errorCode), ["title"] = "Message undeliverable", ["error_data"] = new JsonObject { ["details"] = "Recipient is not a WhatsApp user" } });
        return Envelope(wabaId, "messages", new JsonObject
        {
            ["messaging_product"] = "whatsapp",
            ["metadata"] = new JsonObject { ["display_phone_number"] = "966500000001", ["phone_number_id"] = phoneNumberId },
            ["statuses"] = new JsonArray(s),
        });
    }

    public static string TemplateWebhook(string wabaId, string name, string language, string status, string reason = "NONE")
        => Envelope(wabaId, "message_template_status_update", new JsonObject
        {
            ["event"] = status, ["message_template_id"] = 1, ["message_template_name"] = name, ["message_template_language"] = language, ["reason"] = reason,
        });

    private static string Envelope(string wabaId, string field, JsonObject value)
        => new JsonObject
        {
            ["object"] = "whatsapp_business_account",
            ["entry"] = new JsonArray(new JsonObject { ["id"] = wabaId, ["changes"] = new JsonArray(new JsonObject { ["field"] = field, ["value"] = value }) }),
        }.ToJsonString();

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("No network in tests.");
    }
}
