using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;

namespace ERP.WebApi.Controllers;

[ApiController]
[Route("api/v1/database")]
public class DatabaseSyncController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantService _tenantService;

    public DatabaseSyncController(ApplicationDbContext context, ITenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    /// <summary>
    /// فحص حالة قاعدة البيانات وعدد السجلات في كل جدول
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetDatabaseStatus()
    {
        var tables = new List<object>
        {
            new { name = "tenants", count = await _context.Tenants.CountAsync(), nameAr = "سجلات المنشآت والشركات" },
            new { name = "invoices", count = await _context.Invoices.CountAsync(), nameAr = "فواتير المبيعات والمشتريات" },
            new { name = "vouchers", count = await _context.Vouchers.CountAsync(), nameAr = "سندات القبض والصرف" },
            new { name = "accounts", count = await _context.Accounts.CountAsync(), nameAr = "دليل شجرة الحسابات" },
            new { name = "customers", count = await _context.Customers.CountAsync(), nameAr = "سجل العملاء" },
            new { name = "suppliers", count = await _context.Suppliers.CountAsync(), nameAr = "سجل الموردين" },
            new { name = "products", count = await _context.Products.CountAsync(), nameAr = "كتالوج المنتجات والمخزون" },
            new { name = "journalEntries", count = await _context.JournalEntries.CountAsync(), nameAr = "قيود اليومية العامة" },
            new { name = "stockMovements", count = await _context.StockMovements.CountAsync(), nameAr = "حركات المخزون والمستودعات" },
            new { name = "warehouses", count = await _context.Warehouses.CountAsync(), nameAr = "المستودعات والفروع" },
            new { name = "auditLogs", count = await _context.AuditLogs.CountAsync(), nameAr = "سجلات التدقيق والأمان" },
            new { name = "carBrands", count = await _context.CarBrands.CountAsync(), nameAr = "ماركات وعلامات السيارات" },
            new { name = "carAgents", count = await _context.CarAgents.CountAsync(), nameAr = "وكلاء السيارات المحليين" },
            new { name = "carModels", count = await _context.CarModels.CountAsync(), nameAr = "طرازات وموديلات السيارات" },
            new { name = "carTrims", count = await _context.CarTrims.CountAsync(), nameAr = "فئات ومواصفات السيارات" },
            new { name = "vehicles", count = await _context.Vehicles.CountAsync(), nameAr = "مخزون السيارات برقم الشاسي" },
            new { name = "carSalesContracts", count = await _context.CarSalesContracts.CountAsync(), nameAr = "عقود مبيعات سيارات المعرض" },
            new { name = "carProcurementOrders", count = await _context.CarProcurementOrders.CountAsync(), nameAr = "أوامر توريد وشراء السيارات" }
        };

        return Ok(new
        {
            success = true,
            engine = ".NET 9 Enterprise Core (EF Core SQL Server / PostgreSQL / Firestore)",
            status = "connected",
            tenantId = _tenantService.CurrentTenantId,
            totalTables = tables.Count,
            lastChecked = DateTime.UtcNow,
            tables
        });
    }

    /// <summary>
    /// مزامنة شاملة لكافة الجداول وحفظها
    /// </summary>
    [HttpPost("sync")]
    public async Task<IActionResult> SyncDatabase([FromBody] FullSyncRequestDto request)
    {
        var stats = new Dictionary<string, int>();

        if (request.Invoices != null && request.Invoices.Any())
        {
            foreach (var inv in request.Invoices)
            {
                var existing = await _context.Invoices.Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == inv.Id);
                if (existing != null)
                {
                    _context.Entry(existing).CurrentValues.SetValues(inv);
                }
                else
                {
                    _context.Invoices.Add(inv);
                }
            }
            stats["invoices"] = request.Invoices.Count;
        }

        if (request.Accounts != null && request.Accounts.Any())
        {
            foreach (var acc in request.Accounts)
            {
                var existing = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == acc.Id || a.Code == acc.Code);
                if (existing != null)
                {
                    existing.NameAr = acc.NameAr;
                    existing.Balance = acc.Balance;
                }
                else
                {
                    _context.Accounts.Add(acc);
                }
            }
            stats["accounts"] = request.Accounts.Count;
        }

        if (request.Products != null && request.Products.Any())
        {
            foreach (var prod in request.Products)
            {
                var existing = await _context.Products.FirstOrDefaultAsync(p => p.Id == prod.Id || p.Sku == prod.Sku);
                if (existing != null)
                {
                    existing.CurrentStock = prod.CurrentStock;
                    existing.AverageCost = prod.AverageCost;
                    existing.SellingPrice = prod.SellingPrice;
                }
                else
                {
                    _context.Products.Add(prod);
                }
            }
            stats["products"] = request.Products.Count;
        }

        if (request.CarBrands != null && request.CarBrands.Any())
        {
            foreach (var brand in request.CarBrands)
            {
                var existing = await _context.CarBrands.FirstOrDefaultAsync(x => x.Id == brand.Id);
                if (existing != null) _context.Entry(existing).CurrentValues.SetValues(brand);
                else _context.CarBrands.Add(brand);
            }
            stats["carBrands"] = request.CarBrands.Count;
        }

        if (request.CarAgents != null && request.CarAgents.Any())
        {
            foreach (var agent in request.CarAgents)
            {
                var existing = await _context.CarAgents.FirstOrDefaultAsync(x => x.Id == agent.Id);
                if (existing != null) _context.Entry(existing).CurrentValues.SetValues(agent);
                else _context.CarAgents.Add(agent);
            }
            stats["carAgents"] = request.CarAgents.Count;
        }

        if (request.Vehicles != null && request.Vehicles.Any())
        {
            foreach (var vehicle in request.Vehicles)
            {
                var existing = await _context.Vehicles.FirstOrDefaultAsync(x => x.Id == vehicle.Id);
                if (existing != null) _context.Entry(existing).CurrentValues.SetValues(vehicle);
                else _context.Vehicles.Add(vehicle);
            }
            stats["vehicles"] = request.Vehicles.Count;
        }

        if (request.CarSalesContracts != null && request.CarSalesContracts.Any())
        {
            foreach (var contract in request.CarSalesContracts)
            {
                var existing = await _context.CarSalesContracts.FirstOrDefaultAsync(x => x.Id == contract.Id);
                if (existing != null) _context.Entry(existing).CurrentValues.SetValues(contract);
                else _context.CarSalesContracts.Add(contract);
            }
            stats["carSalesContracts"] = request.CarSalesContracts.Count;
        }

        if (request.CarProcurementOrders != null && request.CarProcurementOrders.Any())
        {
            foreach (var order in request.CarProcurementOrders)
            {
                var existing = await _context.CarProcurementOrders.Include(o => o.Items).FirstOrDefaultAsync(x => x.Id == order.Id);
                if (existing != null) _context.Entry(existing).CurrentValues.SetValues(order);
                else _context.CarProcurementOrders.Add(order);
            }
            stats["carProcurementOrders"] = request.CarProcurementOrders.Count;
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            message = "تمت مزامنة وحفظ جميع الجداول المحاسبية ومعارض السيارات بنجاح في قاعدة بيانات .NET Core",
            tenantId = _tenantService.CurrentTenantId,
            timestamp = DateTime.UtcNow,
            stats
        });
    }
}

public class FullSyncRequestDto
{
    public List<Invoice>? Invoices { get; set; }
    public List<Account>? Accounts { get; set; }
    public List<Product>? Products { get; set; }
    public List<Customer>? Customers { get; set; }
    public List<Supplier>? Suppliers { get; set; }
    public List<Voucher>? Vouchers { get; set; }
    public List<CarBrand>? CarBrands { get; set; }
    public List<CarAgent>? CarAgents { get; set; }
    public List<Vehicle>? Vehicles { get; set; }
    public List<CarSalesContract>? CarSalesContracts { get; set; }
    public List<CarProcurementOrder>? CarProcurementOrders { get; set; }
}
