using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.BuildingBlocks.Web;
using Erp.Modules.Inventory.Application;
using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Inventory.Domain;
using Erp.Modules.Inventory.Persistence;
using Erp.SharedKernel.Errors;
using Erp.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Inventory.Endpoints;

internal sealed record StockBalanceDto(Guid ProductId, string Sku, string ProductNameAr, Guid WarehouseId, string WarehouseCode, decimal QuantityOnHand, decimal AverageCost, decimal LastPurchaseCost, decimal Value, decimal MinStockLevel, bool IsLowStock);

/// <summary>The frontend StockMovement shape (+ warehouse and source document).</summary>
internal sealed record StockMovementDto(
    Guid Id,
    Guid ProductId,
    string ItemName,
    Guid WarehouseId,
    DateOnly Date,
    StockMovementType Type,
    decimal Quantity,
    decimal UnitCost,
    decimal? UnitPrice,
    decimal RemainingStock,
    decimal AverageCostAfter,
    string SourceModule,
    string SourceDocumentType,
    Guid SourceDocumentId,
    string ReferenceNumber,
    bool IsReversed);

internal sealed record CostingPolicyDto(
    CostingMethod Method,
    bool RecalculateOnNewPurchase,
    bool IncludeFreightAndCustoms,
    NegativeInventoryPolicy NegativeInventoryPolicy,
    Guid? StandardCostVarianceAccountId,
    DateTimeOffset LastUpdated,
    string? Notes);

