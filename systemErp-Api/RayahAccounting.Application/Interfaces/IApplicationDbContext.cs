using Microsoft.EntityFrameworkCore;
using RayahAccounting.Domain.Entities;

namespace RayahAccounting.Application.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<Account> Accounts { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<InvoiceItem> InvoiceItems { get; }
    DbSet<Voucher> Vouchers { get; }
    DbSet<Product> Products { get; }
    DbSet<Customer> Customers { get; }
    DbSet<Supplier> Suppliers { get; }
    DbSet<JournalEntry> JournalEntries { get; }
    DbSet<JournalEntryLine> JournalEntryLines { get; }
    DbSet<StockMovement> StockMovements { get; }
    DbSet<Warehouse> Warehouses { get; }
    DbSet<AuditLog> AuditLogs { get; }

    // Car Showroom Management Module
    DbSet<CarBrand> CarBrands { get; }
    DbSet<CarAgent> CarAgents { get; }
    DbSet<CarModel> CarModels { get; }
    DbSet<CarTrim> CarTrims { get; }
    DbSet<Vehicle> Vehicles { get; }
    DbSet<CarSalesContract> CarSalesContracts { get; }
    DbSet<CarProcurementOrder> CarProcurementOrders { get; }
    DbSet<CarProcurementOrderItem> CarProcurementOrderItems { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
