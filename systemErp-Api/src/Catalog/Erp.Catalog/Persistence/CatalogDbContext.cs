using Erp.BuildingBlocks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Erp.Catalog.Persistence;

/// <summary>
/// The catalog database: platform data that must be readable before a tenant is known
/// (tenants, login index, plans, subscriptions, master reference data). Never tenant-filtered.
/// </summary>
internal sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public const string Schema = "catalog";

    public DbSet<CatalogTenant> Tenants => Set<CatalogTenant>();

    public DbSet<TenantLoginIndexEntry> LoginIndex => Set<TenantLoginIndexEntry>();

    public DbSet<SubscriptionPlan> Plans => Set<SubscriptionPlan>();

    public DbSet<TenantSubscription> Subscriptions => Set<TenantSubscription>();

    public DbSet<CatalogRole> Roles => Set<CatalogRole>();

    public DbSet<CatalogScreen> Screens => Set<CatalogScreen>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<CatalogTenant>(b =>
        {
            b.ToTable("Tenants");
            b.HasKey(t => t.Id);
            b.Property(t => t.Code).HasMaxLength(40).IsUnicode(false);
            b.HasIndex(t => t.Code).IsUnique();
            b.Property(t => t.NameAr).HasMaxLength(200);
            b.Property(t => t.NameEn).HasMaxLength(200);
            b.Property(t => t.ConnectionStringEncrypted).HasMaxLength(4000).IsUnicode(false);
            b.Property(t => t.DatabaseName).HasMaxLength(128);
            b.Property(t => t.SchemaVersion).HasMaxLength(2000).IsUnicode(false);
            b.HasIndex(t => t.TenancyMode);
        });

        modelBuilder.Entity<TenantLoginIndexEntry>(b =>
        {
            b.ToTable("TenantLoginIndex");
            b.HasKey(e => e.Id);
            b.Property(e => e.NormalizedEmail).HasMaxLength(256);
            b.Property(e => e.NormalizedPhone).HasMaxLength(32).IsUnicode(false);
            b.HasIndex(e => new { e.TenantId, e.UserId }).IsUnique();
            b.HasIndex(e => e.UserId);
            b.HasIndex(e => e.NormalizedEmail).HasFilter("[NormalizedEmail] IS NOT NULL");
            b.HasIndex(e => e.NormalizedPhone).HasFilter("[NormalizedPhone] IS NOT NULL");
            b.HasOne<CatalogTenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SubscriptionPlan>(b =>
        {
            b.ToTable("SubscriptionPlans");
            b.HasKey(p => p.Code);
            b.Property(p => p.Code).HasMaxLength(40).IsUnicode(false);
            b.Property(p => p.NameAr).HasMaxLength(200);
            b.Property(p => p.NameEn).HasMaxLength(200);
            b.Property(p => p.DescriptionAr).HasMaxLength(1000);
            b.Property(p => p.DescriptionEn).HasMaxLength(1000);
            b.Property(p => p.BadgeAr).HasMaxLength(100);
            b.Property(p => p.BadgeEn).HasMaxLength(100);
            b.Property(p => p.PriceMonthly).HasPrecision(18, 2);
            b.Property(p => p.PriceYearly).HasPrecision(18, 2);
            b.HasMany(p => p.Features).WithOne().HasForeignKey(f => f.PlanCode).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SubscriptionPlanFeature>(b =>
        {
            b.ToTable("SubscriptionPlanFeatures");
            b.HasKey(f => f.Id);
            b.Property(f => f.PlanCode).HasMaxLength(40).IsUnicode(false);
            b.Property(f => f.FeatureAr).HasMaxLength(500);
            b.Property(f => f.FeatureEn).HasMaxLength(500);
        });

        modelBuilder.Entity<TenantSubscription>(b =>
        {
            b.ToTable("TenantSubscriptions");
            b.HasKey(s => s.Id);
            b.Property(s => s.PlanCode).HasMaxLength(40).IsUnicode(false);
            b.Property(s => s.BillingCycle).HasMaxLength(20).IsUnicode(false);
            b.Property(s => s.Status).HasMaxLength(20).IsUnicode(false);
            b.Property(s => s.PaymentMethod).HasMaxLength(40).IsUnicode(false);
            b.Property(s => s.TransactionReference).HasMaxLength(100);
            b.Property(s => s.PaidAmount).HasPrecision(18, 2);
            b.HasOne<CatalogTenant>().WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<SubscriptionPlan>().WithMany().HasForeignKey(s => s.PlanCode).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(s => s.TenantId).IsUnique().HasFilter("[Status] IN ('active','trial')");
        });

        modelBuilder.Entity<CatalogRole>(b =>
        {
            b.ToTable("Roles");
            b.HasKey(r => r.Code);
            b.Property(r => r.Code).HasMaxLength(40).IsUnicode(false);
            b.Property(r => r.NameAr).HasMaxLength(100);
            b.Property(r => r.NameEn).HasMaxLength(100);
        });

        modelBuilder.Entity<CatalogScreen>(b =>
        {
            b.ToTable("Screens");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).HasMaxLength(60).IsUnicode(false);
            b.Property(s => s.NameAr).HasMaxLength(200);
            b.Property(s => s.NameEn).HasMaxLength(200);
        });

        modelBuilder.UseSnakeCaseEnums();
    }
}

internal sealed class CatalogDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args) => new(DesignTimeOptions.For<CatalogDbContext>(CatalogDbContext.Schema));
}
