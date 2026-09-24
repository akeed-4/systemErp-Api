using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.Banking.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Banking.Persistence;

internal sealed class BankingDbContext(DbContextOptions<BankingDbContext> options, ModuleDbContextDependencies dependencies)
    : ModuleDbContext(options, dependencies)
{
    public const string SchemaName = "banking";

    public override string Schema => SchemaName;

    public DbSet<Bank> Banks => Set<Bank>();

    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Bank>(b =>
        {
            b.ToTable("Banks");
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(20).IsUnicode(false);
            b.Property(x => x.NameAr).HasMaxLength(200);
            b.Property(x => x.NameEn).HasMaxLength(200);
            b.Property(x => x.SwiftCode).HasMaxLength(11).IsUnicode(false);
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });

        modelBuilder.Entity<BankAccount>(b =>
        {
            b.ToTable("BankAccounts");
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(20).IsUnicode(false);
            b.Property(x => x.NameAr).HasMaxLength(200);
            b.Property(x => x.NameEn).HasMaxLength(200);
            b.Property(x => x.AccountNumber).HasMaxLength(40).IsUnicode(false);
            b.Property(x => x.Iban).HasMaxLength(34).IsUnicode(false);
            b.Property(x => x.Branch).HasMaxLength(100);
            b.Property(x => x.CurrencyCode).HasMaxLength(3).IsUnicode(false);
            b.Property(x => x.OpeningBalance).HasPrecision(18, 2);
            b.Property(x => x.Notes).HasMaxLength(1000);
            b.Property(x => x.RowVersion).IsRowVersion();
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
            b.HasIndex(x => new { x.TenantId, x.Iban }).IsUnique().HasFilter("[Iban] IS NOT NULL AND [IsDeleted] = 0");
            b.HasIndex(x => new { x.TenantId, x.AccountId }).IsUnique().HasFilter("[AccountId] IS NOT NULL");
            b.HasOne<Bank>().WithMany().HasForeignKey(x => x.BankId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}

internal sealed class BankingDbContextDesignTimeFactory : ModuleDesignTimeFactory<BankingDbContext>
{
    protected override string Schema => BankingDbContext.SchemaName;

    protected override BankingDbContext Create(DbContextOptions<BankingDbContext> options, ModuleDbContextDependencies dependencies) =>
        new(options, dependencies);
}
