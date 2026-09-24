using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.Accounting.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Accounting.Persistence;

internal sealed class AccountingDbContext(DbContextOptions<AccountingDbContext> options, ModuleDbContextDependencies dependencies)
    : ModuleDbContext(options, dependencies)
{
    public const string SchemaName = "accounting";

    public override string Schema => SchemaName;

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<CostCenter> CostCenters => Set<CostCenter>();

    public DbSet<FiscalPeriod> FiscalPeriods => Set<FiscalPeriod>();

    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();

    public DbSet<JournalEntryLine> JournalEntryLines => Set<JournalEntryLine>();

    public DbSet<AccountBalance> AccountBalances => Set<AccountBalance>();

    public DbSet<PostingAccountMapping> PostingMappings => Set<PostingAccountMapping>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(b =>
        {
            b.ToTable("Accounts");
            b.HasKey(a => a.Id);
            b.Property(a => a.Code).HasMaxLength(30).IsUnicode(false);
            b.Property(a => a.NameAr).HasMaxLength(200);
            b.Property(a => a.NameEn).HasMaxLength(200);
            b.Property(a => a.CurrencyCode).HasMaxLength(3).IsUnicode(false);
            b.Property(a => a.LinkedEntityType).HasMaxLength(20).IsUnicode(false);
            b.Property(a => a.Notes).HasMaxLength(1000);
            b.HasIndex(a => new { a.TenantId, a.Code }).IsUnique();
            b.HasIndex(a => new { a.TenantId, a.LinkedEntityType, a.LinkedEntityId });
            b.HasOne<Account>().WithMany().HasForeignKey(a => a.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CostCenter>(b =>
        {
            b.ToTable("CostCenters");
            b.HasKey(c => c.Id);
            b.Property(c => c.Code).HasMaxLength(30).IsUnicode(false);
            b.Property(c => c.NameAr).HasMaxLength(200);
            b.Property(c => c.NameEn).HasMaxLength(200);
            b.Property(c => c.Description).HasMaxLength(1000);
            b.HasIndex(c => new { c.TenantId, c.Code }).IsUnique();
        });

        modelBuilder.Entity<FiscalPeriod>(b =>
        {
            b.ToTable("FiscalPeriods");
            b.HasKey(p => p.Id);
            b.HasIndex(p => new { p.TenantId, p.Year, p.Month }).IsUnique();
        });

        modelBuilder.Entity<JournalEntry>(b =>
        {
            b.ToTable("JournalEntries");
            b.HasKey(e => e.Id);
            b.Property(e => e.EntryNumber).HasMaxLength(40).IsUnicode(false);
            b.Property(e => e.Description).HasMaxLength(1000);
            b.Property(e => e.SourceModule).HasMaxLength(40).IsUnicode(false);
            b.Property(e => e.SourceDocumentType).HasMaxLength(60).IsUnicode(false);
            b.Property(e => e.SourceDocumentNumber).HasMaxLength(60);
            b.Property(e => e.PostingKind).HasMaxLength(40).IsUnicode(false);
            b.Property(e => e.TotalDebit).HasPrecision(18, 2);
            b.Property(e => e.TotalCredit).HasPrecision(18, 2);
            b.Property(e => e.RowVersion).IsRowVersion();
            b.HasIndex(e => new { e.TenantId, e.EntryNumber }).IsUnique().HasFilter("[EntryNumber] IS NOT NULL");
            b.HasIndex(e => new { e.TenantId, e.Date });

            // Idempotency: one live posting per (source document, kind).
            b.HasIndex(e => new { e.TenantId, e.SourceModule, e.SourceDocumentType, e.SourceDocumentId, e.PostingKind })
                .IsUnique()
                .HasFilter("[ReversalOfId] IS NULL AND [Status] = 'posted'")
                .HasDatabaseName("UX_JournalEntries_Source");
            b.HasMany(e => e.Lines).WithOne().HasForeignKey(l => l.JournalEntryId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(e => e.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<JournalEntryLine>(b =>
        {
            b.ToTable("JournalEntryLines", t => t.HasCheckConstraint(
                "CK_JournalEntryLines_Amounts",
                "[Debit] >= 0 AND [Credit] >= 0 AND ([Debit] = 0 OR [Credit] = 0) AND ([Debit] + [Credit]) > 0"));
            b.HasKey(l => l.Id);
            b.Property(l => l.Debit).HasPrecision(18, 2);
            b.Property(l => l.Credit).HasPrecision(18, 2);
            b.Property(l => l.Notes).HasMaxLength(500);
            b.HasIndex(l => new { l.TenantId, l.AccountId, l.JournalEntryId });
            b.HasIndex(l => new { l.TenantId, l.PartyType, l.PartyId }).HasFilter("[PartyId] IS NOT NULL");
            b.HasOne<Account>().WithMany().HasForeignKey(l => l.AccountId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<CostCenter>().WithMany().HasForeignKey(l => l.CostCenterId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AccountBalance>(b =>
        {
            b.ToTable("AccountBalances");
            b.HasKey(x => x.Id);
            b.Property(x => x.Debit).HasPrecision(18, 2);
            b.Property(x => x.Credit).HasPrecision(18, 2);
            b.HasIndex(x => new { x.TenantId, x.AccountId, x.FiscalPeriodId }).IsUnique();
            b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<FiscalPeriod>().WithMany().HasForeignKey(x => x.FiscalPeriodId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PostingAccountMapping>(b =>
        {
            b.ToTable("PostingAccountMappings");
            b.HasKey(m => m.Id);
            b.HasIndex(m => new { m.TenantId, m.Purpose }).IsUnique();
            b.HasOne<Account>().WithMany().HasForeignKey(m => m.AccountId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}

internal sealed class AccountingDbContextDesignTimeFactory : ModuleDesignTimeFactory<AccountingDbContext>
{
    protected override string Schema => AccountingDbContext.SchemaName;

    protected override AccountingDbContext Create(DbContextOptions<AccountingDbContext> options, ModuleDbContextDependencies dependencies) =>
        new(options, dependencies);
}
