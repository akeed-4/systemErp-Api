using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using ERP.Domain.Common;

namespace ERP.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    private readonly ITenantService _tenantService;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ITenantService tenantService) : base(options)
    {
        _tenantService = tenantService;
    }

    public DbSet<Account> Accounts { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<BankEntity> BankEntities { get; set; }
    public DbSet<CarAgent> CarAgents { get; set; }
    public DbSet<CarBrand> CarBrands { get; set; }
    public DbSet<CarModel> CarModels { get; set; }
    public DbSet<CarProcurementOrder> CarProcurementOrders { get; set; }
    public DbSet<CarSalesContract> CarSalesContracts { get; set; }
    public DbSet<CarTrim> CarTrims { get; set; }
    public DbSet<CostCenter> CostCenters { get; set; }
    public DbSet<Currency> Currencies { get; set; }
    public DbSet<Customer> Customers { get; set; }
    public DbSet<FixedAsset> FixedAssets { get; set; }
    public DbSet<Invoice> Invoices { get; set; }
    public DbSet<InvoiceItem> InvoiceItems { get; set; }
    public DbSet<JournalEntry> JournalEntries { get; set; }
    public DbSet<JournalEntryLine> JournalEntryLines { get; set; }
    public DbSet<MaterialRequisition> MaterialRequisitions { get; set; }
    public DbSet<MaterialRequisitionItem> MaterialRequisitionItems { get; set; }
    public DbSet<PaymentMethodItem> PaymentMethodItems { get; set; }
    public DbSet<Product> Products { get; set; }
    public DbSet<ProductCategory> ProductCategories { get; set; }
    public DbSet<Quotation> Quotations { get; set; }
    public DbSet<QuotationItem> QuotationItems { get; set; }
    public DbSet<StockMovement> StockMovements { get; set; }
    public DbSet<Subscription> Subscriptions { get; set; }
    public DbSet<Supplier> Suppliers { get; set; }
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<UnitOfMeasure> UnitsOfMeasure { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Vehicle> Vehicles { get; set; }
    public DbSet<Voucher> Vouchers { get; set; }
    public DbSet<Warehouse> Warehouses { get; set; }
    public DbSet<AppNotification> Notifications { get; set; }
    public DbSet<ApprovalRequest> ApprovalRequests { get; set; }
    public DbSet<ApprovalHistoryItem> ApprovalHistoryItems { get; set; }
    public DbSet<CommercialOrder> CommercialOrders { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Tenant>().OwnsOne(t => t.ZatcaConfig);
        
        modelBuilder.Entity<Invoice>().HasMany(e => e.Items).WithOne(i => i.Invoice).HasForeignKey(i => i.InvoiceId);
        modelBuilder.Entity<JournalEntry>().HasMany(e => e.Lines).WithOne(l => l.JournalEntry).HasForeignKey(l => l.JournalEntryId);
        modelBuilder.Entity<Quotation>().HasMany(e => e.Items).WithOne(i => i.Quotation).HasForeignKey(i => i.QuotationId);
        modelBuilder.Entity<MaterialRequisition>().HasMany(e => e.Items).WithOne(i => i.Requisition).HasForeignKey(i => i.RequisitionId);
        modelBuilder.Entity<CommercialOrder>().HasMany(e => e.Items).WithOne().HasForeignKey("CommercialOrderId");
        modelBuilder.Entity<ApprovalRequest>().HasMany(e => e.History).WithOne(h => h.ApprovalRequest).HasForeignKey(h => h.ApprovalRequestId);

        modelBuilder.Entity<CarProcurementOrder>().HasMany(e => e.Items).WithOne(i => i.Order).HasForeignKey(i => i.OrderId);

        // Multi-tenancy Global Filters
        var tenantId = _tenantService.CurrentTenantId;
        if (tenantId.HasValue)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
                {
                    modelBuilder.Entity(entityType.ClrType).HasQueryFilter(ConvertFilterExpression(entityType.ClrType, tenantId.Value));
                }
            }
        }

        // Additional configurations for specific entities if needed
        modelBuilder.Entity<Account>().Property(a => a.Balance).HasPrecision(18, 2);
        modelBuilder.Entity<Invoice>().Property(i => i.GrandTotal).HasPrecision(18, 2);
        modelBuilder.Entity<JournalEntryLine>().Property(l => l.Debit).HasPrecision(18, 2);
        modelBuilder.Entity<JournalEntryLine>().Property(l => l.Credit).HasPrecision(18, 2);
    }

    private static System.Linq.Expressions.LambdaExpression ConvertFilterExpression(Type type, Guid tenantId)
    {
        var parameter = System.Linq.Expressions.Expression.Parameter(type, "e");
        var property = System.Linq.Expressions.Expression.Property(parameter, "TenantId");
        var constant = System.Linq.Expressions.Expression.Constant(tenantId);
        var body = System.Linq.Expressions.Expression.Equal(property, constant);
        return System.Linq.Expressions.Expression.Lambda(body, parameter);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantService.CurrentTenantId;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (tenantId.HasValue)
                    {
                        entry.Entity.TenantId = tenantId.Value;
                    }
                    entry.Entity.CreatedAt = DateTime.UtcNow;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    break;
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
