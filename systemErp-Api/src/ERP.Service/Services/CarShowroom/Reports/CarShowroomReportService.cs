using ERP.Service.Services.Shared;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using ERP.Service.Data;

using DevExtreme.AspNet.Data.ResponseModel;

namespace ERP.Service.Services.CarShowroom;

/// <summary>
/// تقارير معرض السيارات (8 تقارير) - قراءة فقط من بيانات العقود والمركبات والأوامر والفواتير الفعلية.
/// كل تقرير مبني كاستعلام IQueryable مُسقَط، فتُنفَّذ الفلترة والفرز والترقيم والملخصات في قاعدة البيانات
/// ولا يُحمَّل إلى الذاكرة إلا الصفحة المطلوبة.
/// </summary>
public class CarShowroomReportService : ICarShowroomReportService
{
    private readonly ErpDbContext _db;
    public CarShowroomReportService(ErpDbContext db) => _db = db;

    /// <summary>العقود المعتمدة ماليًا: الفوترة تعني تحقق البيع.</summary>
    private IQueryable<CarSalesContract> Sold(CarReportQueryDto q)
    {
        var c = _db.Set<CarSalesContract>().AsNoTracking().Where(x => x.Status == SalesContractStatus.Invoiced);
        if (q.DateFrom.HasValue) c = c.Where(x => x.Date >= q.DateFrom);
        if (q.DateTo.HasValue) c = c.Where(x => x.Date <= q.DateTo);
        return c;
    }

    private IQueryable<CarProcurementOrder> Orders(CarReportQueryDto q)
    {
        var orders = _db.Set<CarProcurementOrder>().AsNoTracking().AsQueryable();
        if (q.DateFrom.HasValue) orders = orders.Where(o => o.Date >= q.DateFrom);
        if (q.DateTo.HasValue) orders = orders.Where(o => o.Date <= q.DateTo);
        return orders;
    }

    // ---------- الاستعلامات (تُنفَّذ في قاعدة البيانات) ----------

    private IQueryable<CarSalesPerformanceRowDto> SalesPerformanceQuery(CarReportQueryDto q)
        => Sold(q).OrderBy(c => c.Date).Select(c => new CarSalesPerformanceRowDto
        {
            ContractNumber = c.ContractNumber, Date = c.Date, CycleType = c.CycleType, BuyerName = c.BuyerName,
            BuyerNationalIdOrCr = c.BuyerNationalIdOrCr, VehicleDescription = c.VehicleDescription, Vin = c.Vin, Condition = c.Condition,
            FinancingBankName = c.FinancingBankName, CostPrice = c.CostPrice, SellingPrice = c.NetPriceBeforeVat ?? c.SellingPrice,
            ProfitMargin = c.ProfitMargin, VatMode = c.VatMode, VatAmount = c.VatAmount, TotalWithVat = c.TotalWithVat,
        });

    private IQueryable<CarVinInventoryRowDto> VinInventoryQuery()
    {
        var now = DateTime.UtcNow;
        return _db.Set<Vehicle>().AsNoTracking().Where(v => v.Status != VehicleStatus.Sold).OrderBy(v => v.CreatedAt)
            .Select(v => new CarVinInventoryRowDto
            {
                Id = v.Id, BrandNameAr = v.BrandNameAr, AgentNameAr = v.AgentNameAr, ModelNameAr = v.ModelNameAr, TrimNameAr = v.TrimNameAr,
                Year = v.Year, ChassisNumber = v.ChassisNumber, EngineNumber = v.EngineNumber, Condition = v.Condition,
                TotalCost = v.TotalCost, SellingPrice = v.SellingPrice, VatMode = v.VatMode, Status = v.Status,
                DaysInStock = v.CreatedAt > now ? 0 : EF.Functions.DateDiffDay(v.CreatedAt, now),
            });
    }

