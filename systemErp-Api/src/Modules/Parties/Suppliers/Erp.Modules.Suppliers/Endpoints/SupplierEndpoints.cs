using Erp.BuildingBlocks.Web;
using Erp.Modules.Accounting.Contracts;
using Erp.Modules.Suppliers.Application;
using Erp.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Suppliers.Endpoints;

internal sealed record EnsureAccountRequest(string? AccountCode);

/// <summary>master-data/suppliers: suppliers CRUD + POST {id}/account + statement + lookup.</summary>
internal static class SupplierEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var suppliers = app.MapGroup("/api/v1/suppliers").WithTags("Suppliers");

        suppliers.MapGet(string.Empty, async ([AsParameters] PaginationParams paging, SupplierService service, CancellationToken ct) =>
            ErpResults.Ok(await service.ListAsync(paging, ct))).RequireScreen(ScreenIds.MasterData, ScreenAction.View);

        suppliers.MapGet("lookup", async (string? q, SupplierService service, CancellationToken ct) =>
            ErpResults.Ok(await service.LookupAsync(q, ct))).RequireAuthorization();

        suppliers.MapGet("{id:guid}", async (Guid id, SupplierService service, CancellationToken ct) =>
            ErpResults.Ok(await service.GetAsync(id, ct))).RequireScreen(ScreenIds.MasterData, ScreenAction.View);

        suppliers.MapGet("{id:guid}/statement", async (Guid id, DateOnly? from, DateOnly? to, SupplierService service, IAccountStatementService statements, CancellationToken ct) =>
            ErpResults.Ok(await service.StatementAsync(id, from, to, statements, ct))).RequireScreen(ScreenIds.MasterData, ScreenAction.View);

        suppliers.MapPost(string.Empty, async (SaveSupplierRequest request, SupplierService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(request, ct);
            return ErpResults.Created($"/api/v1/suppliers/{created.Id}", created, "تم إضافة المورد وإنشاء حسابه المحاسبي");
        }).RequireScreen(ScreenIds.MasterData, ScreenAction.Create);

        suppliers.MapPut("{id:guid}", async (Guid id, SaveSupplierRequest request, SupplierService service, CancellationToken ct) =>
            ErpResults.Ok(await service.UpdateAsync(id, request, ct), "تم تحديث بيانات المورد")).RequireScreen(ScreenIds.MasterData, ScreenAction.Edit);

        suppliers.MapDelete("{id:guid}", async (Guid id, SupplierService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return ErpResults.Ok(new { id }, "تم حذف المورد");
        }).RequireScreen(ScreenIds.MasterData, ScreenAction.Delete);

        suppliers.MapPost("{id:guid}/account", async (Guid id, EnsureAccountRequest? request, SupplierService service, CancellationToken ct) =>
            ErpResults.Ok(await service.EnsureGlAccountAsync(id, request?.AccountCode, ct), "تم ربط الحساب المحاسبي")).RequireScreen(ScreenIds.Accounts, ScreenAction.Create);
    }
}
