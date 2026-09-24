using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.Inventory.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Inventory.Persistence;

internal sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options, ModuleDbContextDependencies dependencies)
    : ModuleDbContext(options, dependencies)
{
    public const string SchemaName = "inventory";

    public override string Schema => SchemaName;

    public DbSet<ProductCategory> Categories => Set<ProductCategory>();

    public DbSet<UnitOfMeasure> Units => Set<UnitOfMeasure>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    public DbSet<StockBalance> StockBalances => Set<StockBalance>();

    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    public DbSet<CostingPolicy> CostingPolicies => Set<CostingPolicy>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProductCategory>(b =>
        {
            b.ToTable("ProductCategories");
            b.HasKey(c => c.Id);
            b.Property(c => c.Code).HasMaxLength(30).IsUnicode(false);
            b.Property(c => c.NameAr).HasMaxLength(200);
            b.Property(c => c.NameEn).HasMaxLength(200);
            b.Property(c => c.Description).HasMaxLength(500);
            b.HasIndex(c => new { c.TenantId, c.Code }).IsUnique();
            b.HasOne<ProductCategory>().WithMany().HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UnitOfMeasure>(b =>
        {
            b.ToTable("UnitsOfMeasure");
            b.HasKey(u => u.Id);
            b.Property(u => u.Code).HasMaxLength(20).IsUnicode(false);
            b.Property(u => u.NameAr).HasMaxLength(100);
            b.Property(u => u.NameEn).HasMaxLength(100);
            b.Property(u => u.Symbol).HasMaxLength(20);
            b.Property(u => u.ConversionFactor).HasPrecision(18, 6);
            b.HasIndex(u => new { u.TenantId, u.Code }).IsUnique();
            b.HasOne<UnitOfMeasure>().WithMany().HasForeignKey(u => u.BaseUnitId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Product>(b =>
        {
            b.ToTable("Products");
            b.HasKey(p => p.Id);
            b.Property(p => p.Sku).HasMaxLength(40).IsUnicode(false);
            b.Property(p => p.Barcode).HasMaxLength(64).IsUnicode(false);
            b.Property(p => p.NameAr).HasMaxLength(300);
            b.Property(p => p.NameEn).HasMaxLength(300);
            b.Property(p => p.SellingPrice).HasPrecision(18, 2);
            b.Property(p => p.VatRate).HasPrecision(9, 4);
            b.Property(p => p.MinStockLevel).HasPrecision(18, 3);
            b.Property(p => p.StandardCost).HasPrecision(18, 4);
            b.Property(p => p.Notes).HasMaxLength(1000);
            b.Property(p => p.RowVersion).IsRowVersion();
            b.HasIndex(p => new { p.TenantId, p.Sku }).IsUnique().HasFilter("[IsDeleted] = 0");
            b.HasIndex(p => new { p.TenantId, p.Barcode }).IsUnique().HasFilter("[Barcode] IS NOT NULL AND [IsDeleted] = 0");
            b.HasOne<ProductCategory>().WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UnitOfMeasure>().WithMany().HasForeignKey(p => p.UnitId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Warehouse>(b =>
        {
            b.ToTable("Warehouses");
            b.HasKey(w => w.Id);
            b.Property(w => w.Code).HasMaxLength(20).IsUnicode(false);
            b.Property(w => w.NameAr).HasMaxLength(200);
            b.Property(w => w.NameEn).HasMaxLength(200);
            b.Property(w => w.Location).HasMaxLength(300);
            b.Property(w => w.ManagerName).HasMaxLength(200);
            b.Property(w => w.Phone).HasMaxLength(32);
            b.HasIndex(w => new { w.TenantId, w.Code }).IsUnique();
            b.HasIndex(w => new { w.TenantId, w.IsDefault }).IsUnique().HasFilter("[IsDefault] = 1");
        });

        modelBuilder.Entity<StockBalance>(b =>
        {
            b.ToTable("StockBalances");
            b.HasKey(s => s.Id);
            b.Property(s => s.QuantityOnHand).HasPrecision(18, 3);
            b.Property(s => s.AverageCost).HasPrecision(18, 4);
            b.Property(s => s.LastPurchaseCost).HasPrecision(18, 4);
            b.Property(s => s.RowVersion).IsRowVersion();
            b.HasIndex(s => new { s.TenantId, s.ProductId, s.WarehouseId }).IsUnique();
            b.HasOne<Product>().WithMany().HasForeignKey(s => s.ProductId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Warehouse>().WithMany().HasForeignKey(s => s.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockMovement>(b =>
        {
            b.ToTable("StockMovements");
            b.HasKey(m => m.Id);
            b.Property(m => m.Quantity).HasPrecision(18, 3);
            b.Property(m => m.UnitCost).HasPrecision(18, 4);
            b.Property(m => m.UnitPrice).HasPrecision(18, 2);
            b.Property(m => m.BalanceAfter).HasPrecision(18, 3);
            b.Property(m => m.AverageCostAfter).HasPrecision(18, 4);
            b.Property(m => m.SourceModule).HasMaxLength(40).IsUnicode(false);
            b.Property(m => m.SourceDocumentType).HasMaxLength(60).IsUnicode(false);
            b.Property(m => m.SourceNumber).HasMaxLength(60);
            b.Property(m => m.Notes).HasMaxLength(500);
            b.Ignore(m => m.IsInbound);
            b.HasIndex(m => new { m.TenantId, m.ProductId, m.WarehouseId, m.Date });
            b.HasIndex(m => new { m.TenantId, m.SourceModule, m.SourceDocumentType, m.SourceDocumentId });
            b.HasOne<Product>().WithMany().HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Warehouse>().WithMany().HasForeignKey(m => m.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CostingPolicy>(b =>
        {
            b.ToTable("CostingPolicies");
            b.HasKey(p => p.Id);
            b.HasIndex(p => p.TenantId).IsUnique();
        });
    }
}

internal sealed class InventoryDbContextDesignTimeFactory : ModuleDesignTimeFactory<InventoryDbContext>
{
    protected override string Schema => InventoryDbContext.SchemaName;

    protected override InventoryDbContext Create(DbContextOptions<InventoryDbContext> options, ModuleDbContextDependencies dependencies) =>
        new(options, dependencies);
}