    private IQueryable<CarZatcaMarginTaxRowDto> ZatcaMarginTaxQuery(CarReportQueryDto q)
    {
        var invoices = _db.Set<Invoice>().AsNoTracking();
        // في هامش الربح الضريبة مضمَّنة: سعر البيع المتفق عليه = الإجمالي المحصَّل
        return Sold(q).Where(c => c.VatMode == VatMode.ProfitMargin_15 || c.VatMode == VatMode.MarginScheme).OrderBy(c => c.Date)
            .Select(c => new CarZatcaMarginTaxRowDto
            {
                ContractNumber = c.ContractNumber, Date = c.Date, BuyerName = c.BuyerName, Vin = c.Vin, VehicleDescription = c.VehicleDescription,
                PurchaseCost = c.CostPrice, SellingPrice = c.TotalWithVat, GrossProfitMargin = c.ProfitMargin,
                VatAmount15Percent = c.VatAmount, TotalAmountCollected = c.TotalWithVat,
                ZatcaComplianceStatus = invoices.Where(i => i.Id == c.InvoiceId).Select(i => (ZatcaSubmissionStatus?)i.ZatcaStatus).FirstOrDefault()
                    ?? ZatcaSubmissionStatus.NotSubmitted,
            });
    }

    /// <summary>BrandAndModel نصّ مجمّع من البنود؛ يُملأ بعد جلب الصفحة (انظر <see cref="FillBrandAndModelAsync"/>).</summary>
    private IQueryable<CarProcurementTrackingRowDto> ProcurementTrackingQuery(CarReportQueryDto q)
        => Orders(q).OrderBy(o => o.Date).Select(o => new CarProcurementTrackingRowDto
        {
            Id = o.Id, OrderNumber = o.OrderNumber, Date = o.Date, SupplierName = o.SupplierName,
            RequestedQty = o.Items.Sum(i => (int?)i.Quantity) ?? 0, ReceivedVinQty = o.ReceivedVins.Count(),
            UnitPrice = o.Items.Any() ? Math.Round(o.Items.Average(i => i.UnitPrice), 2) : 0,
            GrandTotal = o.GrandTotal, CurrentStage = o.Stage, PaymentType = o.PaymentType,
        });

    private IQueryable<CarProfitLossRowDto> ProfitLossQuery(CarReportQueryDto q)
    {
        var invoices = _db.Set<Invoice>().AsNoTracking();
        return Sold(q).OrderBy(c => c.Date).Select(c => new
            {
                c,
                Revenue = c.NetPriceBeforeVat ?? c.SellingPrice,
                InvoiceNumber = invoices.Where(i => i.Id == c.InvoiceId).Select(i => i.InvoiceNumber).FirstOrDefault(),
            })
            .Select(x => new CarProfitLossRowDto
            {
                Id = x.c.Id, InvoiceNumber = x.InvoiceNumber,
                ContractNumber = x.c.ContractNumber, SaleDate = x.c.Date, CustomerName = x.c.BuyerName, Vin = x.c.Vin,
                VehicleDescription = x.c.VehicleDescription, Condition = x.c.Condition, TotalCost = x.c.CostPrice, SellingPrice = x.Revenue,
                ProfitAmount = x.Revenue - x.c.CostPrice, IsProfit = x.Revenue - x.c.CostPrice >= 0,
                ProfitPercentage = x.Revenue == 0 ? 0 : Math.Round((x.Revenue - x.c.CostPrice) / x.Revenue * 100, 2),
                VatAmount = x.c.VatAmount, VatMode = x.c.VatMode, SalesPerson = x.c.SalespersonName,
            });
    }

    private IQueryable<CarInstallmentReceivableRowDto> InstallmentsReceivableQuery()
        => _db.Set<CarSalesContract>().AsNoTracking()
            .Where(c => c.Status != SalesContractStatus.Cancelled
                && (c.CycleType == CarSalesCycleType.Installment || c.CycleType == CarSalesCycleType.BankLease))
            .OrderBy(c => c.Date)
            .Select(c => new CarInstallmentReceivableRowDto
            {
                Id = c.Id, ContractNumber = c.ContractNumber, CustomerName = c.BuyerName, CustomerPhone = c.BuyerPhone,
                CustomerNationalId = c.BuyerNationalIdOrCr, VehicleDescription = c.VehicleDescription, Vin = c.Vin, StartDate = c.Date,
                TotalContractAmount = c.TotalWithVat, DownPayment = c.DownPaymentAmount ?? 0,
                RemainingBalance = c.TotalWithVat - (c.DownPaymentAmount ?? 0),
                InstallmentsCount = c.FinanceTenorMonths ?? 0, MonthlyInstallment = c.MonthlyInstallment ?? 0,
                BankOrShowroom = c.CycleType == CarSalesCycleType.BankLease ? c.FinancingBankName ?? "بنك" : "المعرض",
            });

