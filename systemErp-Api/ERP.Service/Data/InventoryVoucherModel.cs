using Microsoft.EntityFrameworkCore;

namespace ERP.Service.Data;

/// <summary>فواتير الصرف والاستلام المخزني ومصدر سند التكاليف الإضافية (الهجرة <c>InventoryIssueReceiptVouchers</c>).</summary>
internal static class InventoryVoucherModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InventoryVoucher>(e =>
        {
            e.Property(v => v.Kind).HasMaxLength(16).IsRequired();
            e.Property(v => v.VoucherNumber).HasMaxLength(50).IsRequired();
            e.Property(v => v.Status).HasMaxLength(16).IsRequired();
            e.Property(v => v.CounterAccountCode).HasMaxLength(50).IsRequired();
            e.Property(v => v.Reason).HasMaxLength(24).IsRequired();
            e.Property(v => v.Reference).HasMaxLength(100);
            e.Property(v => v.Notes).HasMaxLength(1000);
            e.Property(v => v.PostedBy).HasMaxLength(256);
            e.Property(v => v.CancelReason).HasMaxLength(500);
            e.Property(v => v.CancelledBy).HasMaxLength(256);
            e.Property(v => v.RowVersion).IsRowVersion();
            e.HasIndex(v => new { v.TenantId, v.Kind, v.Date });
            e.HasIndex(v => new { v.TenantId, v.Kind, v.Status });
            e.ToTable(t => t.HasCheckConstraint("CK_InventoryVouchers_Kind", "[Kind] IN ('issue', 'receipt')"));
        });
        modelBuilder.Entity<InventoryVoucherLine>(e =>
        {
            e.Property(l => l.Sku).HasMaxLength(100);
            e.Property(l => l.ItemName).HasMaxLength(300);
            e.Property(l => l.Unit).HasMaxLength(20);
            e.Property(l => l.UnitFactor).HasPrecision(18, 6).HasDefaultValue(1m);
            e.Property(l => l.Quantity).HasPrecision(18, 4);
            e.Property(l => l.UnitPrice).HasPrecision(18, 4);
            e.Property(l => l.UnitCost).HasPrecision(18, 4);
            e.Property(l => l.BatchNumber).HasMaxLength(200);
            e.Property(l => l.Notes).HasMaxLength(500);
            e.Property(l => l.CounterAccountCode).HasMaxLength(50);
            e.HasOne(l => l.Voucher).WithMany(v => v.Lines).HasForeignKey(l => l.VoucherId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(l => new { l.TenantId, l.ItemId });
            e.ToTable(t => t.HasCheckConstraint("CK_InventoryVoucherLines_Quantity", "[Quantity] > 0"));
        });
        // مصدر سند التكاليف الإضافية: فاتورة شراء (القائم) أو فاتورة استلام مخزني
        modelBuilder.Entity<PurchaseLandedCostInvoice>(e =>
        {
            e.Property(i => i.SourceType).HasMaxLength(24).IsRequired().HasDefaultValue(PurchaseLandedCostSources.PurchaseInvoice);
            e.HasIndex(i => new { i.TenantId, i.SourceType, i.InvoiceId });
        });
    }
}