/// <summary>Stock balances and ledger, stock adjustments/transfers, and the costing engine settings (costing screen).</summary>
internal static class StockEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/stock-balances", ListBalancesAsync).WithTags("Inventory").RequireAuthorization();
        app.MapGet("/api/v1/stock-movements", ListMovementsAsync).WithTags("Inventory").RequireScreen(ScreenIds.MasterData, ScreenAction.View);

        app.MapPost("/api/v1/stock-adjustments", async (StockAdjustmentRequest request, StockOperations operations, CancellationToken ct) =>
            ErpResults.Ok(await operations.AdjustAsync(request, ct), "تم ترحيل التسوية المخزنية")).WithTags("Inventory").RequireScreen(ScreenIds.MasterData, ScreenAction.Edit);

        app.MapPost("/api/v1/stock-transfers", async (StockTransferRequest request, StockOperations operations, CancellationToken ct) =>
            ErpResults.Ok(await operations.TransferAsync(request, ct), "تم تحويل المخزون بين المستودعات")).WithTags("Inventory").RequireScreen(ScreenIds.MasterData, ScreenAction.Edit);

        var costing = app.MapGroup("/api/v1/inventory").WithTags("Inventory");
        costing.MapGet("costing-policy", GetPolicyAsync).RequireAuthorization();
        costing.MapPut("costing-policy", SavePolicyAsync).RequireRoles(SystemRoles.Owner, SystemRoles.Admin, SystemRoles.ChiefAccountant);
        costing.MapPost("costing/recalculate", async (StockOperations operations, CancellationToken ct) =>
            ErpResults.Ok(await operations.RecalculateAsync(ct), "تم إعادة احتساب التكلفة وأرصدة المخزون")).RequireRoles(SystemRoles.Owner, SystemRoles.Admin, SystemRoles.ChiefAccountant);
    }

    private static async Task<IResult> ListBalancesAsync(Guid? productId, Guid? warehouseId, bool? lowStockOnly, InventoryDbContext db, CancellationToken ct)
    {
        var query =
            from b in db.StockBalances.AsNoTracking()
            join p in db.Products.AsNoTracking() on b.ProductId equals p.Id
            join w in db.Warehouses.AsNoTracking() on b.WarehouseId equals w.Id
            select new { b, p, w };
        if (productId is { } pid)
        {
            query = query.Where(x => x.b.ProductId == pid);
        }

        if (warehouseId is { } wid)
        {
            query = query.Where(x => x.b.WarehouseId == wid);
        }

        if (lowStockOnly == true)
        {
            query = query.Where(x => x.b.QuantityOnHand <= x.p.MinStockLevel);
        }

        return ErpResults.Ok(await query.OrderBy(x => x.p.Sku).ThenBy(x => x.w.Code)
            .Select(x => new StockBalanceDto(x.p.Id, x.p.Sku, x.p.NameAr, x.w.Id, x.w.Code, x.b.QuantityOnHand, x.b.AverageCost, x.b.LastPurchaseCost,
                Math.Round(x.b.QuantityOnHand * x.b.AverageCost, 2), x.p.MinStockLevel, x.b.QuantityOnHand <= x.p.MinStockLevel))
            .ToListAsync(ct));
    }

    private static async Task<IResult> ListMovementsAsync([AsParameters] PaginationParams paging, Guid? productId, Guid? warehouseId, InventoryDbContext db, CancellationToken ct)
    {
        // Left join: movements of a soft-deleted product stay visible in the ledger.
        var query =
            from m in db.StockMovements.AsNoTracking()
            join p in db.Products.AsNoTracking() on m.ProductId equals p.Id into products
            from p in products.DefaultIfEmpty()
            select new { m, NameAr = p != null ? p.NameAr : "(صنف محذوف)" };
        if (productId is { } pid)
        {
            query = query.Where(x => x.m.ProductId == pid);
        }

        if (warehouseId is { } wid)
        {
            query = query.Where(x => x.m.WarehouseId == wid);
        }

        if (paging.StartDate is { } start)
        {
            query = query.Where(x => x.m.Date >= start);
        }

        if (paging.EndDate is { } end)
        {
            query = query.Where(x => x.m.Date <= end);
        }

        var page = await query.OrderByDescending(x => x.m.Date).ThenByDescending(x => x.m.CreatedAt)
            .Select(x => new StockMovementDto(x.m.Id, x.m.ProductId, x.NameAr, x.m.WarehouseId, x.m.Date, x.m.Type, x.m.Quantity, x.m.UnitCost, x.m.UnitPrice,
                x.m.BalanceAfter, x.m.AverageCostAfter, x.m.SourceModule, x.m.SourceDocumentType, x.m.SourceDocumentId, x.m.SourceNumber, x.m.IsReversed))
            .ToPagedResultAsync(paging, ct);
        return ErpResults.Ok(page);
    }

    private static async Task<IResult> GetPolicyAsync(InventoryDbContext db, CancellationToken ct) =>
        ErpResults.Ok(ToDto(await db.CostingPolicies.AsNoTracking().SingleOrDefaultAsync(ct) ?? throw ErpException.NotFound("Costing policy", "سياسة التكلفة")));

    private static async Task<IResult> SavePolicyAsync(CostingPolicyDto request, InventoryDbContext db, IUnitOfWork unitOfWork, TimeProvider clock, CancellationToken ct)
    {
        if (request.Method == CostingMethod.Fifo)
        {
            // FIFO needs cost layers per receipt; until they exist the engine would silently use the average instead.
            throw ErpException.Validation("FIFO costing is not available yet; use moving average.", "طريقة الوارد أولاً صادر أولاً (FIFO) غير متاحة حالياً؛ استخدم المتوسط المرجح.");
        }

        var policy = await db.CostingPolicies.SingleOrDefaultAsync(ct) ?? throw ErpException.NotFound("Costing policy", "سياسة التكلفة");
        policy.Update(request.Method, request.RecalculateOnNewPurchase, request.IncludeFreightAndCustoms, request.NegativeInventoryPolicy, request.StandardCostVarianceAccountId, request.Notes, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Ok(ToDto(policy), "تم حفظ سياسة التكلفة");
    }

    private static CostingPolicyDto ToDto(CostingPolicy p) =>
        new(p.Method, p.RecalculateOnNewPurchase, p.IncludeFreightAndCustoms, p.NegativeInventoryPolicy, p.PurchasePriceVarianceAccountId, p.LastUpdated, p.Notes);
}
