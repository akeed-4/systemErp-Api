using ERP.Service.Services.Shared;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using ERP.Service.Data;

using DevExtreme.AspNet.Data.ResponseModel;

namespace ERP.Service.Services.CarShowroom;

/// <summary>تقارير معرض السيارات (8 تقارير) - كلها قراءة فقط من بيانات العقود والمركبات والأوامر والفواتير الفعلية.</summary>
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

    public async Task<List<CarSalesPerformanceRowDto>> GetSalesPerformanceAsync(CarReportQueryDto q, CancellationToken ct = default)
        => (await Sold(q).OrderBy(c => c.Date).ToListAsync(ct)).Select(c => new CarSalesPerformanceRowDto
        {
            ContractNumber = c.ContractNumber, Date = c.Date, CycleType = c.CycleType, BuyerName = c.BuyerName,
            BuyerNationalIdOrCr = c.BuyerNationalIdOrCr, VehicleDescription = c.VehicleDescription, Vin = c.Vin, Condition = c.Condition,
            FinancingBankName = c.FinancingBankName, CostPrice = c.CostPrice, SellingPrice = c.NetPriceBeforeVat ?? c.SellingPrice,
            ProfitMargin = c.ProfitMargin, VatMode = c.VatMode, VatAmount = c.VatAmount, TotalWithVat = c.TotalWithVat,
        }).ToList();

    public async Task<List<CarVinInventoryRowDto>> GetVinInventoryAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return (await _db.Set<Vehicle>().AsNoTracking().Where(v => v.Status != VehicleStatus.Sold).OrderBy(v => v.CreatedAt).ToListAsync(ct))
            .Select(v => new CarVinInventoryRowDto
            {
                Id = v.Id, BrandNameAr = v.BrandNameAr, AgentNameAr = v.AgentNameAr, ModelNameAr = v.ModelNameAr, TrimNameAr = v.TrimNameAr,
                Year = v.Year, ChassisNumber = v.ChassisNumber, EngineNumber = v.EngineNumber, Condition = v.Condition,
                TotalCost = v.TotalCost, SellingPrice = v.SellingPrice, VatMode = v.VatMode, Status = v.Status,
                DaysInStock = Math.Max(0, (int)(now - v.CreatedAt).TotalDays),
            }).ToList();
    }

    public async Task<List<CarZatcaMarginTaxRowDto>> GetZatcaMarginTaxAsync(CarReportQueryDto q, CancellationToken ct = default)
    {
        var contracts = await Sold(q).Where(c => c.VatMode == VatMode.ProfitMargin_15 || c.VatMode == VatMode.MarginScheme).OrderBy(c => c.Date).ToListAsync(ct);
        var invoiceIds = contracts.Where(c => c.InvoiceId.HasValue).Select(c => c.InvoiceId!.Value).ToList();
        var status = await _db.Set<Invoice>().AsNoTracking().Where(i => invoiceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.ZatcaStatus, ct);
        return contracts.Select(c => new CarZatcaMarginTaxRowDto
        {
            ContractNumber = c.ContractNumber, Date = c.Date, BuyerName = c.BuyerName, Vin = c.Vin, VehicleDescription = c.VehicleDescription,
            PurchaseCost = c.CostPrice, SellingPrice = c.NetPriceBeforeVat ?? c.SellingPrice, GrossProfitMargin = c.ProfitMargin,
            VatAmount15Percent = c.VatAmount, TotalAmountCollected = c.TotalWithVat,
            ZatcaComplianceStatus = c.InvoiceId.HasValue && status.TryGetValue(c.InvoiceId.Value, out var s) ? s : ZatcaSubmissionStatus.NotSubmitted,
        }).ToList();
    }

    public async Task<List<CarProcurementTrackingRowDto>> GetProcurementTrackingAsync(CarReportQueryDto q, CancellationToken ct = default)
    {
        var orders = _db.Set<CarProcurementOrder>().AsNoTracking().AsQueryable();
        if (q.DateFrom.HasValue) orders = orders.Where(o => o.Date >= q.DateFrom);
        if (q.DateTo.HasValue) orders = orders.Where(o => o.Date <= q.DateTo);
        return (await orders.Include(o => o.Items).Include(o => o.ReceivedVins).OrderBy(o => o.Date).AsSplitQuery().ToListAsync(ct))
            .Select(o => new CarProcurementTrackingRowDto
            {
                OrderNumber = o.OrderNumber, Date = o.Date, SupplierName = o.SupplierName,
                BrandAndModel = string.Join("، ", o.Items.Select(i => $"{i.BrandName} {i.ModelName}")),
                RequestedQty = o.Items.Sum(i => i.Quantity), ReceivedVinQty = o.ReceivedVins.Count,
                UnitPrice = o.Items.Count == 0 ? 0 : Math.Round(o.Items.Average(i => i.UnitPrice), 2),
                GrandTotal = o.GrandTotal, CurrentStage = o.Stage, PaymentType = o.PaymentType,
            }).ToList();
    }

    public async Task<List<CarProfitLossRowDto>> GetProfitLossAsync(CarReportQueryDto q, CancellationToken ct = default)
    {
        var contracts = await Sold(q).OrderBy(c => c.Date).ToListAsync(ct);
        var invoiceIds = contracts.Where(c => c.InvoiceId.HasValue).Select(c => c.InvoiceId!.Value).ToList();
        var numbers = await _db.Set<Invoice>().AsNoTracking().Where(i => invoiceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.InvoiceNumber, ct);
        return contracts.Select(c =>
        {
            var revenue = c.NetPriceBeforeVat ?? c.SellingPrice;
            var profit = revenue - c.CostPrice;
            return new CarProfitLossRowDto
            {
                Id = c.Id, InvoiceNumber = c.InvoiceId.HasValue ? numbers.GetValueOrDefault(c.InvoiceId.Value) : null,
                ContractNumber = c.ContractNumber, SaleDate = c.Date, CustomerName = c.BuyerName, Vin = c.Vin,
                VehicleDescription = c.VehicleDescription, Condition = c.Condition, TotalCost = c.CostPrice, SellingPrice = revenue,
                ProfitAmount = profit, IsProfit = profit >= 0, ProfitPercentage = revenue == 0 ? 0 : Math.Round(profit / revenue * 100, 2),
                VatAmount = c.VatAmount, VatMode = c.VatMode, SalesPerson = c.SalespersonName,
            };
        }).ToList();
    }

    public async Task<List<CarInstallmentReceivableRowDto>> GetInstallmentsReceivableAsync(CancellationToken ct = default)
        => (await _db.Set<CarSalesContract>().AsNoTracking()
                .Where(c => c.Status != SalesContractStatus.Cancelled
                    && (c.CycleType == CarSalesCycleType.Installment || c.CycleType == CarSalesCycleType.BankLease))
                .OrderBy(c => c.Date).ToListAsync(ct))
            .Select(c => new CarInstallmentReceivableRowDto
            {
                Id = c.Id, ContractNumber = c.ContractNumber, CustomerName = c.BuyerName, CustomerPhone = c.BuyerPhone,
                CustomerNationalId = c.BuyerNationalIdOrCr, VehicleDescription = c.VehicleDescription, Vin = c.Vin, StartDate = c.Date,
                TotalContractAmount = c.TotalWithVat, DownPayment = c.DownPaymentAmount ?? 0,
                RemainingBalance = c.TotalWithVat - (c.DownPaymentAmount ?? 0),
                InstallmentsCount = c.FinanceTenorMonths ?? 0, MonthlyInstallment = c.MonthlyInstallment ?? 0,
                BankOrShowroom = c.CycleType == CarSalesCycleType.BankLease ? c.FinancingBankName ?? "بنك" : "المعرض",
            }).ToList();

    public async Task<List<CarSupplierProcurementRowDto>> GetSuppliersProcurementAsync(CarReportQueryDto q, CancellationToken ct = default)
    {
        var orders = _db.Set<CarProcurementOrder>().AsNoTracking().Where(o => o.Status != ProcurementOrderStatus.Rejected);
        if (q.DateFrom.HasValue) orders = orders.Where(o => o.Date >= q.DateFrom);
        if (q.DateTo.HasValue) orders = orders.Where(o => o.Date <= q.DateTo);
        var list = await orders.Include(o => o.ReceivedVins).AsSplitQuery().ToListAsync(ct);
        var suppliers = await _db.Set<Supplier>().AsNoTracking().ToDictionaryAsync(s => s.Id, ct);

        return list.GroupBy(o => o.SupplierId).Select(g =>
        {
            suppliers.TryGetValue(g.Key, out var s);
            var name = s?.NameAr ?? g.First().SupplierName;
            return new CarSupplierProcurementRowDto
            {
                SupplierId = g.Key, SupplierName = name, SupplierTaxNumber = s?.VatNumber, SupplierPhone = s?.Phone,
                TotalOrdersCount = g.Count(), VehiclesCount = g.Sum(o => o.ReceivedVins.Count),
                TotalPurchaseAmount = g.Sum(o => o.GrandTotal),
                CompletedOrdersCount = g.Count(o => o.Status == ProcurementOrderStatus.Invoiced || o.Status == ProcurementOrderStatus.Closed),
                InProgressOrdersCount = g.Count(o => o.Status is ProcurementOrderStatus.Approved or ProcurementOrderStatus.InProgress or ProcurementOrderStatus.Received),
                BalanceDue = s?.CurrentBalance ?? 0, LastOrderDate = g.Max(o => o.Date),
            };
        }).OrderByDescending(r => r.TotalPurchaseAmount).ToList();
    }

    public async Task<List<CarDailyMonthlySalesRowDto>> GetDailyMonthlySalesAsync(CarReportQueryDto q, CancellationToken ct = default)
    {
        var daily = string.Equals(q.Period, "daily", StringComparison.OrdinalIgnoreCase);
        var contracts = await Sold(q).OrderBy(c => c.Date).ToListAsync(ct);
        return contracts.GroupBy(c => daily ? c.Date.Date : new DateTime(c.Date.Year, c.Date.Month, 1))
            .OrderBy(g => g.Key).Select(g =>
            {
                var revenue = g.Sum(c => c.NetPriceBeforeVat ?? c.SellingPrice); var cost = g.Sum(c => c.CostPrice); var profit = revenue - cost;
                return new CarDailyMonthlySalesRowDto
                {
                    PeriodKey = g.Key.ToString(daily ? "yyyy-MM-dd" : "yyyy-MM"), PeriodType = daily ? "daily" : "monthly",
                    StartDate = g.Key, EndDate = daily ? g.Key : g.Key.AddMonths(1).AddDays(-1),
                    CarsSoldCount = g.Count(), CashSalesCount = g.Count(c => c.PaymentMethod is "cash" or "bank_transfer" or "pos_mada"),
                    CreditSalesCount = g.Count(c => c.PaymentMethod is "credit" or "bank_finance"),
                    TotalRevenue = revenue, TotalCost = cost, TotalGrossProfit = profit,
                    ProfitMarginPercent = revenue == 0 ? 0 : Math.Round(profit / revenue * 100, 2),
                    VatCollected = g.Sum(c => c.VatAmount), AverageCarPrice = g.Count() == 0 ? 0 : Math.Round(revenue / g.Count(), 2),
                };
            }).ToList();
    }

    public async Task<LoadResult> LoadSalesPerformanceAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => await ReportLoader.LoadAsync(await GetSalesPerformanceAsync(q, ct), options, ct);
    public async Task<LoadResult> LoadVinInventoryAsync(DataSourceLoadOptions options, CancellationToken ct = default)
        => await ReportLoader.LoadAsync(await GetVinInventoryAsync(ct), options, ct);
    public async Task<LoadResult> LoadZatcaMarginTaxAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => await ReportLoader.LoadAsync(await GetZatcaMarginTaxAsync(q, ct), options, ct);
    public async Task<LoadResult> LoadProcurementTrackingAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => await ReportLoader.LoadAsync(await GetProcurementTrackingAsync(q, ct), options, ct);
    public async Task<LoadResult> LoadProfitLossAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => await ReportLoader.LoadAsync(await GetProfitLossAsync(q, ct), options, ct);
    public async Task<LoadResult> LoadInstallmentsReceivableAsync(DataSourceLoadOptions options, CancellationToken ct = default)
        => await ReportLoader.LoadAsync(await GetInstallmentsReceivableAsync(ct), options, ct);
    public async Task<LoadResult> LoadSuppliersProcurementAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => await ReportLoader.LoadAsync(await GetSuppliersProcurementAsync(q, ct), options, ct);
    public async Task<LoadResult> LoadDailyMonthlySalesAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => await ReportLoader.LoadAsync(await GetDailyMonthlySalesAsync(q, ct), options, ct);
}
