using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ERP.Core.Contracts.Shared;
using Microsoft.AspNetCore.DataProtection;

namespace ERP.Service.Services.Shared;

/// <summary>إعدادات حساب Paymob الخاص بالمنصة (اشتراكات المنشآت) وروابط الإشعار والعودة.</summary>
public class PaymobOptions
{
    public const string Section = "Paymob";
    public string BaseUrl { get; set; } = "https://ksa.paymob.com";
    public string SecretKey { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string HmacSecret { get; set; } = string.Empty;
    public string IntegrationIds { get; set; } = string.Empty;
    /// <summary>العنوان العام للـ API (يصل إليه Paymob لإرسال الإشعار).</summary>
    public string PublicApiUrl { get; set; } = string.Empty;
    /// <summary>عنوان الواجهة (صفحة نتيجة الدفع بعد العودة من Paymob).</summary>
    public string FrontendUrl { get; set; } = string.Empty;
}

/// <summary>
/// Paymob Intention API (Unified Checkout): إنشاء نية دفع ثم توجيه العميل لصفحة الدفع المستضافة لدى Paymob.
/// بيانات البطاقة لا تمر بخوادمنا إطلاقاً.
/// </summary>
public class PaymobClient : IPaymobClient
{
    private readonly IHttpClientFactory _http;
    public PaymobClient(IHttpClientFactory http) => _http = http;

    public async Task<PaymobIntention> CreateIntentionAsync(PaymobCredentials c, PaymobIntentionRequest r, CancellationToken ct = default)
    {
        var client = _http.CreateClient("paymob");
        var baseUrl = c.BaseUrl.TrimEnd('/');
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/intention/");
        req.Headers.Authorization = new AuthenticationHeaderValue("Token", c.SecretKey);
        var (first, last) = SplitName(r.CustomerName);
        req.Content = JsonContent.Create(new
        {
            amount = r.AmountCents,
            currency = r.Currency,
            payment_methods = c.IntegrationIds,
            items = new[] { new { name = Truncate(r.Description, 50), amount = r.AmountCents, description = r.Description, quantity = 1 } },
            billing_data = new
            {
                first_name = first, last_name = last,
                email = string.IsNullOrWhiteSpace(r.CustomerEmail) ? "na@example.com" : r.CustomerEmail,
                phone_number = string.IsNullOrWhiteSpace(r.CustomerPhone) ? "+966500000000" : r.CustomerPhone,
                country = "SA",
            },
            special_reference = r.SpecialReference,
            notification_url = r.NotificationUrl,
            redirection_url = r.RedirectionUrl,
        });

        using var res = await client.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new ConflictException(string.Format(Messages.PaymobRejectedPayment, (int)res.StatusCode, Truncate(body, 300)));

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var clientSecret = root.GetProperty("client_secret").GetString() ?? throw new ConflictException(Messages.PaymobNoClientSecret);
        var id = root.TryGetProperty("id", out var idEl) ? idEl.ToString() : string.Empty;
        long? orderId = root.TryGetProperty("intention_order_id", out var o) && o.ValueKind == JsonValueKind.Number ? o.GetInt64() : null;
        var checkout = $"{baseUrl}/unifiedcheckout/?publicKey={Uri.EscapeDataString(c.PublicKey)}&clientSecret={Uri.EscapeDataString(clientSecret)}";
        return new PaymobIntention(id, clientSecret, orderId, checkout);
    }

    private static (string First, string Last) SplitName(string? name)
    {
        var parts = (name ?? "Customer").Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return (parts.ElementAtOrDefault(0) ?? "Customer", parts.ElementAtOrDefault(1) ?? "NA");
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];
}

/// <summary>
/// التحقق من توقيع إشعار Paymob (Transaction Processed Callback): HMAC-SHA512 لقيم حقول محددة بترتيب أبجدي ثابت.
/// </summary>
public static class PaymobHmac
{
    private static readonly string[] Fields =
    {
        "amount_cents", "created_at", "currency", "error_occured", "has_parent_transaction", "id", "integration_id",
        "is_3d_secure", "is_auth", "is_capture", "is_refunded", "is_standalone_payment", "is_voided", "order.id",
        "owner", "pending", "source_data.pan", "source_data.sub_type", "source_data.type", "success",
    };

    public static string Compute(JsonElement obj, string secret)
    {
        var sb = new StringBuilder();
        foreach (var f in Fields) sb.Append(Value(obj, f));
        using var h = new HMACSHA512(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    public static bool Verify(JsonElement obj, string? hmac, string secret)
    {
        if (string.IsNullOrWhiteSpace(hmac) || string.IsNullOrWhiteSpace(secret)) return false;
        var expected = Encoding.ASCII.GetBytes(Compute(obj, secret));
        var given = Encoding.ASCII.GetBytes(hmac.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, given);
    }

    private static string Value(JsonElement obj, string path)
    {
        var el = obj;
        foreach (var part in path.Split('.'))
        {
            if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(part, out el)) return string.Empty;
        }
        return el.ValueKind switch
        {
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            JsonValueKind.String => el.GetString() ?? string.Empty,
            _ => el.GetRawText(),
        };
    }
}

/// <summary>تشفير الأسرار بـ ASP.NET Data Protection (المفاتيح تُدار خارج قاعدة البيانات).</summary>
public class SecretProtector : ISecretProtector
{
    private readonly IDataProtector _protector;
    public SecretProtector(IDataProtectionProvider provider) => _protector = provider.CreateProtector("ERP.PaymentGateway.Secrets.v1");
    public string Protect(string plain) => string.IsNullOrEmpty(plain) ? string.Empty : _protector.Protect(plain);
    public string Unprotect(string cipher) => string.IsNullOrEmpty(cipher) ? string.Empty : _protector.Unprotect(cipher);
}
