using ERP.Service.Services.Shared;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using ERP.Service.Data;

using DevExtreme.AspNet.Data.ResponseModel;

namespace ERP.Service.Services.CarShowroom;

/// <summary>
/// تقارير معرض السيارات (8 تقارير) - كلها قراءة فقط من بيانات العقود والمركبات والأوامر والفواتير الفعلية.
/// كل تقرير استعلام يُنفَّذ في قاعدة البيانات مع خيارات DevExtreme، ولا تُحمَّل إلا الصفوف المطلوبة.
/// </summary>
public class CarShowroomReportService : ICarShowroomReportService
{
    private readonly ErpDbContext _db;
    public CarShowroomReportService(ErpDbContext db) => _db = db;

    private IQueryable<Invoice> Invoices => _db.Set<Invoice>().AsNoTracking();

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

    private IQueryable<CarSalesPerformanceRowDto> SalesPerformanceQuery(CarReportQueryDto q)
        => Sold(q).Select(c => new CarSalesPerformanceRowDto
        {
            Id = c.Id, ContractNumber = c.ContractNumber, Date = c.Date, CycleType = c.CycleType, BuyerName = c.BuyerName,
            BuyerNationalIdOrCr = c.BuyerNationalIdOrCr, VehicleDescription = c.VehicleDescription, Vin = c.Vin, Condition = c.Condition,
            FinancingBankName = c.FinancingBankName, CostPrice = c.CostPrice, SellingPrice = c.NetPriceBeforeVat ?? c.SellingPrice,
            ProfitMargin = c.ProfitMargin, VatMode = c.VatMode, VatAmount = c.VatAmount, TotalWithVat = c.TotalWithVat,
        });

    private IQueryable<CarVinInventoryRowDto> VinInventoryQuery()
    {
        var now = DateTime.UtcNow;
        return _db.Set<Vehicle>().AsNoTracking().Where(v => v.Status != VehicleStatus.Sold && v.Status != VehicleStatus.WrittenOff)
            .Select(v => new { v, Hours = EF.Functions.DateDiffHour(v.CreatedAt, now) })
            .Select(r => new CarVinInventoryRowDto
            {
                Id = r.v.Id, BrandNameAr = r.v.BrandNameAr, AgentNameAr = r.v.AgentNameAr, ModelNameAr = r.v.ModelNameAr, TrimNameAr = r.v.TrimNameAr,
                Year = r.v.Year, ChassisNumber = r.v.ChassisNumber, EngineNumber = r.v.EngineNumber, Condition = r.v.Condition,
                TotalCost = r.v.TotalCost, SellingPrice = r.v.SellingPrice, VatMode = r.v.VatMode, Status = r.v.Status,
                DaysInStock = r.Hours > 0 ? r.Hours / 24 : 0,
            });
    }

    private IQueryable<CarZatcaMarginTaxRowDto> ZatcaMarginTaxQuery(CarReportQueryDto q)
        // في هامش الربح الضريبة مضمَّنة: سعر البيع المتفق عليه = الإجمالي المحصَّل
        => Sold(q).Where(c => c.VatMode == VatMode.ProfitMargin_15 || c.VatMode == VatMode.MarginScheme).Select(c => new CarZatcaMarginTaxRowDto
        {
            Id = c.Id, ContractNumber = c.ContractNumber, Date = c.Date, BuyerName = c.BuyerName, Vin = c.Vin, VehicleDescription = c.VehicleDescription,
            PurchaseCost = c.CostPrice, SellingPrice = c.TotalWithVat, GrossProfitMargin = c.ProfitMargin,
            VatAmount15Percent = c.VatAmount, TotalAmountCollected = c.TotalWithVat,
            ZatcaComplianceStatus = Invoices.Where(i => i.Id == c.InvoiceId).Select(i => (ZatcaSubmissionStatus?)i.ZatcaStatus).FirstOrDefault()
                ?? ZatcaSubmissionStatus.NotSubmitted,
        });

    private IQueryable<CarProcurementTrackingRowDto> ProcurementTrackingQuery(CarReportQueryDto q)
        => Orders(q).Select(o => new CarProcurementTrackingRowDto
        {
            Id = o.Id, OrderNumber = o.OrderNumber, Date = o.Date, SupplierName = o.SupplierName,
            BrandAndModel = string.Join("، ", o.Items.Select(i => i.BrandName + " " + i.ModelName)),
            RequestedQty = o.Items.Sum(i => i.Quantity), ReceivedVinQty = o.ReceivedVins.Count,
            UnitPrice = Math.Round(o.Items.Average(i => (decimal?)i.UnitPrice) ?? 0m, 2),
            GrandTotal = o.GrandTotal, CurrentStage = o.Stage, PaymentType = o.PaymentType,
        });

