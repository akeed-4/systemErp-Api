using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ERP.Core.Contracts.Shared;
using Microsoft.Extensions.Options;

namespace ERP.Service.Services.Shared;

/// <summary>
/// WhatsApp Cloud API عبر Graph API (Meta). كل الطلبات من الخادم إلى الخادم برمز المنشأة (Business Integration System User)
/// المستبدَل من رمز Embedded Signup. الإصدار من إعداد المنصة. أخطاء Graph ({ error: { code, message, error_data.details } })
/// تُرمى <see cref="WhatsAppProviderException"/>، والكودان 190 و0 رمز مرفوض.
/// </summary>
public class MetaWhatsAppProvider : IWhatsAppProvider
{
    public const string HttpClientName = "whatsapp";
    private readonly IHttpClientFactory _http;
    private readonly WhatsAppOptions _options;

    public MetaWhatsAppProvider(IHttpClientFactory http, IOptions<WhatsAppOptions> options)
    {
        _http = http; _options = options.Value;
    }

    public string Name => "meta";

    private string Url(WhatsAppAppCredentials app, string path) => $"{_options.GraphBaseUrl.TrimEnd('/')}/{app.GraphVersion}/{path.TrimStart('/')}";

    public async Task<string> ExchangeCodeAsync(WhatsAppAppCredentials app, string code, string? redirectUri = null, CancellationToken ct = default)
    {
        var url = Url(app, "oauth/access_token")
            + $"?client_id={Uri.EscapeDataString(app.AppId)}&client_secret={Uri.EscapeDataString(app.AppSecret)}&code={Uri.EscapeDataString(code)}";
        // الرمز مربوط بـ redirect_uri نافذة Meta: صفحة الرجوع التي ترسلها الواجهة كما هي أولاً، ثم بلا عنوان أو بعنوان فارغ
        // (رموز قديمة من FB.login)؛ يُنتقل للتالي ما دامت Meta ترفض لعدم تطابقه وحده
        var candidates = new List<string?>();
        if (Uri.TryCreate(redirectUri, UriKind.Absolute, out var page) && page.Scheme == Uri.UriSchemeHttps) candidates.Add(redirectUri);
        candidates.AddRange(new[] { null, string.Empty });
        for (var i = 0; ; i++)
        {
            var attempt = candidates[i] is { } uri ? $"{url}&redirect_uri={Uri.EscapeDataString(uri)}" : url;
            try
            {
                using var root = await SendAsync(new HttpRequestMessage(HttpMethod.Get, attempt), null, ct);
                return Str(root.RootElement, "access_token") is { Length: > 0 } token ? token
                    : throw new WhatsAppProviderException("no_token", "Meta did not return an access token.");
            }
            catch (WhatsAppProviderException e) when (i < candidates.Count - 1 && e.Message.Contains("redirect_uri", StringComparison.OrdinalIgnoreCase)) { }
        }
    }

    public async Task<IReadOnlyList<string>> ListSharedWabaIdsAsync(WhatsAppAppCredentials app, string token, CancellationToken ct = default)
    {
        // رمز التطبيق (AppId|AppSecret) يقرأ صلاحيات رمز العميل؛ الحسابات الممنوحة هي target_ids لصلاحيات واتساب
        var url = Url(app, "debug_token")
            + $"?input_token={Uri.EscapeDataString(token)}&access_token={Uri.EscapeDataString(app.AppId + "|" + app.AppSecret)}";
        var found = new List<string>();
        using (var root = await SendAsync(new HttpRequestMessage(HttpMethod.Get, url), null, ct))
        {
            if (root.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("granular_scopes", out var scopes) && scopes.ValueKind == JsonValueKind.Array)
                found.AddRange(scopes.EnumerateArray()
                    .Where(s => Str(s, "scope") is "whatsapp_business_management" or "whatsapp_business_messaging")
                    .SelectMany(s => s.TryGetProperty("target_ids", out var ids) && ids.ValueKind == JsonValueKind.Array ? ids.EnumerateArray() : Enumerable.Empty<JsonElement>())
                    .Select(id => id.ValueKind == JsonValueKind.String ? id.GetString() ?? string.Empty : id.GetRawText()));
        }
        // صلاحية بلا target_ids (منح شامل): حسابات الأنشطة التي يصل إليها الرمز، المملوكة ثم حسابات العملاء
        if (found.Count == 0)
        {
            using var businesses = await SendAsync(new HttpRequestMessage(HttpMethod.Get, Url(app, "me/businesses?fields=id&limit=50")), token, ct);
            foreach (var business in Data(businesses.RootElement).Select(b => Str(b, "id")).Where(id => id.Length > 0))
                foreach (var edge in new[] { "owned_whatsapp_business_accounts", "client_whatsapp_business_accounts" })
                {
                    try
                    {
                        using var accounts = await SendAsync(new HttpRequestMessage(HttpMethod.Get, Url(app, $"{business}/{edge}?fields=id&limit=50")), token, ct);
                        found.AddRange(Data(accounts.RootElement).Select(a => Str(a, "id")));
                    }
                    catch (WhatsAppProviderException e) when (!e.IsAuth) { } // صلاحية ناقصة على أحد النوعين لا تُفشل الآخر
                }
        }
        return found.Where(id => id.Length > 0).Distinct().ToList();
    }

