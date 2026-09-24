using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.BuildingBlocks.Infrastructure.Outbox;
using Erp.IntegrationTests.Infrastructure;
using Erp.SharedKernel.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.IntegrationTests;

[Collection(ErpCollection.Name)]
public sealed class AuthenticationTests(ErpTestEnvironment env)
{
    [Fact]
    public async Task Login_with_an_email_in_two_tenants_requires_a_tenant_choice()
    {
        var email = TestTenants.NewEmail("multi");
        var first = await env.ProvisionAsync(TenancyMode.Shared, email);
        var second = await env.ProvisionAsync(TenancyMode.Dedicated, email);

        var choice = await env.LoginRawAsync(email, TestTenants.Password);
        Assert.False(choice.GetProperty("success").GetBoolean());
        Assert.True(choice.GetProperty("requiresTenantSelection").GetBoolean());
        Assert.False(choice.TryGetProperty("token", out _));
        var codes = choice.GetProperty("tenants").EnumerateArray().Select(t => t.GetProperty("code").GetString()).Order().ToList();
        Assert.Equal(new[] { first.Code, second.Code }.Order(), codes);

        var chosen = await env.LoginRawAsync(email, TestTenants.Password, second.Code);
        Assert.True(chosen.GetProperty("success").GetBoolean());
        Assert.Equal(second.Id, chosen.GetProperty("user").GetProperty("tenantId").GetGuid());
        Assert.Equal(second.Id, chosen.GetProperty("tenant").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Wrong_password_is_rejected_without_revealing_tenants()
    {
        var email = TestTenants.NewEmail("wrong");
        await env.ProvisionAsync(TenancyMode.Shared, email);
        await env.ProvisionAsync(TenancyMode.Shared, email);

        using var client = env.Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "not-the-password" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.False(body.TryGetProperty("tenants", out _));
        Assert.Equal("invalid_credentials", body.GetProperty("problem").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Tenant_switch_issues_a_token_for_the_target_tenant_only_if_the_user_exists_there()
    {
        var email = TestTenants.NewEmail("switch");
        var first = await env.ProvisionAsync(TenancyMode.Shared, email);
        var second = await env.ProvisionAsync(TenancyMode.Dedicated, email);
        var stranger = await env.ProvisionAsync(TenancyMode.Shared);

        using var client = await env.SignInAsync(email, TestTenants.Password, first.Code);

        var tenants = await (await client.GetAsync("/api/v1/tenants")).ReadDataAsync();
        Assert.Equal(2, tenants.GetArrayLength());

        var switched = await (await client.PostAsync($"/api/v1/tenants/{second.Id}/switch", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(switched.GetProperty("success").GetBoolean());
        using var secondClient = env.Authorized(switched.GetProperty("token").GetString()!);
        var company = await (await secondClient.GetAsync("/api/v1/company")).ReadDataAsync();
        Assert.Equal(second.Id, company.GetProperty("id").GetGuid());

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/v1/tenants/{stranger.Id}/switch", null)).StatusCode);
    }

    [Fact]
    public async Task Register_company_provisions_a_shared_tenant_and_signs_in()
    {
        var email = TestTenants.NewEmail("register");
        using var client = env.Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register-company", new
        {
            companyNameAr = "معارض الاختبار",
            companyNameEn = "Test Showrooms",
            vatNumber = "310000000000003",
            crNumber = "1010101010",
            city = "الرياض",
            address = "حي العليا",
            phone = "0110000001",
            email,
            industry = "automotive",
            planId = "professional",
            billingCycle = "yearly",
            paymentMethod = "mada",
            adminName = "Admin Tester",
            adminEmail = email,
            adminPhone = "0500000001",
            password = TestTenants.Password,
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("success").GetBoolean(), body.ToString());
        var tenantId = body.GetProperty("tenantId").GetGuid();
        Assert.Equal("owner", body.GetProperty("user").GetProperty("role").GetString());
        Assert.Equal(1, await env.CountAsync(env.SharedDatabase, "SELECT COUNT(*) FROM org.Tenants WHERE Id = @id", ("@id", tenantId)));

        using var authorized = env.Authorized(body.GetProperty("token").GetString()!);
        var company = await (await authorized.GetAsync("/api/v1/company")).ReadDataAsync();
        Assert.Equal("Test Showrooms", company.GetProperty("nameEn").GetString());

        // The same person can sign in again later with email + password (phone works too).
        var byPhone = await env.LoginRawAsync("+966500000001", TestTenants.Password);
        Assert.True(byPhone.GetProperty("success").GetBoolean(), byPhone.ToString());
    }

    [Fact]
    public async Task New_user_can_sign_in_once_the_outbox_syncs_the_login_index()
    {
        var tenant = await env.ProvisionAsync(TenancyMode.Dedicated);
        using var owner = await env.SignInAsync(tenant);
        var email = TestTenants.NewEmail("cashier");

        await (await owner.PostAsJsonAsync("/api/v1/users", new { name = "Cashier One", email, role = "sales_rep", password = TestTenants.Password })).ReadDataAsync();

        var before = await env.LoginRawAsync(email, TestTenants.Password);
        Assert.False(before.GetProperty("success").GetBoolean());

        var processed = await env.Factory.Services.GetRequiredService<OutboxDispatcher>().DispatchAllAsync(CancellationToken.None);
        Assert.True(processed >= 1);

        var after = await env.LoginRawAsync(email, TestTenants.Password);
        Assert.True(after.GetProperty("success").GetBoolean(), after.ToString());
        Assert.Equal("sales_rep", after.GetProperty("user").GetProperty("role").GetString());

        // Sales reps have no access to user management (frontend default permissions).
        using var cashier = env.Authorized(after.GetProperty("token").GetString()!);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/v1/users")).StatusCode);
    }

    [Fact]
    public async Task Refresh_token_rotates_and_password_reset_revokes_sessions()
    {
        var tenant = await env.ProvisionAsync(TenancyMode.Shared);
        var login = await env.LoginRawAsync(tenant.OwnerEmail, tenant.Password);
        var refreshToken = login.GetProperty("refreshToken").GetString()!;

        using var client = env.Factory.CreateClient();
        var refreshed = await (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(refreshed.GetProperty("success").GetBoolean());
        var rotated = refreshed.GetProperty("refreshToken").GetString()!;
        Assert.NotEqual(refreshToken, rotated);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken })).StatusCode);

        var request = await (await client.PostAsJsonAsync("/api/v1/auth/forgot-password/request", new { identifier = tenant.OwnerEmail })).Content.ReadFromJsonAsync<JsonElement>();
        var userId = request.GetProperty("userId").GetGuid();
        var otp = request.GetProperty("otpCode").GetString();
        var verify = await (await client.PostAsJsonAsync("/api/v1/auth/forgot-password/verify-otp", new { userId, otp })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(verify.GetProperty("success").GetBoolean());

        const string newPassword = "N3wPassw0rd!";
        var reset = await (await client.PostAsJsonAsync("/api/v1/auth/forgot-password/reset", new { userId, otp, newPassword })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(reset.GetProperty("success").GetBoolean());

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = rotated })).StatusCode);
        Assert.True((await env.LoginRawAsync(tenant.OwnerEmail, newPassword)).GetProperty("success").GetBoolean());
    }
}