    private IQueryable<CarProfitLossRowDto> ProfitLossQuery(CarReportQueryDto q)
        => Sold(q).Select(c => new { c, Revenue = c.NetPriceBeforeVat ?? c.SellingPrice }).Select(r => new CarProfitLossRowDto
        {
            Id = r.c.Id, InvoiceNumber = Invoices.Where(i => i.Id == r.c.InvoiceId).Select(i => i.InvoiceNumber).FirstOrDefault(),
            ContractNumber = r.c.ContractNumber, SaleDate = r.c.Date, CustomerName = r.c.BuyerName, Vin = r.c.Vin,
            VehicleDescription = r.c.VehicleDescription, Condition = r.c.Condition, TotalCost = r.c.CostPrice, SellingPrice = r.Revenue,
            ProfitAmount = r.Revenue - r.c.CostPrice, IsProfit = r.Revenue - r.c.CostPrice >= 0,
            ProfitPercentage = r.Revenue == 0 ? 0m : Math.Round((r.Revenue - r.c.CostPrice) / r.Revenue * 100, 2),
            VatAmount = r.c.VatAmount, VatMode = r.c.VatMode, SalesPerson = r.c.SalespersonName,
        });

    private IQueryable<CarInstallmentReceivableRowDto> InstallmentsReceivableQuery()
        => _db.Set<CarSalesContract>().AsNoTracking()
            .Where(c => c.Status != SalesContractStatus.Cancelled
                && (c.CycleType == CarSalesCycleType.Installment || c.CycleType == CarSalesCycleType.BankLease))
            .Select(c => new CarInstallmentReceivableRowDto
            {
                Id = c.Id, ContractNumber = c.ContractNumber, CustomerName = c.BuyerName, CustomerPhone = c.BuyerPhone,
                CustomerNationalId = c.BuyerNationalIdOrCr, VehicleDescription = c.VehicleDescription, Vin = c.Vin, StartDate = c.Date,
                TotalContractAmount = c.TotalWithVat, DownPayment = c.DownPaymentAmount ?? 0m,
                RemainingBalance = c.TotalWithVat - (c.DownPaymentAmount ?? 0m),
                InstallmentsCount = c.FinanceTenorMonths ?? 0, MonthlyInstallment = c.MonthlyInstallment ?? 0m,
                BankOrShowroom = c.CycleType == CarSalesCycleType.BankLease ? c.FinancingBankName ?? "بنك" : "المعرض",
            });

    private IQueryable<CarSupplierProcurementRowDto> SuppliersProcurementQuery(CarReportQueryDto q)
    {
        var orders = Orders(q).Where(o => o.Status != ProcurementOrderStatus.Rejected);
        var vins = _db.Set<CarProcurementOrderVin>().AsNoTracking();
        var totals = from o in orders
                     group o by o.SupplierId into g
                     select new
                     {
                         SupplierId = g.Key, OrderSupplierName = g.Max(o => o.SupplierName),
                         Orders = g.Count(), Amount = g.Sum(o => o.GrandTotal), LastOrderDate = g.Max(o => o.Date),
                         Completed = g.Count(o => o.Status == ProcurementOrderStatus.Invoiced || o.Status == ProcurementOrderStatus.Closed),
                         InProgress = g.Count(o => o.Status == ProcurementOrderStatus.Approved || o.Status == ProcurementOrderStatus.InProgress
                             || o.Status == ProcurementOrderStatus.Received),
                     };
        return from t in totals
               join s in _db.Set<Supplier>().AsNoTracking() on t.SupplierId equals s.Id into matched
               from s in matched.DefaultIfEmpty()
               select new CarSupplierProcurementRowDto
               {
                   SupplierId = t.SupplierId, SupplierName = s.NameAr ?? t.OrderSupplierName, SupplierTaxNumber = s.VatNumber, SupplierPhone = s.Phone,
                   TotalOrdersCount = t.Orders,
                   VehiclesCount = vins.Count(v => orders.Any(o => o.Id == v.OrderId && o.SupplierId == t.SupplierId)),
                   TotalPurchaseAmount = t.Amount, CompletedOrdersCount = t.Completed, InProgressOrdersCount = t.InProgress,
                   BalanceDue = (decimal?)s.CurrentBalance ?? 0m, LastOrderDate = t.LastOrderDate,
               };
    }

