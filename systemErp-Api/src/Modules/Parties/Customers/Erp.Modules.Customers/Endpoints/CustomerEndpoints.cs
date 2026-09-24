using Erp.BuildingBlocks.Web;
using Erp.Modules.Accounting.Contracts;
using Erp.Modules.Customers.Application;
using Erp.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Customers.Endpoints;

internal sealed record EnsureAccountRequest(string? AccountCode);

/// <summary>master-data/customers: customers CRUD + POST {id}/account + statement + lookup.</summary>
internal static class CustomerEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var customers = app.MapGroup("/api/v1/customers").WithTags("Customers");

        customers.MapGet(string.Empty, async ([AsParameters] PaginationParams paging, CustomerService service, CancellationToken ct) =>
            ErpResults.Ok(await service.ListAsync(paging, ct))).RequireScreen(ScreenIds.MasterData, ScreenAction.View);

        // Lookup is used by every selling screen, so it only needs a signed-in user.
        customers.MapGet("lookup", async (string? q, CustomerService service, CancellationToken ct) =>
            ErpResults.Ok(await service.LookupAsync(q, ct))).RequireAuthorization();

        customers.MapGet("{id:guid}", async (Guid id, CustomerService service, CancellationToken ct) =>
            ErpResults.Ok(await service.GetAsync(id, ct))).RequireScreen(ScreenIds.MasterData, ScreenAction.View);

        customers.MapGet("{id:guid}/statement", async (Guid id, DateOnly? from, DateOnly? to, CustomerService service, IAccountStatementService statements, CancellationToken ct) =>
            ErpResults.Ok(await service.StatementAsync(id, from, to, statements, ct))).RequireScreen(ScreenIds.MasterData, ScreenAction.View);

        customers.MapPost(string.Empty, async (SaveCustomerRequest request, CustomerService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(request, ct);
            return ErpResults.Created($"/api/v1/customers/{created.Id}", created, "تم إضافة العميل وإنشاء حسابه المحاسبي");
        }).RequireScreen(ScreenIds.MasterData, ScreenAction.Create);

        customers.MapPut("{id:guid}", async (Guid id, SaveCustomerRequest request, CustomerService service, CancellationToken ct) =>
            ErpResults.Ok(await service.UpdateAsync(id, request, ct), "تم تحديث بيانات العميل")).RequireScreen(ScreenIds.MasterData, ScreenAction.Edit);

        customers.MapDelete("{id:guid}", async (Guid id, CustomerService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return ErpResults.Ok(new { id }, "تم حذف العميل");
        }).RequireScreen(ScreenIds.MasterData, ScreenAction.Delete);

        customers.MapPost("{id:guid}/account", async (Guid id, EnsureAccountRequest? request, CustomerService service, CancellationToken ct) =>
            ErpResults.Ok(await service.EnsureGlAccountAsync(id, request?.AccountCode, ct), "تم ربط الحساب المحاسبي")).RequireScreen(ScreenIds.Accounts, ScreenAction.Create);
    }
}
