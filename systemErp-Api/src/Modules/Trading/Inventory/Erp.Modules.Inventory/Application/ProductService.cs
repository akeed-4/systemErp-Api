using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.BuildingBlocks.Web;
using Erp.Modules.Accounting.Contracts;
using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Inventory.Domain;
using Erp.Modules.Inventory.Persistence;
using Erp.Modules.Settings.Contracts;
using Erp.SharedKernel.Errors;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Inventory.Application;

internal sealed record WarehouseStockDto(Guid WarehouseId, string WarehouseCode, string WarehouseNameAr, decimal Quantity, decimal AverageCost, decimal LastPurchaseCost, decimal Value);

/// <summary>The frontend ProductItem shape; stock and costs are totals over all warehouses.</summary>
internal sealed record ProductDto(
    Guid Id,
    Guid TenantId,
    string Sku,
    string? Barcode,
    string NameAr,
    string NameEn,
    Guid CategoryId,
    string Category,
    Guid UnitId,
    string Unit,
    decimal CurrentStock,
    decimal AverageCost,
    decimal LastPurchaseCost,
    decimal? StandardCost,
    decimal SellingPrice,
    decimal VatRate,
    decimal MinStockLevel,
    bool IsLowStock,
    string? Notes,
    string Status,
    IReadOnlyList<WarehouseStockDto>? Warehouses);

internal sealed record SaveProductRequest(
    string? Sku,
    string? Barcode,
    string NameAr,
    string? NameEn,
    Guid? CategoryId,
    Guid? UnitId,
    decimal SellingPrice,
    decimal? VatRate,
    decimal? MinStockLevel,
    decimal? StandardCost,
    string? Notes,
    string? Status,
    decimal? OpeningQuantity,
    decimal? OpeningUnitCost,
    Guid? OpeningWarehouseId);

