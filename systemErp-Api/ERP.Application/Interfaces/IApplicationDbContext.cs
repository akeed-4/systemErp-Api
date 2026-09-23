using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Account> Accounts { get; set; }
    DbSet<AuditLog> AuditLogs { get; set; }
    DbSet<BankEntity> BankEntities { get; set; }
    DbSet<CarAgent> CarAgents { get; set; }
    DbSet<CarBrand> CarBrands { get; set; }
    DbSet<CarModel> CarModels { get; set; }
    DbSet<CarProcurementOrder> CarProcurementOrders { get; set; }
    DbSet<CarSalesContract> CarSalesContracts { get; set; }
    DbSet<CarTrim> CarTrims { get; set; }
    DbSet<CostCenter> CostCenters { get; set; }
    DbSet<Currency> Currencies { get; set; }
    DbSet<Customer> Customers { get; set; }
    DbSet<FixedAsset> FixedAssets { get; set; }
    DbSet<Invoice> Invoices { get; set; }
    DbSet<InvoiceItem> InvoiceItems { get; set; }
    DbSet<JournalEntry> JournalEntries { get; set; }
    DbSet<JournalEntryLine> JournalEntryLines { get; set; }
    DbSet<MaterialRequisition> MaterialRequisitions { get; set; }
    DbSet<MaterialRequisitionItem> MaterialRequisitionItems { get; set; }
    DbSet<PaymentMethodItem> PaymentMethodItems { get; set; }
    DbSet<Product> Products { get; set; }
    DbSet<ProductCategory> ProductCategories { get; set; }
    DbSet<Quotation> Quotations { get; set; }
    DbSet<QuotationItem> QuotationItems { get; set; }
    DbSet<StockMovement> StockMovements { get; set; }
    DbSet<Subscription> Subscriptions { get; set; }
    DbSet<Supplier> Suppliers { get; set; }
    DbSet<Tenant> Tenants { get; set; }
    DbSet<UnitOfMeasure> UnitsOfMeasure { get; set; }
    DbSet<User> Users { get; set; }
    DbSet<Vehicle> Vehicles { get; set; }
    DbSet<Voucher> Vouchers { get; set; }
    DbSet<Warehouse> Warehouses { get; set; }
    DbSet<AppNotification> Notifications { get; set; }
    DbSet<ApprovalRequest> ApprovalRequests { get; set; }
    DbSet<ApprovalHistoryItem> ApprovalHistoryItems { get; set; }
    DbSet<CommercialOrder> CommercialOrders { get; set; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
