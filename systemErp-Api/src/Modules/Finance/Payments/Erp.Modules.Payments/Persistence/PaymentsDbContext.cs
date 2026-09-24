using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.Payments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Payments.Persistence;

internal sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options, ModuleDbContextDependencies dependencies)
    : ModuleDbContext(options, dependencies)
{
    public const string SchemaName = "payments";

    public override string Schema => SchemaName;

    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();

    public DbSet<Voucher> Vouchers => Set<Voucher>();

    public DbSet<VoucherPayment> VoucherPayments => Set<VoucherPayment>();

    public DbSet<VoucherAllocation> VoucherAllocations => Set<VoucherAllocation>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaymentMethod>(b =>
        {
            b.ToTable("PaymentMethods");
            b.HasKey(m => m.Id);
            b.Property(m => m.Code).HasMaxLength(30).IsUnicode(false);
            b.Property(m => m.NameAr).HasMaxLength(100);
            b.Property(m => m.NameEn).HasMaxLength(100);
            b.Property(m => m.Icon).HasMaxLength(60).IsUnicode(false);
            b.Property(m => m.CommissionPercent).HasPrecision(9, 4);
            b.HasIndex(m => new { m.TenantId, m.Code }).IsUnique();
        });

        modelBuilder.Entity<Voucher>(b =>
        {
            b.ToTable("Vouchers");
            b.HasKey(v => v.Id);
            b.Property(v => v.VoucherNumber).HasMaxLength(40).IsUnicode(false);
            b.Property(v => v.Amount).HasPrecision(18, 2);
            b.Property(v => v.AmountInWordsAr).HasMaxLength(500);
            b.Property(v => v.PartyName).HasMaxLength(200);
            b.Property(v => v.ReferenceNumber).HasMaxLength(60);
            b.Property(v => v.Notes).HasMaxLength(1000);
            b.Property(v => v.ReceivedOrPaidBy).HasMaxLength(200);
            b.Property(v => v.CancellationReason).HasMaxLength(500);
            b.Property(v => v.RowVersion).IsRowVersion();
            b.HasIndex(v => new { v.TenantId, v.VoucherNumber }).IsUnique().HasFilter("[VoucherNumber] IS NOT NULL");
            b.HasIndex(v => new { v.TenantId, v.Date });
            b.HasIndex(v => new { v.TenantId, v.PartyType, v.PartyId });
            b.HasMany(v => v.Payments).WithOne().HasForeignKey(p => p.VoucherId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(v => v.Allocations).WithOne().HasForeignKey(a => a.VoucherId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(v => v.Payments).UsePropertyAccessMode(PropertyAccessMode.Field);
            b.Navigation(v => v.Allocations).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<VoucherPayment>(b =>
        {
            b.ToTable("VoucherPayments");
            b.HasKey(p => p.Id);
            b.Property(p => p.Amount).HasPrecision(18, 2);
            b.Property(p => p.Reference).HasMaxLength(100);
            b.HasOne<PaymentMethod>().WithMany().HasForeignKey(p => p.PaymentMethodId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<VoucherAllocation>(b =>
        {
            b.ToTable("VoucherAllocations");
            b.HasKey(a => a.Id);
            b.Property(a => a.TargetModule).HasMaxLength(40).IsUnicode(false);
            b.Property(a => a.TargetDocumentType).HasMaxLength(60).IsUnicode(false);
            b.Property(a => a.TargetDocumentNumber).HasMaxLength(60);
            b.Property(a => a.Amount).HasPrecision(18, 2);
            b.HasIndex(a => new { a.TenantId, a.TargetModule, a.TargetDocumentType, a.TargetDocumentId });
        });
    }
}

internal sealed class PaymentsDbContextDesignTimeFactory : ModuleDesignTimeFactory<PaymentsDbContext>
{
    protected override string Schema => PaymentsDbContext.SchemaName;

    protected override PaymentsDbContext Create(DbContextOptions<PaymentsDbContext> options, ModuleDbContextDependencies dependencies) =>
        new(options, dependencies);
}
