using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.BuildingBlocks.Infrastructure.Schema;
using Erp.Catalog.Contracts;
using Erp.Catalog.Persistence;
using Erp.Catalog.Services;
using Erp.IntegrationTests.Infrastructure;
using Erp.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.IntegrationTests;

[Collection(ErpCollection.Name)]
public sealed class MigratorAndTenantAccessTests(ErpTestEnvironment env)
{
    [Fact]
    public async Task Migrator_migrates_catalog_shared_and_every_dedicated_database_and_is_rerunnable()
    {
        var modules = env.Factory.Services.GetServices<ModuleDatabase>().ToList();
        var tenantModules = modules.Count;

        var first = await env.ProvisionAsync(TenancyMode.Dedicated);
        var second = await env.ProvisionAsync(TenancyMode.Dedicated);

        // Simulate a dedicated database that is missing entirely (e.g. restored server): the migrator recreates it.
        await env.DropDatabaseAsync(second.Result.DatabaseName!);

        var migrator = env.Factory.Services.GetRequiredService<IDatabaseMigrationOrchestrator>();
        var run1 = await migrator.RunAsync(CancellationToken.None);
        Assert.True(run1.Succeeded, Describe(run1));
        Assert.Contains(run1.Databases, d => d.Kind == "catalog" && d.Database == env.CatalogDatabase);
        Assert.Contains(run1.Databases, d => d.Kind == "shared" && d.Database == env.SharedDatabase);
        Assert.Contains(run1.Databases, d => d.Database == first.Result.DatabaseName && d.AppliedMigrations == 0);
        Assert.Contains(run1.Databases, d => d.Database == second.Result.DatabaseName && d.AppliedMigrations == tenantModules);

        var run2 = await migrator.RunAsync(CancellationToken.None);
        Assert.True(run2.Succeeded, Describe(run2));
        Assert.All(run2.Databases, d => Assert.Equal(0, d.AppliedMigrations));
        Assert.True(run2.Databases.Count(d => d.Kind.StartsWith("dedicated", StringComparison.Ordinal)) >= 2);

        var expected = env.Factory.Services.GetRequiredService<ISchemaVersionProvider>().ExpectedVersion;
        var directory = env.Factory.Services.GetRequiredService<ITenantDirectory>();
        Assert.Equal(expected, (await directory.FindAsync(first.Id, CancellationToken.None))!.SchemaVersion);
        Assert.Equal(expected, (await directory.FindAsync(second.Id, CancellationToken.None))!.SchemaVersion);

        foreach (var schema in modules.Select(m => m.Schema))
        {
            Assert.Equal(1, await env.CountAsync(second.Result.DatabaseName!, $"SELECT COUNT(*) FROM [{schema}].[__EFMigrationsHistory]"));
        }

        Assert.Equal(12, await env.CountAsync(second.Result.DatabaseName!, "SELECT COUNT(*) FROM authz.Screens"));
        Assert.Equal(5, await env.CountAsync(second.Result.DatabaseName!, "SELECT COUNT(*) FROM [identity].Roles"));
    }

    [Fact]
    public async Task Suspended_tenant_gets_403_and_outdated_schema_gets_503()
    {
        var tenant = await env.ProvisionAsync(TenancyMode.Shared);
        using var client = await env.SignInAsync(tenant);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/company")).StatusCode);

        await UpdateCatalogAsync(tenant.Id, t => t.Status = TenantStatus.Suspended);
        var suspended = await client.GetAsync("/api/v1/company");
        Assert.Equal(HttpStatusCode.Forbidden, suspended.StatusCode);
        Assert.Equal("tenant_not_active", (await suspended.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("problem").GetProperty("code").GetString());

        await UpdateCatalogAsync(tenant.Id, t =>
        {
            t.Status = TenantStatus.Active;
            t.SchemaVersion = "platform=old";
        });
        var outdated = await client.GetAsync("/api/v1/company");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, outdated.StatusCode);
        Assert.Equal("tenant_schema_outdated", (await outdated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("problem").GetProperty("code").GetString());

        // Anonymous login to that tenant is refused the same way.
        Assert.False((await env.LoginRawAsync(tenant.OwnerEmail, tenant.Password)).GetProperty("success").GetBoolean());

        await UpdateCatalogAsync(tenant.Id, t => t.SchemaVersion = env.Factory.Services.GetRequiredService<ISchemaVersionProvider>().ExpectedVersion);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/company")).StatusCode);
    }

    private async Task UpdateCatalogAsync(Guid tenantId, Action<CatalogTenant> change)
    {
        var factory = env.Factory.Services.GetRequiredService<IDbContextFactory<CatalogDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            change(await db.Tenants.SingleAsync(t => t.Id == tenantId));
            await db.SaveChangesAsync();
        }

        env.Factory.Services.GetRequiredService<ITenantDirectory>().Invalidate(tenantId);
    }

    private static string Describe(MigrationReport report) =>
        string.Join("; ", report.Databases.Select(d => $"{d.Database}/{d.Kind}: {(d.Succeeded ? "ok" : d.Error)} ({d.AppliedMigrations})"));
}
