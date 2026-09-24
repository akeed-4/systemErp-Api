using Erp.BuildingBlocks.Infrastructure.Modules;
using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Inventory.Domain;
using Erp.Modules.Inventory.Persistence;
using Erp.SharedKernel.Errors;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Inventory.Application;

internal sealed class InventorySeeder(InventoryDbContext db, TimeProvider clock) : IModuleSeeder
{
    public int Order => 100;

    public async Task SeedTenantAsync(TenantSeedContext context, CancellationToken cancellationToken)
    {
        if (!await db.Units.AnyAsync(cancellationToken))
        {
            (string Code, string Ar, string En, string Symbol)[] units =
            [
                ("PCS", "قطعة", "Piece", "قطعة"),
                ("BOX", "كرتون", "Box", "كرتون"),
                ("SET", "طقم", "Set", "طقم"),
                ("KG", "كيلوجرام", "Kilogram", "كجم"),
                ("L", "لتر", "Litre", "لتر"),
                ("M", "متر", "Metre", "م"),
                ("SRV", "خدمة", "Service", "خدمة"),
            ];
            foreach (var u in units)
            {
                var unit = new UnitOfMeasure(u.Code);
                unit.Update(u.Ar, u.En, u.Symbol, null, 1, true);
                db.Units.Add(unit);
            }
        }

        if (!await db.Categories.AnyAsync(cancellationToken))
        {
            var general = new ProductCategory("GEN");
            general.Update("أصناف عامة", "General Items", null, "التصنيف الافتراضي");
            db.Categories.Add(general);
        }

        if (!await db.Warehouses.AnyAsync(cancellationToken))
        {
            var main = new Warehouse(Warehouse.DefaultCode);
            main.Update("المستودع الرئيسي", "Main Warehouse", context.Company.City, null, null, context.Company.Phone, true);
            main.SetDefault(true);
            db.Warehouses.Add(main);
        }

        if (!await db.CostingPolicies.AnyAsync(cancellationToken))
        {
            var policy = new CostingPolicy(context.TenantId);
            policy.Update(CostingMethod.MovingAverage, true, true, NegativeInventoryPolicy.Prohibit, null, null, clock.GetUtcNow());
            db.CostingPolicies.Add(policy);
        }
    }
}

internal sealed class ProductCatalog(InventoryDbContext db) : IProductCatalog
{
    public async Task<ProductSummary?> FindAsync(Guid productId, CancellationToken cancellationToken) =>
        (await FindManyAsync([productId], cancellationToken)).GetValueOrDefault(productId);

    public async Task<IReadOnlyDictionary<Guid, ProductSummary>> FindManyAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken) =>
        await Summaries(db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))).ToDictionaryAsync(p => p.Id, cancellationToken);

    public Task<ProductSummary?> FindByBarcodeAsync(string barcodeOrSku, CancellationToken cancellationToken)
    {
        var code = barcodeOrSku.Trim();
        return Summaries(db.Products.AsNoTracking().Where(p => p.Barcode == code || p.Sku == code.ToUpper())).FirstOrDefaultAsync(cancellationToken);
    }

    private IQueryable<ProductSummary> Summaries(IQueryable<Product> products) =>
        from p in products
        join u in db.Units on p.UnitId equals u.Id
        select new ProductSummary(p.Id, p.Sku, p.Barcode, p.NameAr, p.NameEn, p.CategoryId, p.UnitId, u.Code, u.NameAr, p.SellingPrice, p.VatRate, p.IsActive);
}

internal sealed class WarehouseDirectory(InventoryDbContext db) : IWarehouseDirectory
{
    public Task<WarehouseSummary?> FindAsync(Guid warehouseId, CancellationToken cancellationToken) =>
        db.Warehouses.AsNoTracking().Where(w => w.Id == warehouseId)
            .Select(w => new WarehouseSummary(w.Id, w.Code, w.NameAr, w.NameEn, w.BranchId, w.IsDefault, w.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<WarehouseSummary> GetDefaultAsync(CancellationToken cancellationToken) =>
        await db.Warehouses.AsNoTracking().Where(w => w.IsDefault)
            .Select(w => new WarehouseSummary(w.Id, w.Code, w.NameAr, w.NameEn, w.BranchId, w.IsDefault, w.IsActive))
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw ErpException.Conflict("no_default_warehouse", "No default warehouse is configured.", "لا يوجد مستودع افتراضي.");
}
