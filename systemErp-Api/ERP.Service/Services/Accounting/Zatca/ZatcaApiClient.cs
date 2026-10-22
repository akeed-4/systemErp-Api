using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ERP.Core.Contracts.Accounting;

namespace ERP.Service.Services.Accounting;

/// <summary>عميل HTTP لواجهة «فاتورة» (الإصدار V2). لا يرمي عند رفض الهيئة: يعيد حكمها ورسائلها كما وردت.</summary>
public class ZatcaApiClient : IZatcaApiClient
{
    public const string HttpClientName = "zatca";
    private const string ApiVersion = "V2";

    private readonly IHttpClientFactory _http;
    public ZatcaApiClient(IHttpClientFactory http) => _http = http;

    public async Task<ZatcaCsidResult> RequestComplianceCsidAsync(string baseUrl, string csrBase64, string otp, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/compliance") { Content = JsonContent.Create(new { csr = csrBase64 }) };
        request.Headers.TryAddWithoutValidation("OTP", otp);
        return await SendCsidAsync(request, ct);
    }

    public async Task<ZatcaCsidResult> RequestProductionCsidAsync(string baseUrl, string complianceRequestId, string token, string secret, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/production/csids")
        {
            Content = JsonContent.Create(new { compliance_request_id = complianceRequestId }),
        };
        Authorize(request, token, secret);
        return await SendCsidAsync(request, ct);
    }

    public Task<ZatcaApiResult> CheckComplianceAsync(string baseUrl, string token, string secret, ZatcaSubmission invoice, CancellationToken ct = default)
        => SendInvoiceAsync($"{baseUrl}/compliance/invoices", token, secret, invoice, clearance: null, ct);

    public Task<ZatcaApiResult> ReportAsync(string baseUrl, string token, string secret, ZatcaSubmission invoice, CancellationToken ct = default)
        => SendInvoiceAsync($"{baseUrl}/invoices/reporting/single", token, secret, invoice, clearance: false, ct);

    public Task<ZatcaApiResult> ClearAsync(string baseUrl, string token, string secret, ZatcaSubmission invoice, CancellationToken ct = default)
        => SendInvoiceAsync($"{baseUrl}/invoices/clearance/single", token, secret, invoice, clearance: true, ct);

    private async Task<ZatcaCsidResult> SendCsidAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Prepare(request);
        try
        {
            using var response = await _http.CreateClient(HttpClientName).SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            using var json = Parse(body);
            if (!response.IsSuccessStatusCode || json == null)
                return new ZatcaCsidResult(false, null, null, null, Describe(json, body, (int)response.StatusCode));

            var root = json.RootElement;
            var token = Text(root, "binarySecurityToken");
            var secret = Text(root, "secret");
            // requestID رقم في رد الهيئة
            var requestId = root.TryGetProperty("requestID", out var id) ? id.ToString() : null;
            return string.IsNullOrEmpty(token) || string.IsNullOrEmpty(secret)
                ? new ZatcaCsidResult(false, requestId, null, null, Describe(json, body, (int)response.StatusCode))
                : new ZatcaCsidResult(true, requestId, token, secret, Text(root, "dispositionMessage"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new ZatcaCsidResult(false, null, null, null, ex.Message);
        }
    }

    private async Task<ZatcaApiResult> SendInvoiceAsync(string url, string token, string secret, ZatcaSubmission invoice, bool? clearance, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new
            {
                invoiceHash = invoice.InvoiceHash,
                uuid = invoice.Uuid.ToString(),
                invoice = Convert.ToBase64String(Encoding.UTF8.GetBytes(invoice.Xml)),
            }),
        };
        Prepare(request);
        Authorize(request, token, secret);
        if (clearance.HasValue) request.Headers.TryAddWithoutValidation("Clearance-Status", clearance.Value ? "1" : "0");

        try
        {
            using var response = await _http.CreateClient(HttpClientName).SendAsync(request, ct);
            var status = (int)response.StatusCode;
            var body = await response.Content.ReadAsStringAsync(ct);
            using var json = Parse(body);

            // حكم الهيئة على الفاتورة (يستهلك ICV): ما يعدّه قاموس الأخطاء مستهلِكاً للسلسلة (200، 202، 400، 422).
            // ما عداه (مصادقة، رابط، ضغط، خادم، أو رمز غير معروف) ليس حكماً: تبقى السلسلة ويُعاد الإرسال بالبيانات نفسها
            var verdict = ZatcaErrorDictionary.Http.TryGetValue(status, out var http) && http.Chain == ZatcaChainEffect.Consumed;
            var result = new ZatcaApiResult
            {
                Reached = verdict, HttpStatus = status, Accepted = status is 200 or 202 or 409,
                Errors = Validation(json, "errorMessages"), Warnings = Validation(json, "warningMessages"),
                ClearedInvoice = json == null ? null : Text(json.RootElement, "clearedInvoice"),
                Message = verdict ? null : Describe(json, body, status),
            };
            if (verdict && !result.Accepted && result.Errors.Count == 0) result.Errors.Add(Describe(json, body, status));
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new ZatcaApiResult { Reached = false, Message = ex.Message };
        }
    }

    private static void Prepare(HttpRequestMessage request)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Accept-Version", ApiVersion);
        request.Headers.TryAddWithoutValidation("Accept-Language", "en");
    }

    /// <summary>المصادقة الأساسية: رمز الأمان اسماً والسر كلمة مرور.</summary>
    private static void Authorize(HttpRequestMessage request, string token, string secret)
        => request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{token}:{secret}")));

    private static JsonDocument? Parse(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try { return JsonDocument.Parse(body); }
        catch (JsonException) { return null; }
    }

    private static string? Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>رسائل التحقق: «الرمز: النص» لكل رسالة في validationResults.</summary>
    private static List<string> Validation(JsonDocument? json, string name)
    {
        var list = new List<string>();
        if (json == null || json.RootElement.ValueKind != JsonValueKind.Object) return list;
        if (!json.RootElement.TryGetProperty("validationResults", out var results) || results.ValueKind != JsonValueKind.Object) return list;
        if (!results.TryGetProperty(name, out var messages) || messages.ValueKind != JsonValueKind.Array) return list;
        foreach (var m in messages.EnumerateArray())
        {
            var code = Text(m, "code");
            var message = Text(m, "message");
            list.Add(string.IsNullOrEmpty(code) ? message ?? m.ToString() : $"{code}: {message}");
        }
        return list;
    }

    private static string Describe(JsonDocument? json, string body, int status)
    {
        if (json != null && json.RootElement.ValueKind == JsonValueKind.Object)
        {
            var root = json.RootElement;
            var text = Text(root, "message") ?? Text(root, "errorMessage") ?? Text(root, "error");
            if (!string.IsNullOrEmpty(text)) return $"{status}: {text}";
            if (root.TryGetProperty("errors", out var errors)) return $"{status}: {errors}";
        }
        var raw = body.Length > 300 ? body[..300] : body;
        return string.IsNullOrWhiteSpace(raw) ? $"HTTP {status}" : $"{status}: {raw}";
    }
}
