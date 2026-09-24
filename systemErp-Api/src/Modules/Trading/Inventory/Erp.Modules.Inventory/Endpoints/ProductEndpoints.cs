using Erp.BuildingBlocks.Web;
using Erp.Modules.Inventory.Application;
using Erp.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Inventory.Endpoints;

/// <summary>master-data/items (product mode): products + barcode lookup (POS / purchases scanning).</summary>
internal static class ProductEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var products = app.MapGroup("/api/v1/products").WithTags("Inventory");

        products.MapGet(string.Empty, async ([AsParameters] PaginationParams paging, Guid? categoryId, bool? lowStockOnly, ProductService service, CancellationToken ct) =>
            ErpResults.Ok(await service.ListAsync(paging, categoryId, lowStockOnly == true, ct))).RequireAuthorization();

        products.MapGet("by-barcode/{code}", async (string code, ProductService service, CancellationToken ct) =>
            ErpResults.Ok(await service.GetByCodeAsync(code, ct))).RequireAuthorization();

        products.MapGet("{id:guid}", async (Guid id, ProductService service, CancellationToken ct) =>
            ErpResults.Ok(await service.GetAsync(id, ct))).RequireAuthorization();

        products.MapPost(string.Empty, async (SaveProductRequest request, ProductService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(request, ct);
            return ErpResults.Created($"/api/v1/products/{created.Id}", created, "تم إضافة الصنف");
        }).RequireScreen(ScreenIds.MasterData, ScreenAction.Create);

        products.MapPut("{id:guid}", async (Guid id, SaveProductRequest request, ProductService service, CancellationToken ct) =>
            ErpResults.Ok(await service.UpdateAsync(id, request, ct), "تم تحديث الصنف")).RequireScreen(ScreenIds.MasterData, ScreenAction.Edit);

        products.MapDelete("{id:guid}", async (Guid id, ProductService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return ErpResults.Ok(new { id }, "تم حذف الصنف");
        }).RequireScreen(ScreenIds.MasterData, ScreenAction.Delete);
    }
}