internal sealed class ProductService(
    InventoryDbContext db,
    IUnitOfWork unitOfWork,
    IInventoryService inventory,
    IWarehouseDirectory warehouses,
    IAccountingPostingService posting,
    INumberSequenceService numbers,
    TimeProvider clock)
{
    public const string Module = "inventory";

    public async Task<PagedResult<ProductDto>> ListAsync(PaginationParams paging, Guid? categoryId, bool lowStockOnly, CancellationToken ct)
    {
        var query = db.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(paging.SearchTerm))
        {
            var term = paging.SearchTerm.Trim();
            query = query.Where(p => p.Sku.Contains(term) || p.NameAr.Contains(term) || p.NameEn.Contains(term) || (p.Barcode != null && p.Barcode.Contains(term)));
        }

        if (categoryId is { } category)
        {
            query = query.Where(p => p.CategoryId == category);
        }

        if (lowStockOnly)
        {
            query = query.Where(p => db.StockBalances.Where(b => b.ProductId == p.Id).Sum(b => (decimal?)b.QuantityOnHand) <= p.MinStockLevel
                || !db.StockBalances.Any(b => b.ProductId == p.Id));
        }

        var page = await query.OrderBy(p => p.Sku).ToPagedResultAsync(paging, ct);
        return new PagedResult<ProductDto>(await ToDtosAsync(page.Items, includeWarehouses: false, ct), page.TotalCount, page.PageNumber, page.PageSize);
    }

    public async Task<ProductDto> GetAsync(Guid id, CancellationToken ct) =>
        (await ToDtosAsync([await FindAsync(id, tracking: false, ct)], includeWarehouses: true, ct))[0];

    public async Task<ProductDto> GetByCodeAsync(string code, CancellationToken ct)
    {
        var value = code.Trim();
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Barcode == value || p.Sku == value.ToUpper(), ct)
            ?? throw ErpException.NotFound("Product", "الصنف");
        return (await ToDtosAsync([product], includeWarehouses: true, ct))[0];
    }

    public async Task<ProductDto> CreateAsync(SaveProductRequest request, CancellationToken ct)
    {
        var id = await unitOfWork.ExecuteAsync(
            async innerCt =>
            {
                var sku = string.IsNullOrWhiteSpace(request.Sku) ? await numbers.NextCodeAsync("product", "ITM-", 4, innerCt) : request.Sku.Trim().ToUpperInvariant();
                if (await db.Products.AnyAsync(p => p.Sku == sku, innerCt))
                {
                    throw ErpException.Conflict("sku_taken", $"SKU {sku} already exists.", $"رمز الصنف {sku} مستخدم مسبقاً.");
                }

                var product = new Product(sku);
                await ApplyAsync(product, request, innerCt);
                db.Products.Add(product);
                await unitOfWork.SaveChangesAsync(innerCt);

                if (request.OpeningQuantity is > 0)
                {
                    await ReceiveOpeningStockAsync(product, request, innerCt);
                }

                return product.Id;
            },
            ct);
        return await GetAsync(id, ct);
    }

    public async Task<ProductDto> UpdateAsync(Guid id, SaveProductRequest request, CancellationToken ct)
    {
        var product = await FindAsync(id, tracking: true, ct);
        await ApplyAsync(product, request, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var product = await FindAsync(id, tracking: true, ct);
        if (await db.StockBalances.AnyAsync(b => b.ProductId == id && b.QuantityOnHand != 0, ct))
        {
            throw ErpException.Conflict("product_has_stock", "A product with stock cannot be deleted; deactivate it instead.", "لا يمكن حذف صنف له رصيد مخزني؛ يمكنك إيقافه بدلاً من ذلك.");
        }

        product.Delete();
        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task ReceiveOpeningStockAsync(Product product, SaveProductRequest request, CancellationToken ct)
    {
        var warehouseId = request.OpeningWarehouseId ?? (await warehouses.GetDefaultAsync(ct)).Id;
        var unitCost = request.OpeningUnitCost ?? request.StandardCost
            ?? throw ErpException.Validation("Opening stock needs a unit cost.", "الرصيد الافتتاحي للصنف يحتاج إلى تكلفة الوحدة.");
        var date = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var document = new StockDocument(Module, "opening_stock", product.Id, product.Sku, date);

        var received = await inventory.ReceiveAsync(document, StockMovementType.Opening, [new StockLine(product.Id, warehouseId, request.OpeningQuantity!.Value, unitCost)], ct);
        var value = received.Sum(r => r.TotalCost);
        if (value > 0)
        {
            await posting.PostAsync(
                new PostingRequest(
                    new SourceRef(Module, "opening_stock", product.Id, product.Sku),
                    "opening_stock",
                    date,
                    $"رصيد افتتاحي للصنف {product.NameAr}",
                    [
                        new PostingLine(AccountRef.For(PostingPurpose.Inventory), value, 0),
                        new PostingLine(AccountRef.For(PostingPurpose.OpeningBalances), 0, value),
                    ]),
                ct);
        }
    }

    private async Task ApplyAsync(Product product, SaveProductRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NameAr) || request.SellingPrice < 0 || request.VatRate is < 0 or > 100)
        {
            throw ErpException.Validation("Name, a non-negative price and a VAT rate between 0 and 100 are required.", "الاسم وسعر البيع ونسبة الضريبة (0-100) مطلوبة.");
        }

        var categoryId = request.CategoryId ?? await db.Categories.OrderBy(c => c.Code).Select(c => c.Id).FirstAsync(ct);
        var unitId = request.UnitId ?? await db.Units.Where(u => u.Code == "PCS").Select(u => u.Id).FirstAsync(ct);
        if (!await db.Categories.AnyAsync(c => c.Id == categoryId, ct) || !await db.Units.AnyAsync(u => u.Id == unitId && u.IsActive, ct))
        {
            throw ErpException.Validation("Unknown category or unit.", "التصنيف أو الوحدة غير موجود.");
        }

        var barcode = request.Barcode?.Trim();
        if (!string.IsNullOrEmpty(barcode) && await db.Products.AnyAsync(p => p.Barcode == barcode && p.Id != product.Id, ct))
        {
            throw ErpException.Conflict("barcode_taken", "Another product already uses this barcode.", "الباركود مستخدم لصنف آخر.");
        }

        product.Update(
            barcode,
            request.NameAr,
            request.NameEn ?? string.Empty,
            categoryId,
            unitId,
            request.SellingPrice,
            request.VatRate ?? 15m,
            request.MinStockLevel ?? 0,
            request.StandardCost,
            request.Notes,
            !string.Equals(request.Status, "inactive", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<Product> FindAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var query = tracking ? db.Products : db.Products.AsNoTracking();
        return await query.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw ErpException.NotFound("Product", "الصنف");
    }

    private async Task<List<ProductDto>> ToDtosAsync(IReadOnlyList<Product> products, bool includeWarehouses, CancellationToken ct)
    {
        var ids = products.Select(p => p.Id).ToList();
        var balances = await (
            from b in db.StockBalances.AsNoTracking()
            join w in db.Warehouses.AsNoTracking() on b.WarehouseId equals w.Id
            where ids.Contains(b.ProductId)
            select new { b.ProductId, b.WarehouseId, w.Code, w.NameAr, b.QuantityOnHand, b.AverageCost, b.LastPurchaseCost }).ToListAsync(ct);
        var categories = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.NameAr, ct);
        var units = await db.Units.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.NameAr, ct);

        return products.Select(p =>
        {
            var rows = balances.Where(b => b.ProductId == p.Id).ToList();
            var quantity = rows.Sum(r => r.QuantityOnHand);
            var value = rows.Sum(r => r.QuantityOnHand * r.AverageCost);
            var average = quantity > 0 ? Math.Round(value / quantity, 4) : rows.Select(r => r.AverageCost).DefaultIfEmpty(0).Max();
            return new ProductDto(
                p.Id, p.TenantId, p.Sku, p.Barcode, p.NameAr, p.NameEn, p.CategoryId, categories.GetValueOrDefault(p.CategoryId, string.Empty), p.UnitId,
                units.GetValueOrDefault(p.UnitId, string.Empty), quantity, average, rows.Select(r => r.LastPurchaseCost).DefaultIfEmpty(0).Max(),
                p.StandardCost, p.SellingPrice, p.VatRate, p.MinStockLevel, quantity <= p.MinStockLevel, p.Notes, p.IsActive ? "active" : "inactive",
                includeWarehouses
                    ? rows.Select(r => new WarehouseStockDto(r.WarehouseId, r.Code, r.NameAr, r.QuantityOnHand, r.AverageCost, r.LastPurchaseCost, Math.Round(r.QuantityOnHand * r.AverageCost, 2))).ToList()
                    : null);
        }).ToList();
    }
}
