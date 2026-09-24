using System.Net.Http.Json;
using Erp.Catalog.Contracts;
using Erp.IntegrationTests.Infrastructure;
using Erp.SharedKernel.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.IntegrationTests;

[Collection(ErpCollection.Name)]
public sealed class DedicatedDatabaseTests(ErpTestEnvironment env)
{
    [Fact]
    public async Task Dedicated_tenant_is_provisioned_end_to_end_and_can_sign_in()
    {
        var tenant = await env.ProvisionAsync(TenancyMode.Dedicated);

        Assert.Equal(TenancyMode.Dedicated, tenant.Result.Mode);
        Assert.Equal(env.DedicatedPrefix + tenant.Code, tenant.Result.DatabaseName);
        Assert.Equal(1, await env.CountAsync("master", "SELECT COUNT(*) FROM sys.databases WHERE name = @n", ("@n", tenant.Result.DatabaseName!)));

        var directory = env.Factory.Services.GetRequiredService<ITenantDirectory>();
        var descriptor = await directory.FindAsync(tenant.Id, CancellationToken.None);
        Assert.Equal(TenantStatus.Active, descriptor!.Status);
        Assert.Contains(tenant.Result.DatabaseName!, descriptor.DedicatedConnectionString, StringComparison.Ordinal);

        using var client = await env.SignInAsync(tenant);
        var company = await (await client.GetAsync("/api/v1/company")).ReadDataAsync();
        Assert.Equal(tenant.NameEn, company.GetProperty("nameEn").GetString());
        Assert.Equal(tenant.Code, company.GetProperty("code").GetString());

        var permissions = await (await client.GetAsync("/api/v1/permissions/me")).ReadDataAsync();
        Assert.Equal(12, permissions.GetProperty("permissions").GetArrayLength());

        var currencies = await (await client.GetAsync("/api/v1/currencies")).ReadDataAsync();
        Assert.Contains(currencies.EnumerateArray(), c => c.GetProperty("code").GetString() == "SAR" && c.GetProperty("isBaseCurrency").GetBoolean());

        var subscription = await (await client.GetAsync("/api/v1/subscriptions/current")).ReadDataAsync();
        Assert.Equal("starter", subscription.GetProperty("planId").GetString());
        Assert.Equal("trial", subscription.GetProperty("status").GetString());

        var upgraded = await (await client.PostAsJsonAsync("/api/v1/subscriptions/upgrade", new { planId = "enterprise", billingCycle = "yearly", paymentMethod = "bank_transfer" })).ReadDataAsync();
        Assert.Equal("enterprise", upgraded.GetProperty("planId").GetString());
        Assert.Equal("active", upgraded.GetProperty("status").GetString());
        Assert.Equal(9990m, upgraded.GetProperty("paidAmount").GetDecimal());
        var current = await (await client.GetAsync("/api/v1/subscriptions/current")).ReadDataAsync();
        Assert.Equal("enterprise", current.GetProperty("planId").GetString());
    }

    [Fact]
    public async Task Dedicated_tenant_requests_touch_only_its_own_database()
    {
        var shared = await env.ProvisionAsync(TenancyMode.Shared);
        var dedicated = await env.ProvisionAsync(TenancyMode.Dedicated);
        var dedicatedDb = dedicated.Result.DatabaseName!;

        using var client = await env.SignInAsync(dedicated);
        await (await client.PostAsJsonAsync("/api/v1/branches", new { code = "DX1", nameAr = "مستودع جدة", type = "warehouse" })).ReadDataAsync();
        await (await client.PostAsJsonAsync("/api/v1/currencies", new { code = "USD", nameAr = "دولار", nameEn = "US Dollar", symbol = "$", exchangeRate = 3.75m })).ReadDataAsync();

        // Every row of the dedicated tenant is in its own database …
        Assert.Equal(1, await env.CountAsync(dedicatedDb, "SELECT COUNT(*) FROM org.Branches WHERE Code = 'DX1'"));
        Assert.Equal(1, await env.CountAsync(dedicatedDb, "SELECT COUNT(*) FROM org.Tenants WHERE Id = @id", ("@id", dedicated.Id)));
        Assert.Equal(1, await env.CountAsync(dedicatedDb, "SELECT COUNT(*) FROM [identity].Users WHERE TenantId = @id", ("@id", dedicated.Id)));
        Assert.Equal(1, await env.CountAsync(dedicatedDb, "SELECT COUNT(*) FROM settings.Currencies WHERE Code = 'USD'"));

        // … and none of it reaches the shared database.
        Assert.Equal(0, await env.CountAsync(env.SharedDatabase, "SELECT COUNT(*) FROM org.Branches WHERE TenantId = @id", ("@id", dedicated.Id)));
        Assert.Equal(0, await env.CountAsync(env.SharedDatabase, "SELECT COUNT(*) FROM [identity].Users WHERE TenantId = @id", ("@id", dedicated.Id)));
        Assert.Equal(0, await env.CountAsync(env.SharedDatabase, "SELECT COUNT(*) FROM org.Tenants WHERE Id = @id", ("@id", dedicated.Id)));

        // The dedicated database holds only this tenant; the shared tenant is untouched.
        Assert.Equal(0, await env.CountAsync(dedicatedDb, "SELECT COUNT(*) FROM org.Branches WHERE TenantId <> @id", ("@id", dedicated.Id)));
        Assert.Equal(1, await env.CountAsync(env.SharedDatabase, "SELECT COUNT(*) FROM org.Tenants WHERE Id = @id", ("@id", shared.Id)));
    }

    [Fact]
    public async Task Jwt_tenant_wins_over_a_spoofed_X_Tenant_Id_header()
    {
        var a = await env.ProvisionAsync(TenancyMode.Shared);
        var b = await env.ProvisionAsync(TenancyMode.Dedicated);

        using var clientA = await env.SignInAsync(a);
        clientA.DefaultRequestHeaders.Add("X-Tenant-Id", b.Id.ToString());

        var company = await (await clientA.GetAsync($"/api/v1/company?tenantId={b.Id}")).ReadDataAsync();
        Assert.Equal(a.Id, company.GetProperty("id").GetGuid());
        Assert.Equal(a.NameEn, company.GetProperty("nameEn").GetString());

        var branches = await (await clientA.GetAsync("/api/v1/branches")).ReadDataAsync();
        Assert.Equal(1, branches.GetProperty("totalCount").GetInt32());
    }
}