    public async Task<IReadOnlyList<string>> ListPhoneNumberIdsAsync(WhatsAppAppCredentials app, string token, string wabaId, CancellationToken ct = default)
    {
        using var root = await SendAsync(new HttpRequestMessage(HttpMethod.Get, Url(app, $"{wabaId}/phone_numbers?fields=id&limit=100")), token, ct);
        return Data(root.RootElement).Select(p => Str(p, "id")).Where(id => id.Length > 0).ToList();
    }

    public async Task<WhatsAppPhoneInfo> GetPhoneNumberAsync(WhatsAppAppCredentials app, string token, string phoneNumberId, CancellationToken ct = default)
    {
        using var root = await SendAsync(new HttpRequestMessage(HttpMethod.Get,
            Url(app, $"{phoneNumberId}?fields=id,display_phone_number,verified_name,quality_rating")), token, ct);
        var p = root.RootElement;
        // الحد اليومي حقل مستقل (الحقل القديم messaging_limit_tier متقادم)؛ غيابه لا يُفشل الربط
        string? limit = null;
        try
        {
            using var l = await SendAsync(new HttpRequestMessage(HttpMethod.Get,
                Url(app, $"{phoneNumberId}?fields=whatsapp_business_manager_messaging_limit")), token, ct);
            if (l.RootElement.TryGetProperty("whatsapp_business_manager_messaging_limit", out var v))
                limit = v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();
        }
        catch (WhatsAppProviderException e) when (!e.IsAuth) { }
        return new WhatsAppPhoneInfo(Str(p, "id"), Str(p, "display_phone_number"), Str(p, "verified_name"), Str(p, "quality_rating") is { Length: > 0 } q ? q : null, limit);
    }

    public async Task SubscribeAppAsync(WhatsAppAppCredentials app, string token, string wabaId, CancellationToken ct = default)
        => (await SendAsync(new HttpRequestMessage(HttpMethod.Post, Url(app, $"{wabaId}/subscribed_apps")), token, ct)).Dispose();

    public async Task UnsubscribeAppAsync(WhatsAppAppCredentials app, string token, string wabaId, CancellationToken ct = default)
        => (await SendAsync(new HttpRequestMessage(HttpMethod.Delete, Url(app, $"{wabaId}/subscribed_apps")), token, ct)).Dispose();

