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

    public async Task<SalesPerformanceDto> GetSalesPerformanceAsync(string? period, Guid? warehouseId, CancellationToken ct = default)
    {
        if (warehouseId is { } site) _db.EnsureWarehouseAllowed(site);
        period = period?.Trim().ToLowerInvariant() switch { "week" => "week", "year" => "year", _ => "month" };

        var today = SaudiTime.Today.Date;
        DateTime from, prevFrom, prevTo;
        switch (period)
        {
            case "week":
                // الأسبوع يبدأ الأحد
                from = today.AddDays(-(int)today.DayOfWeek);
                prevFrom = from.AddDays(-7);
                prevTo = today.AddDays(-7);
                break;
            case "year":
                from = new DateTime(today.Year, 1, 1);
                prevFrom = from.AddYears(-1);
                prevTo = today.AddYears(-1);
                break;
            default:
                from = new DateTime(today.Year, today.Month, 1);
                prevFrom = from.AddMonths(-1);
                prevTo = today.AddMonths(-1);
                break;
        }

        // المواقع المسموحة للمستخدم؛ المستند بلا موقع يظهر للجميع
        var invoices = _db.Set<InvoiceBase>().AsNoTracking().Where(i => i.Status != "cancelled");
        var allowed = _db.SessionAllowedWarehouseIds?.ToList();
        if (allowed != null) invoices = invoices.Where(i => i.WarehouseId == null || allowed.Contains(i.WarehouseId.Value));
        var allSitesSales = invoices.Where(i => i.Kind == InvoiceKind.Sales || i.Kind == InvoiceKind.SalesReturn);
        if (warehouseId.HasValue) invoices = invoices.Where(i => i.WarehouseId == warehouseId);
        var sales = invoices.Where(i => i.Kind == InvoiceKind.Sales || i.Kind == InvoiceKind.SalesReturn);
        var purchases = invoices.Where(i => i.Kind == InvoiceKind.Purchase || i.Kind == InvoiceKind.PurchaseReturn);

        var toEnd = today.AddDays(1);
        var current = await TotalsAsync(sales, purchases, from, toEnd, ct);
        var previous = await TotalsAsync(sales, purchases, prevFrom, prevTo.AddDays(1), ct);

        var inRange = sales.Where(i => i.IssueDate >= from && i.IssueDate < toEnd);
        List<SalesPerformanceBucketDto> trend;
        if (period == "year")
        {
            var byMonth = await inRange.GroupBy(i => new { i.IssueDate.Year, i.IssueDate.Month }).Select(g => new
            {
                g.Key.Year, g.Key.Month,
                Net = g.Sum(i => (i.Kind == InvoiceKind.SalesReturn ? -1 : 1) * (i.GrandTotal - i.VatTotal) * i.ExchangeRate),
                Profit = g.Sum(i => i.GrossProfit),
            }).ToListAsync(ct);
            trend = Enumerable.Range(0, today.Month).Select(n => from.AddMonths(n)).Select(m =>
            {
                var b = byMonth.FirstOrDefault(x => x.Year == m.Year && x.Month == m.Month);
                return Bucket(m.ToString("yyyy-MM"), b?.Net ?? 0, b?.Profit ?? 0);
            }).ToList();
        }
        else
        {
            var byDay = await inRange.GroupBy(i => i.IssueDate.Date).Select(g => new
            {
                Day = g.Key,
                Net = g.Sum(i => (i.Kind == InvoiceKind.SalesReturn ? -1 : 1) * (i.GrandTotal - i.VatTotal) * i.ExchangeRate),
                Profit = g.Sum(i => i.GrossProfit),
            }).ToListAsync(ct);
            trend = Enumerable.Range(0, (today - from).Days + 1).Select(n => from.AddDays(n)).Select(d =>
            {
                var b = byDay.FirstOrDefault(x => x.Day == d);
                return Bucket(d.ToString("yyyy-MM-dd"), b?.Net ?? 0, b?.Profit ?? 0);
            }).ToList();
        }

        var sites = (await allSitesSales.Where(i => i.IssueDate >= from && i.IssueDate < toEnd)
            .GroupBy(i => new { i.WarehouseId, i.WarehouseName }).Select(g => new
            {
                g.Key.WarehouseId, g.Key.WarehouseName,
                Net = g.Sum(i => (i.Kind == InvoiceKind.SalesReturn ? -1 : 1) * (i.GrandTotal - i.VatTotal) * i.ExchangeRate),
                Profit = g.Sum(i => i.GrossProfit),
                Count = g.Count(i => i.Kind == InvoiceKind.Sales),
            }).ToListAsync(ct))
            // الاسم مخزَّن على الفاتورة فقد يختلف بين فواتير الموقع نفسه
            .GroupBy(x => x.WarehouseId).Select(g => new SalesPerformanceSiteDto
            {
                WarehouseId = g.Key,
                Name = g.Key == null ? "بلا موقع" : g.Select(x => x.WarehouseName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? string.Empty,
                Sales = g.Sum(x => x.Net), Profit = g.Sum(x => x.Profit), InvoiceCount = g.Sum(x => x.Count),
            }).OrderByDescending(s => s.Sales).ToList();

        var topCustomers = await inRange.GroupBy(i => i.PartyName).Select(g => new SalesPerformancePartyDto
        {
            Name = g.Key,
            Sales = g.Sum(i => (i.Kind == InvoiceKind.SalesReturn ? -1 : 1) * (i.GrandTotal - i.VatTotal) * i.ExchangeRate),
            InvoiceCount = g.Count(i => i.Kind == InvoiceKind.Sales),
        }).OrderByDescending(p => p.Sales).Take(5).ToListAsync(ct);

        return new SalesPerformanceDto
        {
            Period = period, From = from, To = today, PreviousFrom = prevFrom, PreviousTo = prevTo, WarehouseId = warehouseId,
            Current = current, Previous = previous, Trend = trend, Sites = sites, TopCustomers = topCustomers,
        };
    }

    private static SalesPerformanceBucketDto Bucket(string key, decimal sales, decimal profit)
        => new() { Key = key, Sales = sales, Profit = profit, Cost = sales - profit };

    private static async Task<SalesPerformanceTotalsDto> TotalsAsync(IQueryable<InvoiceBase> sales, IQueryable<InvoiceBase> purchases,
        DateTime from, DateTime toExclusive, CancellationToken ct)
    {
        var s = await sales.Where(i => i.IssueDate >= from && i.IssueDate < toExclusive).GroupBy(i => 1).Select(g => new
        {
            Net = g.Sum(i => (i.Kind == InvoiceKind.SalesReturn ? -1 : 1) * (i.GrandTotal - i.VatTotal) * i.ExchangeRate),
            Returns = g.Sum(i => i.Kind == InvoiceKind.SalesReturn ? (i.GrandTotal - i.VatTotal) * i.ExchangeRate : 0m),
            Vat = g.Sum(i => (i.Kind == InvoiceKind.SalesReturn ? -1 : 1) * i.VatTotal * i.ExchangeRate),
            Profit = g.Sum(i => i.GrossProfit),
            Count = g.Count(i => i.Kind == InvoiceKind.Sales),
        }).FirstOrDefaultAsync(ct);
        var purchasesTotal = await purchases.Where(i => i.IssueDate >= from && i.IssueDate < toExclusive)
            .SumAsync(i => (i.Kind == InvoiceKind.PurchaseReturn ? -1 : 1) * i.GrandTotal * i.ExchangeRate, ct);

        var net = s?.Net ?? 0;
        var profit = s?.Profit ?? 0;
        var count = s?.Count ?? 0;
        return new SalesPerformanceTotalsDto
        {
            Sales = net, Profit = profit, Cost = net - profit,
            MarginPercent = net != 0 ? Math.Round(profit / net * 100, 2) : null,
            Returns = s?.Returns ?? 0, Vat = s?.Vat ?? 0, InvoiceCount = count,
            AverageInvoice = count > 0 ? Math.Round(net / count, 2) : 0,
            Purchases = purchasesTotal,
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