    /// <summary>صف لكل مورد له أوامر في الفترة؛ المجاميع استعلامات فرعية مترابطة تُحسب في SQL.</summary>
    private IQueryable<CarSupplierProcurementRowDto> SuppliersProcurementQuery(CarReportQueryDto q)
    {
        var orders = Orders(q).Where(o => o.Status != ProcurementOrderStatus.Rejected);
        return _db.Set<Supplier>().AsNoTracking()
            .Where(s => orders.Any(o => o.SupplierId == s.Id))
            .Select(s => new CarSupplierProcurementRowDto
            {
                SupplierId = s.Id, SupplierName = s.NameAr, SupplierTaxNumber = s.VatNumber, SupplierPhone = s.Phone,
                TotalOrdersCount = orders.Count(o => o.SupplierId == s.Id),
                VehiclesCount = orders.Where(o => o.SupplierId == s.Id).SelectMany(o => o.ReceivedVins).Count(),
                TotalPurchaseAmount = orders.Where(o => o.SupplierId == s.Id).Sum(o => (decimal?)o.GrandTotal) ?? 0,
                CompletedOrdersCount = orders.Count(o => o.SupplierId == s.Id
                    && (o.Status == ProcurementOrderStatus.Invoiced || o.Status == ProcurementOrderStatus.Closed)),
                InProgressOrdersCount = orders.Count(o => o.SupplierId == s.Id
                    && (o.Status == ProcurementOrderStatus.Approved || o.Status == ProcurementOrderStatus.InProgress || o.Status == ProcurementOrderStatus.Received)),
                BalanceDue = s.CurrentBalance,
                LastOrderDate = orders.Where(o => o.SupplierId == s.Id).Max(o => (DateTime?)o.Date),
            })
            .OrderByDescending(r => r.TotalPurchaseAmount);
    }

    /// <summary>
    /// التجميع (يومي/شهري) يتم في قاعدة البيانات؛ الناتج صف لكل فترة فقط، فصياغة مفتاح الفترة في الذاكرة لا تكلّف شيئًا.
    /// </summary>
    private async Task<List<CarDailyMonthlySalesRowDto>> DailyMonthlySalesRowsAsync(CarReportQueryDto q, CancellationToken ct)
    {
        var daily = string.Equals(q.Period, "daily", StringComparison.OrdinalIgnoreCase);
        var sold = Sold(q);
        var grouped = daily
            ? sold.GroupBy(c => new { c.Date.Year, c.Date.Month, c.Date.Day })
            : sold.GroupBy(c => new { c.Date.Year, c.Date.Month, Day = 1 });
        var totals = await grouped.Select(g => new
        {
            g.Key.Year, g.Key.Month, g.Key.Day,
            Count = g.Count(),
            Cash = g.Count(c => c.PaymentMethod == "cash" || c.PaymentMethod == "bank_transfer" || c.PaymentMethod == "pos_mada"),
            Credit = g.Count(c => c.PaymentMethod == "credit" || c.PaymentMethod == "bank_finance"),
            Revenue = g.Sum(c => c.NetPriceBeforeVat ?? c.SellingPrice),
            Cost = g.Sum(c => c.CostPrice),
            Vat = g.Sum(c => c.VatAmount),
        }).ToListAsync(ct);

        return totals.Select(t =>
        {
            var start = new DateTime(t.Year, t.Month, t.Day);
            var profit = t.Revenue - t.Cost;
            return new CarDailyMonthlySalesRowDto
            {
                PeriodKey = start.ToString(daily ? "yyyy-MM-dd" : "yyyy-MM"), PeriodType = daily ? "daily" : "monthly",
                StartDate = start, EndDate = daily ? start : start.AddMonths(1).AddDays(-1),
                CarsSoldCount = t.Count, CashSalesCount = t.Cash, CreditSalesCount = t.Credit,
                TotalRevenue = t.Revenue, TotalCost = t.Cost, TotalGrossProfit = profit,
                ProfitMarginPercent = t.Revenue == 0 ? 0 : Math.Round(profit / t.Revenue * 100, 2),
                VatCollected = t.Vat, AverageCarPrice = t.Count == 0 ? 0 : Math.Round(t.Revenue / t.Count, 2),
            };
        }).OrderBy(r => r.StartDate).ToList();
    }

