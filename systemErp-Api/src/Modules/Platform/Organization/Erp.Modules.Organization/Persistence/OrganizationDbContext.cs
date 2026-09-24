using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.Organization.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Organization.Persistence;

internal sealed class OrganizationDbContext(DbContextOptions<OrganizationDbContext> options, ModuleDbContextDependencies dependencies)
    : ModuleDbContext(options, dependencies)
{
    public const string SchemaName = "org";

    public override string Schema => SchemaName;

    public DbSet<CompanyProfile> Companies => Set<CompanyProfile>();

    public DbSet<Branch> Branches => Set<Branch>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CompanyProfile>(b =>
        {
            b.ToTable("Tenants");
            b.HasKey(c => c.Id);
            b.HasIndex(c => c.TenantId).IsUnique();
            b.Property(c => c.NameAr).HasMaxLength(200);
            b.Property(c => c.NameEn).HasMaxLength(200);
            b.Property(c => c.VatNumber).HasMaxLength(15).IsUnicode(false);
            b.Property(c => c.CrNumber).HasMaxLength(20).IsUnicode(false);
            b.Property(c => c.Address).HasMaxLength(400);
            b.Property(c => c.City).HasMaxLength(100);
            b.Property(c => c.Country).HasMaxLength(100);
            b.Property(c => c.Phone).HasMaxLength(32);
            b.Property(c => c.Email).HasMaxLength(256);
            b.Property(c => c.BaseCurrencyCode).HasMaxLength(3).IsUnicode(false);
            b.Property(c => c.LogoUrl).HasMaxLength(1000);
            b.Property(c => c.Industry).HasMaxLength(100);
        });

        modelBuilder.Entity<Branch>(b =>
        {
            b.ToTable("Branches");
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(20).IsUnicode(false);
            b.Property(x => x.NameAr).HasMaxLength(200);
            b.Property(x => x.NameEn).HasMaxLength(200);
            b.Property(x => x.City).HasMaxLength(100);
            b.Property(x => x.Address).HasMaxLength(400);
            b.Property(x => x.Phone).HasMaxLength(32);
            b.Property(x => x.RowVersion).IsRowVersion();
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
            b.HasIndex(x => new { x.TenantId, x.Type });
        });
    }
}

internal sealed class OrganizationDbContextDesignTimeFactory : ModuleDesignTimeFactory<OrganizationDbContext>
{
    protected override string Schema => OrganizationDbContext.SchemaName;

    protected override OrganizationDbContext Create(DbContextOptions<OrganizationDbContext> options, ModuleDbContextDependencies dependencies) =>
        new(options, dependencies);
}