    private IQueryable<CarDailyMonthlySalesRowDto> DailyMonthlySalesQuery(CarReportQueryDto q)
    {
        var daily = string.Equals(q.Period, "daily", StringComparison.OrdinalIgnoreCase);
        var periodType = daily ? "daily" : "monthly";
        var sold = Sold(q);
        // بداية الفترة مفتاح التجميع: اليوم نفسه، أو أول الشهر
        var keyed = daily
            ? sold.Select(c => new { Start = c.Date.Date, Revenue = c.NetPriceBeforeVat ?? c.SellingPrice, c.CostPrice, c.VatAmount, c.PaymentMethod })
            : sold.Select(c => new { Start = EF.Functions.DateFromParts(c.Date.Year, c.Date.Month, 1), Revenue = c.NetPriceBeforeVat ?? c.SellingPrice, c.CostPrice, c.VatAmount, c.PaymentMethod });
        var totals = from c in keyed
                     group c by c.Start into g
                     select new
                     {
                         Start = g.Key, Cars = g.Count(), Revenue = g.Sum(c => c.Revenue), Cost = g.Sum(c => c.CostPrice), Vat = g.Sum(c => c.VatAmount),
                         Cash = g.Count(c => c.PaymentMethod == "cash" || c.PaymentMethod == "bank_transfer" || c.PaymentMethod == "pos_mada"),
                         Credit = g.Count(c => c.PaymentMethod == "credit" || c.PaymentMethod == "bank_finance"),
                     };
        return totals.Select(t => new CarDailyMonthlySalesRowDto
        {
            PeriodKey = t.Start.Year.ToString() + "-" + (t.Start.Month < 10 ? "0" : "") + t.Start.Month.ToString()
                + (daily ? "-" + (t.Start.Day < 10 ? "0" : "") + t.Start.Day.ToString() : ""),
            PeriodType = periodType,
            StartDate = t.Start, EndDate = daily ? t.Start : t.Start.AddMonths(1).AddDays(-1),
            CarsSoldCount = t.Cars, CashSalesCount = t.Cash, CreditSalesCount = t.Credit,
            TotalRevenue = t.Revenue, TotalCost = t.Cost, TotalGrossProfit = t.Revenue - t.Cost,
            ProfitMarginPercent = t.Revenue == 0 ? 0m : Math.Round((t.Revenue - t.Cost) / t.Revenue * 100, 2),
            VatCollected = t.Vat, AverageCarPrice = Math.Round(t.Revenue / t.Cars, 2),
        });
    }

    public Task<LoadResult> LoadSalesPerformanceAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(SalesPerformanceQuery(q), options, nameof(CarSalesPerformanceRowDto.Id), ct, EntityLoader.Asc(nameof(CarSalesPerformanceRowDto.Date)));
    public Task<LoadResult> LoadVinInventoryAsync(DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(VinInventoryQuery(), options, nameof(CarVinInventoryRowDto.Id), ct, EntityLoader.Desc(nameof(CarVinInventoryRowDto.DaysInStock)));
    public Task<LoadResult> LoadZatcaMarginTaxAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(ZatcaMarginTaxQuery(q), options, nameof(CarZatcaMarginTaxRowDto.Id), ct, EntityLoader.Asc(nameof(CarZatcaMarginTaxRowDto.Date)));
    public Task<LoadResult> LoadProcurementTrackingAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(ProcurementTrackingQuery(q), options, nameof(CarProcurementTrackingRowDto.Id), ct, EntityLoader.Asc(nameof(CarProcurementTrackingRowDto.Date)));
    public Task<LoadResult> LoadProfitLossAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(ProfitLossQuery(q), options, nameof(CarProfitLossRowDto.Id), ct, EntityLoader.Asc(nameof(CarProfitLossRowDto.SaleDate)));
    public Task<LoadResult> LoadInstallmentsReceivableAsync(DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(InstallmentsReceivableQuery(), options, nameof(CarInstallmentReceivableRowDto.Id), ct, EntityLoader.Asc(nameof(CarInstallmentReceivableRowDto.StartDate)));
    public Task<LoadResult> LoadSuppliersProcurementAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(SuppliersProcurementQuery(q), options, nameof(CarSupplierProcurementRowDto.SupplierId), ct, EntityLoader.Desc(nameof(CarSupplierProcurementRowDto.TotalPurchaseAmount)));
    public Task<LoadResult> LoadDailyMonthlySalesAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(DailyMonthlySalesQuery(q), options, nameof(CarDailyMonthlySalesRowDto.StartDate), ct, EntityLoader.Asc(nameof(CarDailyMonthlySalesRowDto.StartDate)));
}
