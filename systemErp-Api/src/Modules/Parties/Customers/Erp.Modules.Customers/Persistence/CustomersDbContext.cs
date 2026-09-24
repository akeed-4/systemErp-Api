using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.Customers.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Customers.Persistence;

internal sealed class CustomersDbContext(DbContextOptions<CustomersDbContext> options, ModuleDbContextDependencies dependencies)
    : ModuleDbContext(options, dependencies)
{
    public const string SchemaName = "customers";

    public override string Schema => SchemaName;

    public DbSet<Customer> Customers => Set<Customer>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(b =>
        {
            b.ToTable("Customers");
            b.HasKey(c => c.Id);
            b.Property(c => c.Code).HasMaxLength(20).IsUnicode(false);
            b.Property(c => c.NameAr).HasMaxLength(200);
            b.Property(c => c.NameEn).HasMaxLength(200);
            b.Property(c => c.VatNumber).HasMaxLength(15).IsUnicode(false);
            b.Property(c => c.CrNumber).HasMaxLength(20).IsUnicode(false);
            b.Property(c => c.NationalId).HasMaxLength(20).IsUnicode(false);
            b.Property(c => c.Phone).HasMaxLength(32);
            b.Property(c => c.AltPhone).HasMaxLength(32);
            b.Property(c => c.Email).HasMaxLength(256);
            b.Property(c => c.ContactPerson).HasMaxLength(200);
            b.Property(c => c.City).HasMaxLength(100);
            b.Property(c => c.District).HasMaxLength(100);
            b.Property(c => c.Street).HasMaxLength(200);
            b.Property(c => c.BuildingNo).HasMaxLength(10);
            b.Property(c => c.PostalCode).HasMaxLength(10);
            b.Property(c => c.AdditionalNo).HasMaxLength(10);
            b.Property(c => c.CreditLimit).HasPrecision(18, 2);
            b.Property(c => c.OpeningBalance).HasPrecision(18, 2);
            b.Property(c => c.CurrencyCode).HasMaxLength(3).IsUnicode(false);
            b.Property(c => c.Notes).HasMaxLength(1000);
            b.Property(c => c.RowVersion).IsRowVersion();
            b.HasIndex(c => new { c.TenantId, c.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
            b.HasIndex(c => new { c.TenantId, c.Phone });
            b.HasIndex(c => new { c.TenantId, c.VatNumber }).HasFilter("[VatNumber] IS NOT NULL");
            b.HasIndex(c => new { c.TenantId, c.NationalId }).HasFilter("[NationalId] IS NOT NULL");
            b.HasIndex(c => new { c.TenantId, c.AccountId }).IsUnique().HasFilter("[AccountId] IS NOT NULL");
        });
    }
}

internal sealed class CustomersDbContextDesignTimeFactory : ModuleDesignTimeFactory<CustomersDbContext>
{
    protected override string Schema => CustomersDbContext.SchemaName;

    protected override CustomersDbContext Create(DbContextOptions<CustomersDbContext> options, ModuleDbContextDependencies dependencies) =>
        new(options, dependencies);
}
