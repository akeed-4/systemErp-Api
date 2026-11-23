using Microsoft.EntityFrameworkCore;

namespace ERP.Service.Data;

/// <summary>إعداد جداول حجز المخزون للمشاريع (الهجرة <c>ProjectStockReservations</c>).</summary>
internal static class StockReservationModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StockReservation>(e =>
        {
            e.Property(r => r.Quantity).HasPrecision(18, 4);
            e.Property(r => r.IssuedQuantity).HasPrecision(18, 4);
            e.Property(r => r.ReleasedQuantity).HasPrecision(18, 4);
            e.Property(r => r.Status).HasMaxLength(16).IsRequired();
            e.Property(r => r.Source).HasMaxLength(16).IsRequired();
            e.Property(r => r.CreatedBy).HasMaxLength(256);
            e.Property(r => r.Notes).HasMaxLength(500);
            e.Property(r => r.RowVersion).IsRowVersion();
            // المحجوز النشط لكل صنف في موقع (فحص المتاح) ولكل مشروع
            e.HasIndex(r => new { r.TenantId, r.ProductId, r.WarehouseId, r.Status });
            e.HasIndex(r => new { r.TenantId, r.ContractId });
            e.HasOne<CommercialContract>().WithMany().HasForeignKey(r => r.ContractId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Product>().WithMany().HasForeignKey(r => r.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Warehouse>().WithMany().HasForeignKey(r => r.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<StockReservationConsumption>(e =>
        {
            e.Property(c => c.Quantity).HasPrecision(18, 4);
            e.HasIndex(c => new { c.TenantId, c.IssueId });
            e.HasIndex(c => new { c.TenantId, c.ReservationId });
            e.HasOne<StockReservation>().WithMany().HasForeignKey(c => c.ReservationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProjectMaterialIssue>().WithMany().HasForeignKey(c => c.IssueId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<StockReservationTransfer>(e =>
        {
            e.Property(t => t.Quantity).HasPrecision(18, 4);
            e.Property(t => t.Reason).HasMaxLength(500).IsRequired();
            e.Property(t => t.Status).HasMaxLength(16).IsRequired();
            e.Property(t => t.RequestedBy).HasMaxLength(256);
            e.Property(t => t.DecidedBy).HasMaxLength(256);
            e.Property(t => t.DecisionNote).HasMaxLength(500);
            e.HasIndex(t => new { t.TenantId, t.Status });
            e.HasIndex(t => new { t.TenantId, t.SourceContractId });
            e.HasIndex(t => new { t.TenantId, t.TargetContractId });
            e.HasOne<StockReservation>().WithMany().HasForeignKey(t => t.SourceReservationId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
