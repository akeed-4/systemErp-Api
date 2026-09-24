using System.Net;
using System.Net.Http.Json;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.IntegrationTests.Infrastructure;
using Erp.Modules.Organization.Contracts;
using Erp.Modules.Organization.Domain;
using Erp.Modules.Organization.Persistence;
using Erp.Modules.Settings.Contracts;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.IntegrationTests;

[Collection(ErpCollection.Name)]
public sealed class SharedDatabaseIsolationTests(ErpTestEnvironment env)
{
    [Fact]
    public async Task Tenant_A_cannot_read_or_update_tenant_B_rows_through_the_api()
    {
        var a = await env.ProvisionAsync(TenancyMode.Shared);
        var b = await env.ProvisionAsync(TenancyMode.Shared);
        using var clientA = await env.SignInAsync(a);
        using var clientB = await env.SignInAsync(b);

        var created = await (await clientB.PostAsJsonAsync("/api/v1/branches", new { code = "SR1", nameAr = "معرض الشمال", type = "showroom" })).ReadDataAsync();
        var branchId = created.GetProperty("id").GetGuid();

        // List: B's branch is invisible to A.
        var listA = await (await clientA.GetAsync("/api/v1/branches?pageSize=200")).ReadDataAsync();
        Assert.DoesNotContain(listA.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetGuid() == branchId);
        Assert.Contains(listA.GetProperty("items").EnumerateArray(), i => i.GetProperty("code").GetString() == "HO");

        // Direct id: read, update and delete all behave as if the row did not exist.
        Assert.Equal(HttpStatusCode.NotFound, (await clientA.GetAsync($"/api/v1/branches/{branchId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientA.PutAsJsonAsync($"/api/v1/branches/{branchId}", new { nameAr = "مخترق", type = "showroom" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientA.DeleteAsync($"/api/v1/branches/{branchId}")).StatusCode);

        var stillB = await (await clientB.GetAsync($"/api/v1/branches/{branchId}")).ReadDataAsync();
        Assert.Equal("معرض الشمال", stillB.GetProperty("nameAr").GetString());
    }

    [Fact]
    public async Task Query_filter_hides_other_tenants_rows_even_when_queried_by_id()
    {
        var a = await env.ProvisionAsync(TenancyMode.Shared);
        var b = await env.ProvisionAsync(TenancyMode.Shared);

        Guid bHeadOffice;
        await using (var scopeB = await env.TenantScopeAsync(b.Id))
        {
            var db = scopeB.ServiceProvider.GetRequiredService<OrganizationDbContext>();
            bHeadOffice = (await db.Branches.SingleAsync(x => x.Code == Branch.HeadOfficeCode)).Id;
        }

        await using var scopeA = await env.TenantScopeAsync(a.Id);
        var dbA = scopeA.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        Assert.Null(await dbA.Branches.FindAsync(bHeadOffice));
        Assert.Empty(await dbA.Branches.Where(x => x.Id == bHeadOffice).ToListAsync());
        Assert.All(await dbA.Companies.ToListAsync(), c => Assert.Equal(a.Id, c.TenantId));
    }

    [Fact]
    public async Task SaveChanges_guard_rejects_an_entity_with_a_foreign_TenantId()
    {
        var a = await env.ProvisionAsync(TenancyMode.Shared);
        var b = await env.ProvisionAsync(TenancyMode.Shared);

        await using var scope = await env.TenantScopeAsync(a.Id);
        var db = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var intruder = new Branch("X1", "فرع دخيل", "Intruder", BranchType.Warehouse);
        ((ITenantScoped)intruder).StampTenant(b.Id);
        db.Branches.Add(intruder);

        await Assert.ThrowsAsync<TenantIsolationViolationException>(() => unitOfWork.SaveChangesAsync());
        Assert.Equal(0, await env.CountAsync(env.SharedDatabase, "SELECT COUNT(*) FROM org.Branches WHERE Code = 'X1'"));
    }

    [Fact]
    public async Task SaveChanges_guard_rejects_moving_a_row_to_another_tenant()
    {
        var a = await env.ProvisionAsync(TenancyMode.Shared);
        var b = await env.ProvisionAsync(TenancyMode.Shared);

        await using var scope = await env.TenantScopeAsync(a.Id);
        var db = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        var headOffice = await db.Branches.SingleAsync(x => x.Code == Branch.HeadOfficeCode);
        ((ITenantScoped)headOffice).StampTenant(b.Id);

        await Assert.ThrowsAsync<TenantIsolationViolationException>(() => scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync());
    }

    [Fact]
    public async Task A_scope_without_a_tenant_cannot_open_a_tenant_database()
    {
        await using var scope = env.Factory.Services.CreateAsyncScope();
        Assert.Throws<TenantNotResolvedException>(() => scope.ServiceProvider.GetRequiredService<OrganizationDbContext>());
    }

    [Fact]
    public async Task Document_sequences_are_numbered_per_tenant()
    {
        var a = await env.ProvisionAsync(TenancyMode.Shared);
        var b = await env.ProvisionAsync(TenancyMode.Shared);
        var date = new DateOnly(2026, 9, 24);

        async Task<string[]> TakeAsync(Guid tenantId, int count)
        {
            await using var scope = await env.TenantScopeAsync(tenantId);
            var sequences = scope.ServiceProvider.GetRequiredService<INumberSequenceService>();
            var numbers = new List<string>();
            for (var i = 0; i < count; i++)
            {
                numbers.Add(await sequences.NextAsync("inv", date, null, CancellationToken.None));
            }

            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            return [.. numbers];
        }

        Assert.Equal(["INV-2026-0001", "INV-2026-0002"], await TakeAsync(a.Id, 2));
        Assert.Equal(["INV-2026-0001"], await TakeAsync(b.Id, 1));
        Assert.Equal(["INV-2026-0003"], await TakeAsync(a.Id, 1));
    }
}
