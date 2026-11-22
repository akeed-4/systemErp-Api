using Microsoft.EntityFrameworkCore;

namespace ERP.Service.Data;

/// <summary>إعداد دورة الشراء: طلب عروض الأسعار وروابط العروض وأوامر الشراء وسندات الاستلام بالمعرّفات (الهجرة <c>PurchaseCycleRfq</c>).</summary>
internal static class PurchaseCycleModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PurchaseRfq>(e =>
        {
            e.Property(r => r.RfqNumber).HasMaxLength(64).IsRequired();
            e.Property(r => r.Status).HasMaxLength(16).IsRequired();
            e.Property(r => r.Notes).HasMaxLength(500);
            e.Property(r => r.AwardedBy).HasMaxLength(256);
            e.HasIndex(r => new { r.TenantId, r.RfqNumber }).IsUnique();
            e.HasIndex(r => new { r.TenantId, r.RequisitionId, r.Status });
            e.HasOne<MaterialRequisition>().WithMany().HasForeignKey(r => r.RequisitionId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(r => r.Items).WithOne(i => i.Rfq).HasForeignKey(i => i.RfqId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(r => r.Suppliers).WithOne(s => s.Rfq).HasForeignKey(s => s.RfqId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<PurchaseRfqItem>(e =>
        {
            e.Property(i => i.ItemName).HasMaxLength(300);
            e.Property(i => i.Sku).HasMaxLength(100);
            e.Property(i => i.Unit).HasMaxLength(50);
            e.HasOne<Product>().WithMany().HasForeignKey(i => i.ItemId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PurchaseRfqSupplier>(e =>
        {
            e.Property(s => s.SupplierName).HasMaxLength(300);
            e.HasIndex(s => new { s.TenantId, s.RfqId, s.SupplierId }).IsUnique();
            e.HasOne<Supplier>().WithMany().HasForeignKey(s => s.SupplierId).OnDelete(DeleteBehavior.Restrict);
        });

        // العرض ← طلب العروض وسطره
        modelBuilder.Entity<Quotation>(e =>
        {
            e.HasIndex(q => new { q.TenantId, q.RfqId });
            e.HasOne<PurchaseRfq>().WithMany().HasForeignKey(q => q.RfqId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<QuotationItem>(e =>
            e.HasOne<PurchaseRfqItem>().WithMany().HasForeignKey(i => i.RfqItemId).OnDelete(DeleteBehavior.Restrict));

        // أمر الشراء ← طلب العروض والعرض المرسّى وطلب الشراء
        modelBuilder.Entity<CommercialOrder>(e =>
        {
            e.HasIndex(o => new { o.TenantId, o.RfqId });
            e.HasIndex(o => new { o.TenantId, o.RequisitionId });
            e.HasOne<PurchaseRfq>().WithMany().HasForeignKey(o => o.RfqId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Quotation>().WithMany().HasForeignKey(o => o.QuotationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<MaterialRequisition>().WithMany().HasForeignKey(o => o.RequisitionId).OnDelete(DeleteBehavior.Restrict);
        });

        // سند الاستلام ← أمر الشراء (الاستلام الجزئي)
        modelBuilder.Entity<DeliveryNote>(e =>
        {
            e.HasIndex(n => new { n.TenantId, n.PurchaseOrderId });
            e.HasOne<CommercialOrder>().WithMany().HasForeignKey(n => n.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