    /// <summary>يملأ BrandAndModel لصفوف الصفحة الحالية فقط باستعلام واحد على البنود.</summary>
    private async Task FillBrandAndModelAsync(IReadOnlyCollection<CarProcurementTrackingRowDto> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return;
        var ids = rows.Select(r => r.Id).ToList();
        var items = await _db.Set<CarProcurementOrder>().AsNoTracking().Where(o => ids.Contains(o.Id))
            .SelectMany(o => o.Items.Select(i => new { OrderId = o.Id, i.BrandName, i.ModelName }))
            .ToListAsync(ct);
        var byOrder = items.GroupBy(i => i.OrderId).ToDictionary(g => g.Key, g => string.Join("، ", g.Select(i => $"{i.BrandName} {i.ModelName}")));
        foreach (var row in rows) row.BrandAndModel = byOrder.GetValueOrDefault(row.Id) ?? string.Empty;
    }

    // ---------- القوائم الكاملة (للتصدير/الاستخدام الداخلي) ----------

    public Task<List<CarSalesPerformanceRowDto>> GetSalesPerformanceAsync(CarReportQueryDto q, CancellationToken ct = default)
        => SalesPerformanceQuery(q).ToListAsync(ct);

    public Task<List<CarVinInventoryRowDto>> GetVinInventoryAsync(CancellationToken ct = default)
        => VinInventoryQuery().ToListAsync(ct);

    public Task<List<CarZatcaMarginTaxRowDto>> GetZatcaMarginTaxAsync(CarReportQueryDto q, CancellationToken ct = default)
        => ZatcaMarginTaxQuery(q).ToListAsync(ct);

    public async Task<List<CarProcurementTrackingRowDto>> GetProcurementTrackingAsync(CarReportQueryDto q, CancellationToken ct = default)
    {
        var rows = await ProcurementTrackingQuery(q).ToListAsync(ct);
        await FillBrandAndModelAsync(rows, ct);
        return rows;
    }

    public Task<List<CarProfitLossRowDto>> GetProfitLossAsync(CarReportQueryDto q, CancellationToken ct = default)
        => ProfitLossQuery(q).ToListAsync(ct);

    public Task<List<CarInstallmentReceivableRowDto>> GetInstallmentsReceivableAsync(CancellationToken ct = default)
        => InstallmentsReceivableQuery().ToListAsync(ct);

    public Task<List<CarSupplierProcurementRowDto>> GetSuppliersProcurementAsync(CarReportQueryDto q, CancellationToken ct = default)
        => SuppliersProcurementQuery(q).ToListAsync(ct);

    public Task<List<CarDailyMonthlySalesRowDto>> GetDailyMonthlySalesAsync(CarReportQueryDto q, CancellationToken ct = default)
        => DailyMonthlySalesRowsAsync(q, ct);

    // ---------- تقارير DevExtreme (صفحة واحدة + ملخصات من الخادم) ----------

    public Task<LoadResult> LoadSalesPerformanceAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(SalesPerformanceQuery(q), options, ct, "Date", "ContractNumber");
    public Task<LoadResult> LoadVinInventoryAsync(DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(VinInventoryQuery(), options, ct, "-DaysInStock", "ChassisNumber");
    public Task<LoadResult> LoadZatcaMarginTaxAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(ZatcaMarginTaxQuery(q), options, ct, "Date", "ContractNumber");
    public async Task<LoadResult> LoadProcurementTrackingAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
    {
        var result = await ReportLoader.LoadAsync(ProcurementTrackingQuery(q), options, ct, "Date", "OrderNumber");
        var page = ReportLoader.Materialize(result);
        await FillBrandAndModelAsync(page.OfType<CarProcurementTrackingRowDto>().ToList(), ct);
        return result;
    }
    public Task<LoadResult> LoadProfitLossAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(ProfitLossQuery(q), options, ct, "SaleDate", "ContractNumber");
    public Task<LoadResult> LoadInstallmentsReceivableAsync(DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(InstallmentsReceivableQuery(), options, ct, "StartDate", "ContractNumber");
    public Task<LoadResult> LoadSuppliersProcurementAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(SuppliersProcurementQuery(q), options, ct, "-TotalPurchaseAmount", "SupplierName");
    public async Task<LoadResult> LoadDailyMonthlySalesAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => await ReportLoader.LoadAsync(await DailyMonthlySalesRowsAsync(q, ct), options, ct);
}
