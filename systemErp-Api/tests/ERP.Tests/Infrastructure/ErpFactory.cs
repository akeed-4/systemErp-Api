using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;

namespace ERP.Tests.Infrastructure;

/// <summary>
/// يشغّل الـ API الحقيقي على قاعدة SQL Server مؤقتة (LocalDB افتراضياً، أو ERP_TEST_CONNECTION) تُنشأ بالـ migrations
/// وتُحذف بعد الانتهاء. كل اختبار يسجّل منشأته الخاصة فلا تتداخل الاختبارات.
/// </summary>
public class ErpFactory : WebApplicationFactory<Program>
{
    private readonly string _database = $"ErpTest_{Guid.NewGuid():N}";
    private readonly string _master;
    public string ConnectionString { get; }

    public ErpFactory()
    {
        var baseConn = Environment.GetEnvironmentVariable("ERP_TEST_CONNECTION")
            ?? @"Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True;";
        _master = baseConn;
        ConnectionString = $"{baseConn.TrimEnd(';')};Database={_database};MultipleActiveResultSets=true";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);
        builder.UseSetting("Database:AutoMigrate", "true");
        builder.UseSetting("Jwt:Key", "integration-tests-signing-key-0123456789abcdef");
        builder.UseSetting("Auth:ExposeOtpInResponse", "true");
        builder.UseSetting("Auth:RateLimitPerMinute", "0"); // الاختبارات تسجّل مئات المنشآت من العنوان نفسه
        // Paymob: حساب المنصة للاشتراكات + روابط الإشعار؛ العميل الفعلي يُستبدل ببديل لا يتصل بالإنترنت
        builder.UseSetting("Paymob:SecretKey", "platform-secret");
        builder.UseSetting("Paymob:PublicKey", "platform-public");
        builder.UseSetting("Paymob:HmacSecret", FakePaymobClient.PlatformHmac);
        builder.UseSetting("Paymob:IntegrationIds", "111");
        builder.UseSetting("Paymob:PublicApiUrl", "https://api.test");
        builder.UseSetting("Paymob:FrontendUrl", "https://app.test");
        builder.ConfigureTestServices(services =>
        {
            services.AddScoped<ERP.Core.Contracts.Shared.IPaymobClient, FakePaymobClient>();
            // لا بريد حقيقي في الاختبارات: الرسائل تُلتقط في الذاكرة
            services.AddSingleton<ERP.Core.Contracts.Shared.IEmailSender>(FakeEmailSender.Instance);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        try
        {
            SqlConnection.ClearAllPools();
            using var conn = new SqlConnection(_master);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"IF DB_ID('{_database}') IS NOT NULL BEGIN ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]; END";
            cmd.ExecuteNonQuery();
        }
        catch { /* تنظيف اختياري */ }
    }
}

public class Res
{
    public int Status { get; init; }
    public JsonNode? Body { get; init; }
    public JsonNode? Data => Body?["data"];
    public bool Success => Body?["success"]?.GetValue<bool>() == true;
}

/// <summary>عميل اختبار مصادَق عليه لمنشأة واحدة.</summary>
public class Client
{
    private static readonly Random Rng = new();
    private readonly HttpClient _http;
    public string? Token { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public Guid TenantId { get; private set; }
    /// <summary>استجابة تسجيل المنشأة (تحمل عملية دفع الاشتراك).</summary>
    public Res? Registration { get; private set; }
    /// <summary>قيمة Accept-Language لطلبات هذا العميل (null = بدون ترويسة، فتُستخدم العربية الافتراضية).</summary>
    public string? Language { get; set; }

    public Client(HttpClient http) => _http = http;

    public static string NewVat() => "3" + Rng.NextInt64(0, 9_999_999_999_999).ToString("D13") + "3";
    public static string NewVin(char prefix) => prefix + "HGCM82633A" + Rng.Next(0, 999_999).ToString("D6");

    /// <summary>
    /// يسجّل منشأة جديدة (تبدأ بفترة تجريبية مجانية) ثم يسدّد اشتراكها ليصير فعّالاً؛
    /// <paramref name="pay"/> = false يُبقيها في الفترة التجريبية.
    /// </summary>
    public async Task<Client> RegisterAsync(string name = "شركة اختبار", bool pay = true)
    {
        Email = $"o{Guid.NewGuid():N}@test.com";
        var r = await SendAsync(HttpMethod.Post, "/auth/RegisterCompany", new
        {
            companyNameAr = name, companyNameEn = name, vatNumber = NewVat(), crNumber = "1010", city = "الرياض", address = "x",
            phone = "050", email = Email, industry = "x", planId = "professional", billingCycle = "yearly", paymentMethod = "mada",
            adminName = "المدير", adminEmail = Email, adminPhone = "050", password = "Passw0rd!",
        });
        Token = r.Body!["token"]!.GetValue<string>();
        TenantId = Guid.Parse(r.Body["tenantId"]!.GetValue<string>());
        Registration = r;
        if (pay) await PaySubscriptionAsync();
        return this;
    }

    /// <summary>يسدّد اشتراك المنشأة: عملية دفع ثم إشعار Paymob الموقَّع كما يصل في الإنتاج.</summary>
    public async Task<Res> PaySubscriptionAsync(string planId = "professional", string billingCycle = "yearly", bool success = true)
    {
        var checkout = await Post("/payments/subscription", new { planId, billingCycle });
        var payment = checkout.Data!;
        var (body, hmac) = PaymobCallback.Signed(PaymobCallback.OrderIdOf(payment), payment["amount"].D(), FakePaymobClient.PlatformHmac, success);
        await SendAsync(HttpMethod.Post, $"/payments/paymob/webhook?hmac={hmac}", body, anonymous: true);
        return checkout;
    }

    public Task<Res> Get(string path) => SendAsync(HttpMethod.Get, path);
    public Task<Res> Post(string path, object? body = null) => SendAsync(HttpMethod.Post, path, body ?? new { });
    public Task<Res> Put(string path, object body) => SendAsync(HttpMethod.Put, path, body);
    public Task<Res> Delete(string path) => SendAsync(HttpMethod.Delete, path);

    public async Task<Res> SendAsync(HttpMethod method, string path, object? body = null, bool anonymous = false)
    {
        var req = new HttpRequestMessage(method, "/api/v1" + path);
        if (body != null && method != HttpMethod.Get) req.Content = JsonContent.Create(body);
        if (!anonymous && Token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        if (Language != null) req.Headers.TryAddWithoutValidation("Accept-Language", Language);
        var resp = await _http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        JsonNode? node = null;
        try { node = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text); } catch (JsonException) { }
        return new Res { Status = (int)resp.StatusCode, Body = node };
    }

    /// <summary>عميل يحمل رمز مستخدم آخر (مثلاً مندوب مبيعات) مع نفس السرفر.</summary>
    public async Task<Client> LoginAsAsync(HttpClient http, string email, string password)
    {
        var other = new Client(http) { Email = email };
        var r = await other.SendAsync(HttpMethod.Post, "/auth/login", new { email, password }, anonymous: true);
        other.Token = r.Body?["token"]?.GetValue<string>();
        return other;
    }
}

public static class J
{
    public static decimal D(this JsonNode? n) => n == null ? 0 : n.GetValue<decimal>();
    public static string S(this JsonNode? n) => n?.GetValue<string>() ?? string.Empty;
    public static Guid G(this JsonNode? n) => Guid.Parse(n!.GetValue<string>());
}

/// <summary>بديل البريد في الاختبارات: يلتقط الرسائل بدل إرسالها.</summary>
public sealed class FakeEmailSender : ERP.Core.Contracts.Shared.IEmailSender
{
    public static readonly FakeEmailSender Instance = new();
    public sealed record Mail(string To, string Subject, string Body);
    private readonly System.Collections.Concurrent.ConcurrentQueue<Mail> _sent = new();

    public bool IsConfigured => true;
    /// <summary>بريد يفشل الإرسال إليه (لمحاكاة تعطّل خادم البريد).</summary>
    public string? FailFor { get; set; }

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (string.Equals(to, FailFor, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("smtp down");
        _sent.Enqueue(new Mail(to, subject, htmlBody));
        return Task.CompletedTask;
    }

    public IReadOnlyList<Mail> SentTo(string email) => _sent.Where(m => string.Equals(m.To, email, StringComparison.OrdinalIgnoreCase)).ToList();
}
