using Microsoft.EntityFrameworkCore;

namespace ERP.Service.Data;

/// <summary>مصدر بيان التسليم ومتابعة التوصيل والفوترة المرحلية (الهجرة <c>DeliveryNoteSourcesAndTracking</c>).</summary>
internal static class DeliveryTrackingModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeliveryNote>(e =>
        {
            e.Property(n => n.SourceType).HasMaxLength(20).IsRequired().HasDefaultValue(DeliveryNoteSources.Manual);
            e.Property(n => n.SourceNumber).HasMaxLength(100);
            e.Property(n => n.DeliveryStatus).HasMaxLength(24).IsRequired().HasDefaultValue(DeliveryTrackingStatuses.Legacy);
            e.Property(n => n.ReceiverName).HasMaxLength(200);
            e.Property(n => n.CancellationReason).HasMaxLength(500);
            // بيانات المصدر (حساب المتبقي بقفل المصدر) وقوائم المتابعة بحالة التوصيل
            e.HasIndex(n => new { n.TenantId, n.SourceType, n.SourceId });
            e.HasIndex(n => new { n.TenantId, n.DeliveryStatus, n.Date });
            // لقطة موقع التوصيل ومراجعته (الهجرة CustomerDeliveryLocations)
            e.Property(n => n.DeliveryAddress).HasMaxLength(500);
            e.Property(n => n.DeliveryLatitude).HasPrecision(9, 6);
            e.Property(n => n.DeliveryLongitude).HasPrecision(9, 6);
            e.Property(n => n.LocationReviewedByName).HasMaxLength(256);
            e.Property(n => n.LocationReviewNotes).HasMaxLength(500);
            e.HasIndex(n => new { n.TenantId, n.DeliveryLocationId });
        });
        modelBuilder.Entity<CustomerDeliveryLocation>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Address).HasMaxLength(500).IsRequired();
            e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.District).HasMaxLength(100);
            e.Property(x => x.Latitude).HasPrecision(9, 6);
            e.Property(x => x.Longitude).HasPrecision(9, 6);
            e.Property(x => x.ContactName).HasMaxLength(150);
            e.Property(x => x.ContactPhone).HasMaxLength(30);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.HasIndex(x => new { x.TenantId, x.CustomerId, x.IsActive });
            // موقع افتراضي فعال واحد لكل عميل
            e.HasIndex(x => new { x.TenantId, x.CustomerId }).IsUnique().HasFilter("[IsDefault] = 1 AND [IsActive] = 1");
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<DeliveryNoteItem>(e =>
        {
            e.Property(i => i.ConfirmedQty).HasPrecision(18, 4);
            e.Property(i => i.InvoicedQty).HasPrecision(18, 4);
            e.HasIndex(i => new { i.TenantId, i.SourceLineId });
        });
        modelBuilder.Entity<DeliveryNoteEvent>(e =>
        {
            e.Property(x => x.EventType).HasMaxLength(32).IsRequired();
            e.Property(x => x.FromStatus).HasMaxLength(32);
            e.Property(x => x.ToStatus).HasMaxLength(32);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.UserName).HasMaxLength(256);
            e.HasIndex(x => new { x.TenantId, x.DeliveryNoteId, x.OccurredAt });
            e.HasOne<DeliveryNote>().WithMany().HasForeignKey(x => x.DeliveryNoteId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<DeliveryNoteInvoiceLine>(e =>
        {
            e.Property(x => x.InvoiceNumber).HasMaxLength(100);
            e.Property(x => x.Quantity).HasPrecision(18, 4);
            // سطر البيان يظهر مرة واحدة في الفاتورة الواحدة
            e.HasIndex(x => new { x.TenantId, x.DeliveryNoteItemId, x.InvoiceId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.InvoiceId });
            e.HasIndex(x => new { x.TenantId, x.DeliveryNoteId });
            e.HasOne<DeliveryNote>().WithMany().HasForeignKey(x => x.DeliveryNoteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<DeliveryNoteItem>().WithMany().HasForeignKey(x => x.DeliveryNoteItemId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