    public async Task RegisterPhoneAsync(WhatsAppAppCredentials app, string token, string phoneNumberId, string pin, CancellationToken ct = default)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, Url(app, $"{phoneNumberId}/register"))
        {
            Content = JsonContent.Create(new { messaging_product = "whatsapp", pin }),
        };
        (await SendAsync(req, token, ct)).Dispose();
    }

    public async Task<IReadOnlyList<WhatsAppTemplateInfo>> ListTemplatesAsync(WhatsAppAppCredentials app, string token, string wabaId, CancellationToken ct = default)
    {
        using var root = await SendAsync(new HttpRequestMessage(HttpMethod.Get,
            Url(app, $"{wabaId}/message_templates?fields=name,language,status,category&limit=200")), token, ct);
        return Data(root.RootElement).Select(t => new WhatsAppTemplateInfo(Str(t, "name"), Str(t, "language"), Str(t, "status"), Str(t, "category"))).ToList();
    }

    public async Task<WhatsAppTemplateInfo> CreateDocumentTemplateAsync(WhatsAppAppCredentials app, string token, string wabaId,
        WhatsAppTemplateDefinition t, CancellationToken ct = default)
    {
        // رأس الوسائط في القالب يلزمه مثال يُرفع بواجهة الرفع القابل للاستئناف (Resumable Upload) فيعود مقبضه (h)
        var handle = await UploadExampleAsync(app, token, t.SampleDocument, ct);
        var components = new List<object>
        {
            new { type = "HEADER", format = "DOCUMENT", example = new { header_handle = new[] { handle } } },
            // نص بلا متغيرات لا يُرفق به مثال (Meta يرفض المثال الفارغ)
            t.BodyExamples.Count > 0
                ? (object)new { type = "BODY", text = t.BodyText, example = new { body_text = new[] { t.BodyExamples.ToArray() } } }
                : new { type = "BODY", text = t.BodyText },
        };
        if (!string.IsNullOrWhiteSpace(t.Footer)) components.Add(new { type = "FOOTER", text = t.Footer });
        var req = new HttpRequestMessage(HttpMethod.Post, Url(app, $"{wabaId}/message_templates"))
        {
            Content = JsonContent.Create(new { name = t.Name, language = t.Language, category = "UTILITY", components }),
        };
        using var root = await SendAsync(req, token, ct);
        return new WhatsAppTemplateInfo(t.Name, t.Language, Str(root.RootElement, "status") is { Length: > 0 } s ? s : "PENDING", Str(root.RootElement, "category"));
    }

    public async Task DeleteTemplateAsync(WhatsAppAppCredentials app, string token, string wabaId, string name, CancellationToken ct = default)
        => (await SendAsync(new HttpRequestMessage(HttpMethod.Delete, Url(app, $"{wabaId}/message_templates?name={Uri.EscapeDataString(name)}")), token, ct)).Dispose();

    private async Task<string> UploadExampleAsync(WhatsAppAppCredentials app, string token, RenderedDocument doc, CancellationToken ct)
    {
        var start = Url(app, $"{app.AppId}/uploads")
            + $"?file_name={Uri.EscapeDataString(doc.FileName)}&file_length={doc.Content.Length}&file_type={Uri.EscapeDataString(doc.ContentType)}";
        string session;
        using (var root = await SendAsync(new HttpRequestMessage(HttpMethod.Post, start), token, ct))
            session = Str(root.RootElement, "id");
        if (session.Length == 0) throw new WhatsAppProviderException("upload", "Meta did not return an upload session.");

        var req = new HttpRequestMessage(HttpMethod.Post, Url(app, session)) { Content = new ByteArrayContent(doc.Content) };
        req.Headers.TryAddWithoutValidation("file_offset", "0");
        // هذه الواجهة تطلب ترويسة OAuth لا Bearer
        req.Headers.Authorization = new AuthenticationHeaderValue("OAuth", token);
        using var uploaded = await SendAsync(req, null, ct);
        return Str(uploaded.RootElement, "h") is { Length: > 0 } h ? h : throw new WhatsAppProviderException("upload", "Meta did not return an upload handle.");
    }

    public async Task<string> UploadMediaAsync(WhatsAppAppCredentials app, string token, string phoneNumberId, RenderedDocument doc, CancellationToken ct = default)
    {
        var file = new ByteArrayContent(doc.Content);
        file.Headers.ContentType = new MediaTypeHeaderValue(doc.ContentType);
        var form = new MultipartFormDataContent
        {
            { new StringContent("whatsapp"), "messaging_product" },
            { new StringContent(doc.ContentType), "type" },
            { file, "file", doc.FileName },
        };
        using var root = await SendAsync(new HttpRequestMessage(HttpMethod.Post, Url(app, $"{phoneNumberId}/media")) { Content = form }, token, ct);
        return Str(root.RootElement, "id") is { Length: > 0 } id ? id : throw new WhatsAppProviderException("media", "Meta did not return a media id.");
    }

    public async Task<string> SendDocumentTemplateAsync(WhatsAppAppCredentials app, string token, string phoneNumberId, WhatsAppTemplateMessage m, CancellationToken ct = default)
    {
        // قالب بلا رأس مستند (hello_world) لا يُرسل له رأس، والنص بلا متغيرات لا يُرسل له مكوّن
        var components = new List<object>();
        if (m.MediaId.Length > 0)
            components.Add(new { type = "header", parameters = new object[] { new { type = "document", document = new { id = m.MediaId, filename = m.FileName } } } });
        if (m.BodyParameters.Count > 0)
            components.Add(new { type = "body", parameters = m.BodyParameters.Select(p => (object)new { type = "text", text = p }).ToArray() });
        var req = new HttpRequestMessage(HttpMethod.Post, Url(app, $"{phoneNumberId}/messages"))
        {
            Content = JsonContent.Create(new
            {
                messaging_product = "whatsapp",
                recipient_type = "individual",
                to = m.To,
                type = "template",
                template = new
                {
                    name = m.TemplateName,
                    language = new { code = m.Language },
                    components,
                },
            }),
        };
        using var root = await SendAsync(req, token, ct);
        var id = root.RootElement.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Array && msgs.GetArrayLength() > 0
            ? Str(msgs[0], "id") : string.Empty;
        return id.Length > 0 ? id : throw new WhatsAppProviderException("no_message_id", "Meta did not return a message id.");
    }

    // ---------- الإشعارات ----------

    /// <summary>X-Hub-Signature-256: sha256=HMAC-SHA256(جسم الطلب الخام، سرّ التطبيق).</summary>
    public bool VerifySignature(WhatsAppAppCredentials app, byte[] body, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrEmpty(app.AppSecret)) return false;
        var given = signature.Trim();
        if (!given.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)) return false;
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(app.AppSecret));
        var expected = Encoding.ASCII.GetBytes(Convert.ToHexString(h.ComputeHash(body)).ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(given[7..].ToLowerInvariant()));
    }

    public WhatsAppWebhookBatch ParseWebhook(byte[] body)
    {
        var statuses = new List<WhatsAppStatusEvent>();
        var templates = new List<WhatsAppTemplateEvent>();
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return new WhatsAppWebhookBatch(statuses, templates);

        foreach (var entry in entries.EnumerateArray())
        {
            var wabaId = Str(entry, "id");
            if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array) continue;
            foreach (var change in changes.EnumerateArray())
            {
                if (!change.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Object) continue;
                switch (Str(change, "field"))
                {
                    case "messages":
                        var phoneId = value.TryGetProperty("metadata", out var meta) ? Str(meta, "phone_number_id") : string.Empty;
                        if (!value.TryGetProperty("statuses", out var list) || list.ValueKind != JsonValueKind.Array) break;
                        foreach (var s in list.EnumerateArray())
                        {
                            string? code = null, message = null;
                            if (s.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
                            {
                                var e = errors[0];
                                code = e.TryGetProperty("code", out var c) ? c.ToString() : null;
                                var details = e.TryGetProperty("error_data", out var data) ? Str(data, "details") : string.Empty;
                                message = new[] { details, Str(e, "message"), Str(e, "title") }.FirstOrDefault(x => x.Length > 0);
                            }
                            string? category = null; bool? billable = null;
                            if (s.TryGetProperty("pricing", out var pricing) && pricing.ValueKind == JsonValueKind.Object)
                            {
                                category = Str(pricing, "category") is { Length: > 0 } cat ? cat : null;
                                if (pricing.TryGetProperty("billable", out var b) && b.ValueKind is JsonValueKind.True or JsonValueKind.False) billable = b.GetBoolean();
                            }
                            statuses.Add(new WhatsAppStatusEvent(phoneId, Str(s, "id"), Str(s, "status"), UnixTime(Str(s, "timestamp")), code, message, category, billable));
                        }
                        break;
                    case "message_template_status_update":
                        templates.Add(new WhatsAppTemplateEvent(wabaId, Str(value, "message_template_name"), Str(value, "message_template_language"),
                            Str(value, "event"), Str(value, "reason") is { Length: > 0 } r && r != "NONE" ? r : null));
                        break;
                }
            }
        }
        return new WhatsAppWebhookBatch(statuses, templates);
    }

    // ---------- أدوات ----------

    private async Task<JsonDocument> SendAsync(HttpRequestMessage req, string? token, CancellationToken ct)
    {
        using (req)
        {
            if (token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            HttpResponseMessage res;
            try { res = await _http.CreateClient(HttpClientName).SendAsync(req, ct); }
            catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !ct.IsCancellationRequested))
            {
                throw new WhatsAppProviderException("transport", e.Message, isTransport: true, inner: e);
            }
            using (res)
            {
                var body = await res.Content.ReadAsStringAsync(ct);
                if (res.IsSuccessStatusCode)
                    return string.IsNullOrWhiteSpace(body) ? JsonDocument.Parse("{}") : JsonDocument.Parse(body);
                throw Error((int)res.StatusCode, body);
            }
        }
    }

    private static WhatsAppProviderException Error(int status, string body)
    {
        string code = status.ToString(CultureInfo.InvariantCulture), message = body.Length > 300 ? body[..300] : body;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object)
            {
                if (e.TryGetProperty("code", out var c)) code = c.ToString();
                var details = e.TryGetProperty("error_data", out var data) && data.ValueKind == JsonValueKind.Object ? Str(data, "details") : string.Empty;
                message = new[] { details, Str(e, "message") }.FirstOrDefault(x => x.Length > 0) ?? message;
            }
        }
        catch (JsonException) { }
        return new WhatsAppProviderException(code, message, isAuth: code is "190" or "0" || status == 401);
    }

    private static IEnumerable<JsonElement> Data(JsonElement root)
        => root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array ? data.EnumerateArray() : Enumerable.Empty<JsonElement>();

    private static string Str(JsonElement el, string name)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : v.GetRawText()) : string.Empty;

    private static DateTime UnixTime(string seconds)
        => long.TryParse(seconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? DateTimeOffset.FromUnixTimeSeconds(s).UtcDateTime : DateTime.UtcNow;
}
