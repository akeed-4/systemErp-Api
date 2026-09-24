using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.Suppliers.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Suppliers.Persistence;

internal sealed class SuppliersDbContext(DbContextOptions<SuppliersDbContext> options, ModuleDbContextDependencies dependencies)
    : ModuleDbContext(options, dependencies)
{
    public const string SchemaName = "suppliers";

    public override string Schema => SchemaName;

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<SupplierBankDetail> BankDetails => Set<SupplierBankDetail>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Supplier>(b =>
        {
            b.ToTable("Suppliers");
            b.HasKey(s => s.Id);
            b.Property(s => s.Code).HasMaxLength(20).IsUnicode(false);
            b.Property(s => s.NameAr).HasMaxLength(200);
            b.Property(s => s.NameEn).HasMaxLength(200);
            b.Property(s => s.VatNumber).HasMaxLength(15).IsUnicode(false);
            b.Property(s => s.CrNumber).HasMaxLength(20).IsUnicode(false);
            b.Property(s => s.Phone).HasMaxLength(32);
            b.Property(s => s.Email).HasMaxLength(256);
            b.Property(s => s.ContactPerson).HasMaxLength(200);
            b.Property(s => s.City).HasMaxLength(100);
            b.Property(s => s.Address).HasMaxLength(400);
            b.Property(s => s.OpeningBalance).HasPrecision(18, 2);
            b.Property(s => s.CurrencyCode).HasMaxLength(3).IsUnicode(false);
            b.Property(s => s.Notes).HasMaxLength(1000);
            b.Property(s => s.RowVersion).IsRowVersion();
            b.Ignore(s => s.PrimaryBank);
            b.HasIndex(s => new { s.TenantId, s.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
            b.HasIndex(s => new { s.TenantId, s.VatNumber }).HasFilter("[VatNumber] IS NOT NULL");
            b.HasIndex(s => new { s.TenantId, s.AccountId }).IsUnique().HasFilter("[AccountId] IS NOT NULL");
            b.HasMany(s => s.BankDetails).WithOne().HasForeignKey(d => d.SupplierId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(s => s.BankDetails).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<SupplierBankDetail>(b =>
        {
            b.ToTable("SupplierBankDetails");
            b.HasKey(d => d.Id);
            b.Property(d => d.BankName).HasMaxLength(200);
            b.Property(d => d.Iban).HasMaxLength(34).IsUnicode(false);
            b.Property(d => d.SwiftCode).HasMaxLength(11).IsUnicode(false);
        });
    }
}

internal sealed class SuppliersDbContextDesignTimeFactory : ModuleDesignTimeFactory<SuppliersDbContext>
{
    protected override string Schema => SuppliersDbContext.SchemaName;

    protected override SuppliersDbContext Create(DbContextOptions<SuppliersDbContext> options, ModuleDbContextDependencies dependencies) =>
        new(options, dependencies);
}
