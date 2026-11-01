using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>مؤشرات لوحة التحكم مجمّعة في قاعدة البيانات (أعداد ومجاميع)، فلا تُنزَّل المستندات إلى الواجهة لعدّها.</summary>
public class DashboardService : IDashboardService
{
    private const int MonthsBack = 9;
    private readonly ErpDbContext _db;
    public DashboardService(ErpDbContext db) => _db = db;

    public async Task<DashboardSummaryDto> GetSummaryAsync(bool includeShowroom, CancellationToken ct = default)
    {
        var invoices = _db.Set<InvoiceBase>().AsNoTracking().Where(i => i.Status != "cancelled");
        var sales = invoices.Where(i => i.Kind == InvoiceKind.Sales || i.Kind == InvoiceKind.SalesReturn);
        var purchases = invoices.Where(i => i.Kind == InvoiceKind.Purchase);

        // المبالغ بالعملة الأساسية، والمرتجع بالسالب؛ GrossProfit مخزَّن بإشارته وبالعملة الأساسية
        var salesTotals = await sales.GroupBy(i => 1).Select(g => new
        {
            Count = g.Count(),
            Net = g.Sum(i => (i.Kind == InvoiceKind.SalesReturn ? -1 : 1) * (i.GrandTotal - i.VatTotal) * i.ExchangeRate),
            Vat = g.Sum(i => (i.Kind == InvoiceKind.SalesReturn ? -1 : 1) * i.VatTotal * i.ExchangeRate),
        }).FirstOrDefaultAsync(ct);
        var purchaseTotals = await purchases.GroupBy(i => 1).Select(g => new
        {
            Count = g.Count(),
            Total = g.Sum(i => i.GrandTotal * i.ExchangeRate),
            Credit = g.Sum(i => i.PaymentMethod == PaymentMethod.Credit ? i.GrandTotal * i.ExchangeRate : 0m),
        }).FirstOrDefaultAsync(ct);

        var today = SaudiTime.Today;
        var firstMonth = new DateTime(today.Year, today.Month, 1).AddMonths(-(MonthsBack - 1));
        var salesByMonth = await sales.Where(i => i.IssueDate >= firstMonth)
            .GroupBy(i => new { i.IssueDate.Year, i.IssueDate.Month })
            .Select(g => new
            {
                g.Key.Year, g.Key.Month,
                Net = g.Sum(i => (i.Kind == InvoiceKind.SalesReturn ? -1 : 1) * (i.GrandTotal - i.VatTotal) * i.ExchangeRate),
                Profit = g.Sum(i => i.GrossProfit),
            }).ToListAsync(ct);
        var purchasesByMonth = await purchases.Where(i => i.IssueDate >= firstMonth)
            .GroupBy(i => new { i.IssueDate.Year, i.IssueDate.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Total = g.Sum(i => i.GrandTotal * i.ExchangeRate) }).ToListAsync(ct);

        var months = Enumerable.Range(0, MonthsBack).Select(n => firstMonth.AddMonths(n)).Select(m =>
        {
            var s = salesByMonth.FirstOrDefault(x => x.Year == m.Year && x.Month == m.Month);
            var p = purchasesByMonth.FirstOrDefault(x => x.Year == m.Year && x.Month == m.Month);
            return new DashboardMonthDto { Month = m.ToString("yyyy-MM"), Sales = s?.Net ?? 0, Profit = s?.Profit ?? 0, Purchases = p?.Total ?? 0 };
        }).ToList();

        return new DashboardSummaryDto
        {
            SalesCount = salesTotals?.Count ?? 0, TotalSales = salesTotals?.Net ?? 0, TotalVatSales = salesTotals?.Vat ?? 0,
            PurchasesCount = purchaseTotals?.Count ?? 0, TotalPurchases = purchaseTotals?.Total ?? 0,
            CustomersCount = await _db.Set<Customer>().CountAsync(ct),
            SuppliersCount = await _db.Set<Supplier>().CountAsync(ct),
            ProductsCount = await _db.Set<Product>().CountAsync(ct),
            AccountsCount = await _db.Set<Account>().CountAsync(ct),
            JournalEntriesCount = await _db.Set<JournalEntry>().CountAsync(ct),
            VouchersCount = await _db.Set<Voucher>().CountAsync(ct),
            Months = months,
            Showroom = includeShowroom ? await ShowroomAsync(purchaseTotals?.Total ?? 0, purchaseTotals?.Credit ?? 0, ct) : null,
        };
    }

    private async Task<DashboardShowroomDto> ShowroomAsync(decimal purchasesTotal, decimal creditPurchases, CancellationToken ct)
    {
        var byStatus = await _db.Set<Vehicle>().AsNoTracking().GroupBy(v => v.Status).Select(g => new
        {
            Status = g.Key,
            Count = g.Count(),
            Cost = g.Sum(v => v.TotalCost != 0 ? v.TotalCost : v.PurchasePrice ?? 0m),
        }).ToListAsync(ct);
        int Count(VehicleStatus s) => byStatus.Where(x => x.Status == s).Sum(x => x.Count);
        return new DashboardShowroomDto
        {
            VehiclesTotal = byStatus.Sum(x => x.Count),
            VehiclesAvailable = Count(VehicleStatus.Available),
            VehiclesReserved = Count(VehicleStatus.Reserved),
            VehiclesSold = Count(VehicleStatus.Sold),
            InventoryValuation = byStatus.Where(x => x.Status is VehicleStatus.Available or VehicleStatus.Reserved).Sum(x => x.Cost),
            ProcurementCount = await _db.Set<CarProcurementOrder>().CountAsync(ct),
            ContractsCount = await _db.Set<CarSalesContract>().CountAsync(ct),
            CashPurchases = purchasesTotal - creditPurchases,
            CreditPurchases = creditPurchases,
        };
    }
}
