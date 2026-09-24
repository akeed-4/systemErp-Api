using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.BuildingBlocks.Infrastructure.Tenancy;
using Erp.Catalog.Contracts;
using Erp.SharedKernel.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.IntegrationTests.Infrastructure;

public sealed record TestTenant(ProvisionTenantResult Result, string OwnerEmail, string Password)
{
    public Guid Id => Result.TenantId;

    public string Code => Result.TenantCode;

    public string NameEn => $"{Code} Trading Co";
}

public static class TestTenants
{
    public const string Password = "Passw0rd!2026";

    public static string NewCode(string prefix) => $"{prefix}{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

    public static string NewEmail(string prefix) => $"{prefix}.{Guid.NewGuid():N}@test.sa".ToLowerInvariant();

    public static async Task<TestTenant> ProvisionAsync(this ErpTestEnvironment env, TenancyMode mode, string? email = null, string? code = null)
    {
        code ??= NewCode(mode == TenancyMode.Shared ? "S" : "D");
        email ??= NewEmail(code);

        var provisioning = env.Factory.Services.GetRequiredService<ITenantProvisioningService>();
        var result = await provisioning.ProvisionAsync(
            new ProvisionTenantRequest(
                code,
                mode,
                new CompanyInfo($"شركة {code}", $"{code} Trading Co", "300000000000003", "1010000000", "الرياض", "طريق الملك فهد", "0110000000", email, "automotive"),
                new OwnerInfo($"Owner {code}", email, string.Empty, Password),
                new SubscriptionRequest("starter", "monthly", "mada")),
            CancellationToken.None);

        return new TestTenant(result, email, Password);
    }

    public static async Task<JsonElement> LoginRawAsync(this ErpTestEnvironment env, string email, string password, string? tenantCode = null)
    {
        using var client = env.Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password, tenantCode });
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static async Task<HttpClient> SignInAsync(this ErpTestEnvironment env, TestTenant tenant) =>
        await env.SignInAsync(tenant.OwnerEmail, tenant.Password, tenant.Code);

    public static async Task<HttpClient> SignInAsync(this ErpTestEnvironment env, string email, string password, string? tenantCode)
    {
        var body = await env.LoginRawAsync(email, password, tenantCode);
        Assert.True(body.GetProperty("success").GetBoolean(), body.ToString());
        return env.Authorized(body.GetProperty("token").GetString()!);
    }

    public static HttpClient Authorized(this ErpTestEnvironment env, string token)
    {
        var client = env.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>A DI scope bound to the tenant, exactly as the API would build it for that tenant's requests.</summary>
    public static Task<AsyncServiceScope> TenantScopeAsync(this ErpTestEnvironment env, Guid tenantId) =>
        env.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateForTenantAsync(tenantId, requireServable: true, CancellationToken.None);

    public static async Task<JsonElement> ReadDataAsync(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return body.GetProperty("data");
    }
}
