using ERP.Tests.Infrastructure;

namespace ERP.Tests;

[Collection("api")]
public class AuthTests : TestBase
{
    public AuthTests(ErpFactory f) : base(f) { }

    [Fact]
    public async Task Unauthenticated_requests_are_rejected_with_a_consistent_error()
    {
        var api = new Client(NewHttp());
        var r = await api.Get("/customers");
        Assert.Equal(401, r.Status);
        Assert.False(r.Success);
        Assert.Equal(401, r.Body!["statusCode"]!.GetValue<int>());
    }

    [Fact]
    public async Task Register_creates_tenant_admin_and_default_chart_of_accounts()
    {
        var api = await NewTenantAsync();
        var accounts = await api.Get("/accounts?pageSize=500");
        var codes = accounts.Data!["items"]!.AsArray().Select(a => a!["code"].S()).ToList();
        Assert.Contains("112", codes); Assert.Contains("211", codes); Assert.Contains("213", codes);
        Assert.Contains("412", codes); Assert.Contains("1142", codes);
        var me = await api.Get("/auth/me");
        Assert.Equal("owner", me.Data!["role"].S());
        Assert.Null(me.Data["passwordHash"]); // لا يُكشف أبداً
    }

    [Theory]
    [InlineData("123")]
    [InlineData("400000000000003")]
    [InlineData("3abcdefghijklm3")]
    public async Task Register_rejects_invalid_saudi_vat_number(string vat)
    {
        var r = await new Client(NewHttp()).SendAsync(HttpMethod.Post, "/auth/register-company", new
        {
            companyNameAr = "x", vatNumber = vat, adminName = "a", adminEmail = $"{Guid.NewGuid():N}@x.com", password = "Passw0rd!", planId = "starter", billingCycle = "monthly",
        }, anonymous: true);
        Assert.Equal(400, r.Status);
    }

    [Fact]
    public async Task Duplicate_vat_number_is_a_conflict()
    {
        var vat = Client.NewVat();
        object Body(string mail) => new { companyNameAr = "x", vatNumber = vat, adminName = "a", adminEmail = mail, password = "Passw0rd!", planId = "starter", billingCycle = "monthly" };
        var api = new Client(NewHttp());
        Assert.Equal(200, (await api.SendAsync(HttpMethod.Post, "/auth/register-company", Body($"{Guid.NewGuid():N}@x.com"), true)).Status);
        Assert.Equal(409, (await api.SendAsync(HttpMethod.Post, "/auth/register-company", Body($"{Guid.NewGuid():N}@x.com"), true)).Status);
    }

    [Fact]
    public async Task Login_succeeds_with_right_password_and_fails_with_wrong_one()
    {
        var api = await NewTenantAsync();
        var http = NewHttp();
        var ok = await new Client(http).SendAsync(HttpMethod.Post, "/auth/login", new { email = api.Email, password = "Passw0rd!" }, true);
        Assert.Equal(200, ok.Status);
        Assert.False(string.IsNullOrEmpty(ok.Body!["token"].S()));
        var bad = await new Client(http).SendAsync(HttpMethod.Post, "/auth/login", new { email = api.Email, password = "wrong" }, true);
        Assert.Equal(401, bad.Status);
    }

    [Fact]
    public async Task Forgot_password_flow_with_otp_resets_the_password()
    {
        var api = await NewTenantAsync();
        var anon = new Client(NewHttp());
        var req = await anon.SendAsync(HttpMethod.Post, "/auth/forgot-password/request", new { identifier = api.Email }, true);
        var userId = req.Body!["userId"].S(); var otp = req.Body["otpCode"].S();
        Assert.Equal(6, otp.Length);

        var wrong = await anon.SendAsync(HttpMethod.Post, "/auth/forgot-password/verify-otp", new { userId, otp = "000000" == otp ? "111111" : "000000" }, true);
        Assert.Equal(400, wrong.Status);
        var verify = await anon.SendAsync(HttpMethod.Post, "/auth/forgot-password/verify-otp", new { userId, otp }, true);
        Assert.True(verify.Body!["success"]!.GetValue<bool>());
        var reset = await anon.SendAsync(HttpMethod.Post, "/auth/forgot-password/reset", new { userId, otp, newPassword = "NewPassw0rd!" }, true);
        Assert.Equal(200, reset.Status);
        // الرمز يُستخدم مرة واحدة
        Assert.Equal(400, (await anon.SendAsync(HttpMethod.Post, "/auth/forgot-password/reset", new { userId, otp, newPassword = "Another123!" }, true)).Status);
        Assert.Equal(200, (await anon.SendAsync(HttpMethod.Post, "/auth/login", new { email = api.Email, password = "NewPassw0rd!" }, true)).Status);
    }

    [Fact]
    public async Task Unknown_account_does_not_reveal_existence_on_forgot_password()
    {
        var r = await new Client(NewHttp()).SendAsync(HttpMethod.Post, "/auth/forgot-password/request", new { identifier = "nobody@nowhere.com" }, true);
        Assert.Equal(200, r.Status);
        Assert.Null(r.Body!["otpCode"]);
    }
}
