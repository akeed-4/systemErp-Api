using System.Text;
using ERP.Service.Services.Shared;
using ERP.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;

namespace ERP.Tests;

/// <summary>
/// تكامل واتساب: الاستحقاق من الاشتراك، الربط عبر Embedded Signup وفشله، العزل بين المنشآت، تشفير الرمز، الفصل وإعادة الربط،
/// ورفض الرمز، وإشعارات Meta الموقَّعة، وإعداد المنصة.
/// </summary>
[Collection("api")]
public class WhatsAppIntegrationTests : TestBase
{
    public WhatsAppIntegrationTests(ErpFactory f) : base(f) { }

    private static FakeWhatsAppProvider Meta => FakeWhatsAppProvider.Instance;

    private Task<Client> EntitledTenantAsync() => NewTenantWithModulesAsync("accounting", "whatsapp");

    private static async Task<Res> ConnectAsync(Client api, (string WabaId, string PhoneNumberId, string Code) account, bool coexistence = false)
        => await api.Post("/whatsapp/connect", new { code = account.Code, wabaId = account.WabaId, phoneNumberId = account.PhoneNumberId, businessId = "3000000003", isCoexistence = coexistence });

    private async Task<int> PostWebhookAsync(string body, string? signature)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/whatsapp/webhook") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (signature != null) req.Headers.TryAddWithoutValidation("X-Hub-Signature-256", signature);
        return (int)(await NewHttp().SendAsync(req)).StatusCode;
    }

    private string StoredToken(Guid tenantId)
    {
        using var conn = new SqlConnection(Factory.ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT AccessTokenEncrypted FROM TenantWhatsAppIntegration WHERE TenantId = @t";
        cmd.Parameters.AddWithValue("@t", tenantId);
        return cmd.ExecuteScalar() as string ?? string.Empty;
    }

    private async Task<Client> SalesRepAsync(Client owner)
    {
        var email = $"s{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await owner.Post("/users", new { name = "مندوب", email, phone = "1", password = "Passw0rd!", role = "sales_rep" })).Status);
        return await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");
    }

    private async Task<Client> PlatformAdminAsync()
    {
        var admin = await NewTenantAsync("مدير منصة واتساب");
        var host = Factory.WithWebHostBuilder(b => b.UseSetting("Platform:AdminEmails:0", admin.Email));
        return await admin.LoginAsAsync(host.CreateClient(), admin.Email, "Passw0rd!");
    }

    // ---------- الاشتراك ----------

    [Fact]
    public async Task Without_the_add_on_the_integration_is_refused_by_the_server()
    {
        var api = await NewTenantAsync(); // الباقة الافتراضية بلا إضافة واتساب
        Assert.Equal(403, (await api.Get("/whatsapp/integration")).Status);
        Assert.Equal(403, (await ConnectAsync(api, Meta.NewAccount())).Status);
        Assert.Equal(403, (await api.Post("/whatsapp/sync")).Status);
    }

    [Fact]
    public async Task The_add_on_cannot_be_the_only_module_of_a_subscription()
    {
        var root = await PlatformAdminAsync();
        var tenant = await NewTenantAsync();
        Assert.Equal(400, (await root.Post($"/platform/tenants/{tenant.TenantId}/subscriptions/modules", new { modules = new[] { "whatsapp" } })).Status);
        Assert.Equal(200, (await root.Post($"/platform/tenants/{tenant.TenantId}/subscriptions/modules", new { modules = new[] { "pos", "whatsapp" } })).Status);
        var current = (await tenant.Get("/subscriptions/current")).Data!["modules"]!.AsArray().Select(m => m.S()).ToArray();
        Assert.Equal(new[] { "pos", "whatsapp" }, current);
        Assert.Equal(200, (await tenant.Get("/whatsapp/integration")).Status);
    }

    // ---------- الربط ----------

    [Fact]
    public async Task Connecting_stores_the_number_encrypts_the_token_and_submits_the_invoice_template()
    {
        var api = await EntitledTenantAsync();
        var config = await api.Get("/whatsapp/signup-config");
        Assert.True(config.Data!["isConfigured"]!.GetValue<bool>());
        Assert.Equal(FakeWhatsAppProvider.ConfigId, config.Data["configId"].S());
        Assert.DoesNotContain(FakeWhatsAppProvider.AppSecret, config.Body!.ToJsonString());

        var account = Meta.NewAccount();
        var r = await ConnectAsync(api, account);
        Assert.Equal(200, r.Status);
        Assert.True(r.Data!["isConnected"]!.GetValue<bool>());
        Assert.Equal(account.PhoneNumberId, r.Data["phoneNumberId"].S());
        Assert.Equal("PENDING", r.Data["invoiceTemplateStatus"].S());
        Assert.True(Meta.SubscribedAccounts.ContainsKey(account.WabaId));
        Assert.True(Meta.RegisteredPins.ContainsKey(account.PhoneNumberId));
        Assert.Equal(2, Meta.Templates[account.WabaId].Count); // عربي وإنجليزي

        // الرمز لا يخرج في أي استجابة، ومحفوظ مشفّراً
        var json = (await api.Get("/whatsapp/integration")).Body!.ToJsonString();
        Assert.DoesNotContain("token-", json);
        Assert.DoesNotContain("accessToken", json, StringComparison.OrdinalIgnoreCase);
        var stored = StoredToken(api.TenantId);
        Assert.False(string.IsNullOrEmpty(stored));
        Assert.DoesNotContain("token-", stored);
    }

    [Fact]
    public async Task A_failed_signup_leaves_nothing_connected()
    {
        var api = await EntitledTenantAsync();
        var account = Meta.NewAccount();

        Assert.Equal(409, (await ConnectAsync(api, account with { Code = FakeWhatsAppProvider.BadCode })).Status);
        Assert.Equal(400, (await ConnectAsync(api, account with { PhoneNumberId = FakeWhatsAppProvider.NewId() })).Status); // رقم لا يتبع الحساب
        Assert.Equal(400, (await ConnectAsync(api, account with { WabaId = "not-a-number" })).Status);
        Assert.False((await api.Get("/whatsapp/integration")).Data!["isConnected"]!.GetValue<bool>());
        Assert.Equal(409, (await api.Post("/whatsapp/sync")).Status);
    }

    [Fact]
    public async Task Coexistence_numbers_are_not_re_registered()
    {
        var api = await EntitledTenantAsync();
        var account = Meta.NewAccount();
        var r = await ConnectAsync(api, account, coexistence: true);
        Assert.Equal(200, r.Status);
        Assert.True(r.Data!["isCoexistence"]!.GetValue<bool>());
        Assert.False(Meta.RegisteredPins.ContainsKey(account.PhoneNumberId));
    }

    // ---------- العزل ----------

    [Fact]
    public async Task A_number_belongs_to_one_company_and_each_company_sees_only_its_own()
    {
        var a = await EntitledTenantAsync();
        var b = await EntitledTenantAsync();
        var account = Meta.NewAccount();
        Assert.Equal(200, (await ConnectAsync(a, account)).Status);

        // الرقم نفسه لا يُربط بمنشأة أخرى ولو حصلت على رمز صالح
        Assert.Equal(409, (await ConnectAsync(b, account with { Code = "code-other" })).Status);
        var bView = (await b.Get("/whatsapp/integration")).Data!;
        Assert.False(bView["isConnected"]!.GetValue<bool>());
        Assert.Null(bView["phoneNumberId"]);

        // فصل B لا يمس ربط A
        Assert.Equal(409, (await b.Delete("/whatsapp/integration")).Status);
        Assert.True((await a.Get("/whatsapp/integration")).Data!["isConnected"]!.GetValue<bool>());

        // بعد فصل A يصير الرقم متاحاً لغيرها
        Assert.Equal(200, (await a.Delete("/whatsapp/integration")).Status);
        Assert.Equal(200, (await ConnectAsync(b, account with { Code = "code-b" }, coexistence: true)).Status);
    }

    // ---------- الصلاحيات ----------

    [Fact]
    public async Task Only_administrative_roles_connect_or_disconnect_while_sales_reps_see_the_status()
    {
        var owner = await EntitledTenantAsync();
        var rep = await SalesRepAsync(owner);
        Assert.Equal(200, (await rep.Get("/whatsapp/integration")).Status); // زر الإرسال يحتاج معرفة الحالة
        Assert.Equal(403, (await rep.Get("/whatsapp/signup-config")).Status);
        Assert.Equal(403, (await ConnectAsync(rep, Meta.NewAccount())).Status);

        Assert.Equal(200, (await ConnectAsync(owner, Meta.NewAccount())).Status);
        Assert.Equal(403, (await rep.Delete("/whatsapp/integration")).Status);
        Assert.Equal(403, (await rep.Post("/whatsapp/sync")).Status);
        Assert.Equal(403, (await rep.Post("/whatsapp/templates/invoice")).Status);
    }

    // ---------- الفصل وإعادة الربط ورفض الرمز ----------

    [Fact]
    public async Task Disconnect_then_reconnect_reuses_the_registration_pin()
    {
        var api = await EntitledTenantAsync();
        var account = Meta.NewAccount();
        Assert.Equal(200, (await ConnectAsync(api, account)).Status);
        var pin = Meta.RegisteredPins[account.PhoneNumberId];

        var off = await api.Delete("/whatsapp/integration");
        Assert.Equal(200, off.Status);
        Assert.Equal("disconnected", off.Data!["status"].S());
        Assert.False(Meta.SubscribedAccounts.ContainsKey(account.WabaId));
        Assert.Equal(string.Empty, StoredToken(api.TenantId)); // لا رمز محفوظ بعد الفصل
        Assert.Equal(409, (await api.Delete("/whatsapp/integration")).Status);

        var again = await ConnectAsync(api, account with { Code = "code-again-" + account.WabaId });
        Assert.Equal(200, again.Status); // رمز التسجيل السابق نفسه، وإلا رفضت Meta
        Assert.Equal(pin, Meta.RegisteredPins[account.PhoneNumberId]);
        Assert.True(again.Data!["isConnected"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_revoked_token_marks_the_integration_for_reconnection()
    {
        var api = await EntitledTenantAsync();
        var account = Meta.NewAccount();
        Assert.Equal(200, (await ConnectAsync(api, account)).Status);

        Meta.RevokeTokensOf(account.Code);
        Assert.Equal(409, (await api.Post("/whatsapp/sync")).Status);
        var state = (await api.Get("/whatsapp/integration")).Data!;
        Assert.Equal("token_invalid", state["status"].S());
        Assert.False(state["isConnected"]!.GetValue<bool>());
        Assert.Equal(409, (await api.Get("/whatsapp/templates")).Status); // يلزم إعادة الربط

        Assert.Equal(200, (await ConnectAsync(api, account with { Code = "code-fresh-" + account.WabaId })).Status);
        Assert.Equal("connected", (await api.Get("/whatsapp/integration")).Data!["status"].S());
    }

    // ---------- الإشعارات ----------

    [Fact]
    public async Task Webhook_handshake_requires_the_verify_token()
    {
        var ok = await NewHttp().GetAsync($"/api/v1/whatsapp/webhook?hub.mode=subscribe&hub.verify_token={FakeWhatsAppProvider.VerifyToken}&hub.challenge=12345");
        Assert.Equal(200, (int)ok.StatusCode);
        Assert.Equal("12345", await ok.Content.ReadAsStringAsync());
        Assert.Equal(403, (int)(await NewHttp().GetAsync("/api/v1/whatsapp/webhook?hub.mode=subscribe&hub.verify_token=wrong&hub.challenge=1")).StatusCode);
        Assert.Equal(403, (int)(await NewHttp().GetAsync("/api/v1/whatsapp/webhook?hub.mode=other&hub.verify_token=" + FakeWhatsAppProvider.VerifyToken + "&hub.challenge=1")).StatusCode);
    }

    [Fact]
    public async Task Webhook_requires_a_valid_signature_and_updates_the_template_status_of_the_right_company()
    {
        var a = await EntitledTenantAsync();
        var b = await EntitledTenantAsync();
        var accountA = Meta.NewAccount();
        var accountB = Meta.NewAccount();
        Assert.Equal(200, (await ConnectAsync(a, accountA)).Status);
        Assert.Equal(200, (await ConnectAsync(b, accountB)).Status);

        var body = FakeWhatsAppProvider.TemplateWebhook(accountA.WabaId, WhatsAppService.InvoiceTemplateName, "ar", "APPROVED");
        Assert.Equal(401, await PostWebhookAsync(body, null));
        Assert.Equal(401, await PostWebhookAsync(body, FakeWhatsAppProvider.Sign(body, "wrong-secret")));
        Assert.Equal(401, await PostWebhookAsync(body + " ", FakeWhatsAppProvider.Sign(body))); // الجسم تغيّر بعد التوقيع
        Assert.Equal("PENDING", (await a.Get("/whatsapp/integration")).Data!["invoiceTemplateStatus"].S());

        Assert.Equal(200, await PostWebhookAsync(body, FakeWhatsAppProvider.Sign(body)));
        Assert.Equal("APPROVED", (await a.Get("/whatsapp/integration")).Data!["invoiceTemplateStatus"].S());
        Assert.Equal("PENDING", (await b.Get("/whatsapp/integration")).Data!["invoiceTemplateStatus"].S());

        var rejected = FakeWhatsAppProvider.TemplateWebhook(accountB.WabaId, WhatsAppService.InvoiceTemplateName, "ar", "REJECTED", "INVALID_FORMAT");
        Assert.Equal(200, await PostWebhookAsync(rejected, FakeWhatsAppProvider.Sign(rejected)));
        var bState = (await b.Get("/whatsapp/integration")).Data!;
        Assert.Equal("REJECTED", bState["invoiceTemplateStatus"].S());
        Assert.Contains("INVALID_FORMAT", bState["lastError"].S());

        // إشعار لحساب لا يخص أحداً: يُقبل بلا أثر
        var unknown = FakeWhatsAppProvider.TemplateWebhook(FakeWhatsAppProvider.NewId(), "x", "ar", "APPROVED");
        Assert.Equal(200, await PostWebhookAsync(unknown, FakeWhatsAppProvider.Sign(unknown)));
    }

    // ---------- إعداد المنصة ----------

    [Fact]
    public async Task Platform_meta_settings_are_admin_only_and_never_return_secrets()
    {
        var owner = await NewTenantAsync();
        Assert.Equal(403, (await owner.Get("/platform/whatsapp")).Status);

        var root = await PlatformAdminAsync();
        var settings = await root.Get("/platform/whatsapp");
        Assert.Equal(200, settings.Status);
        Assert.True(settings.Data!["isConfigured"]!.GetValue<bool>());
        Assert.True(settings.Data["hasAppSecret"]!.GetValue<bool>());
        Assert.Equal("https://api.test/api/v1/whatsapp/webhook", settings.Data["webhookUrl"].S());
        Assert.DoesNotContain(FakeWhatsAppProvider.AppSecret, settings.Body!.ToJsonString());
        Assert.DoesNotContain(FakeWhatsAppProvider.VerifyToken, settings.Body!.ToJsonString());

        Assert.Equal(400, (await root.Put("/platform/whatsapp", new { appId = "abc", configId = FakeWhatsAppProvider.ConfigId, graphVersion = "v26.0" })).Status);
        Assert.Equal(400, (await root.Put("/platform/whatsapp", new { appId = FakeWhatsAppProvider.AppId, configId = FakeWhatsAppProvider.ConfigId, graphVersion = "26" })).Status);

        // الحقول السرية الفارغة تُبقي المحفوظ
        var saved = await root.Put("/platform/whatsapp", new { appId = FakeWhatsAppProvider.AppId, configId = FakeWhatsAppProvider.ConfigId, graphVersion = "v26.0" });
        Assert.Equal(200, saved.Status);
        Assert.True(saved.Data!["isConfigured"]!.GetValue<bool>());
        Assert.True(saved.Data["isCustomized"]!.GetValue<bool>());
    }
}
