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
        if (q.DateTo.HasValue) c = c.Where(x => x.Date < q.EndExclusive());
        return c;
    }

    private IQueryable<CarProcurementOrder> Orders(CarReportQueryDto q)
    {
        var orders = _db.Set<CarProcurementOrder>().AsNoTracking().AsQueryable();
        if (q.DateFrom.HasValue) orders = orders.Where(o => o.Date >= q.DateFrom);
        if (q.DateTo.HasValue) orders = orders.Where(o => o.Date < q.EndExclusive());
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
        return _db.Set<Vehicle>().AsNoTracking().Where(v => v.Status == VehicleStatus.Available || v.Status == VehicleStatus.Reserved)
            .Select(v => new { v, Hours = EF.Functions.DateDiffHour(v.ReceivedAt ?? v.CreatedAt, now) })
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
            PurchaseCost = c.MarginBaseCost ?? c.CostPrice, SellingPrice = c.TotalWithVat,
            // الهامش الخاضع: السعر − أساس الهامش الذي احتُسبت عليه الضريبة
            GrossProfitMargin = c.TotalWithVat > (c.MarginBaseCost ?? c.CostPrice) ? c.TotalWithVat - (c.MarginBaseCost ?? c.CostPrice) : 0m,
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
            .Where(c => c.Status != SalesContractStatus.Cancelled && c.Status != SalesContractStatus.Returned
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

    /// <summary>
    /// السيارات المباعة بنظام هامش الربح التي يختلف هامشها وضريبتها بين الطريقتين: الهامش من سعر الشراء فقط، أو من التكلفة الكلية.
    /// يساعد المحاسب على تقدير أثر اختيار أساس الهامش على ما سبق بيعه.
    /// </summary>
    public async Task<LoadResult> LoadMarginBasisComparisonAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
    {
        var rate = (await CarTax.LoadAsync(_db, ct)).Rate;
        var rows = from c in Sold(q).Where(c => c.VatMode == VatMode.ProfitMargin_15 || c.VatMode == VatMode.MarginScheme)
                   join v in _db.Set<Vehicle>().AsNoTracking() on c.VehicleId equals v.Id
                   select new { c, Purchase = v.PurchasePrice ?? 0m };
        var margins = rows.Select(r => new
        {
            r.c, r.Purchase,
            OnPurchase = r.c.TotalWithVat > r.Purchase ? r.c.TotalWithVat - r.Purchase : 0m,
            OnCost = r.c.TotalWithVat > r.c.CostPrice ? r.c.TotalWithVat - r.c.CostPrice : 0m,
        });
        var query = margins.Where(m => m.OnPurchase != m.OnCost).Select(m => new CarMarginBasisComparisonRowDto
        {
            Id = m.c.Id, ContractNumber = m.c.ContractNumber, Date = m.c.Date, Vin = m.c.Vin, VehicleDescription = m.c.VehicleDescription,
            SellingPrice = m.c.TotalWithVat, PurchasePrice = m.Purchase, TotalCost = m.c.CostPrice,
            MarginOnPurchasePrice = m.OnPurchase, VatOnPurchasePrice = Math.Round(m.OnPurchase * rate / (100m + rate), 2),
            MarginOnTotalCost = m.OnCost, VatOnTotalCost = Math.Round(m.OnCost * rate / (100m + rate), 2),
            AppliedVat = m.c.VatAmount,
            VatDifference = Math.Round(m.OnPurchase * rate / (100m + rate), 2) - Math.Round(m.OnCost * rate / (100m + rate), 2),
        });
        return await ReportLoader.LoadAsync(query, options, nameof(CarMarginBasisComparisonRowDto.Id), ct, EntityLoader.Asc(nameof(CarMarginBasisComparisonRowDto.Date)));
    }

    // ---------------- تقارير المصاريف والتكاليف الإضافية ----------------
    private IQueryable<AdditionalChargeLine> Charges => _db.Set<AdditionalChargeLine>().AsNoTracking();
    private IQueryable<AdditionalChargeAllocation> Shares => _db.Set<AdditionalChargeAllocation>().AsNoTracking();

    /// <summary>ربح كل سيارة مباعة بعد كل المصاريف: تكلفة المبيعات، ما وصل بعد البيع، مصاريف المعرض عليها، وتكلفة ما حُمّل على العميل.</summary>
    private IQueryable<CarVehicleNetProfitRowDto> VehicleNetProfitQuery(CarReportQueryDto q)
        => Sold(q).Select(c => new
        {
            c,
            VehicleRevenue = c.NetPriceBeforeVat ?? c.SellingPrice,
            Adjustments = _db.Set<VehicleCostAdjustment>().Where(a => a.ContractId == c.Id).Sum(a => (decimal?)a.Amount) ?? 0m,
            Expenses = Shares.Where(a => a.VehicleId == c.VehicleId && a.Line.Status == ChargeStatus.Actual && a.Line.Treatment == ChargeTreatment.Expense).Sum(a => (decimal?)a.Amount) ?? 0m,
            ChargesCost = Charges.Where(l => l.DocumentType == ChargeDocumentTypes.SalesContract && l.DocumentId == c.Id
                && l.Treatment == ChargeTreatment.RechargeToCustomer && l.Status == ChargeStatus.Actual).Sum(l => (decimal?)l.CostAmount) ?? 0m,
        }).Select(r => new
        {
            r.c, r.VehicleRevenue, r.Adjustments, r.Expenses, r.ChargesCost,
            Revenue = r.VehicleRevenue + r.c.CustomerChargesNet,
            Profit = r.VehicleRevenue + r.c.CustomerChargesNet - r.c.CostPrice - r.Adjustments - r.Expenses - r.ChargesCost,
        }).Select(r => new CarVehicleNetProfitRowDto
        {
            Id = r.c.Id, ContractNumber = r.c.ContractNumber, Date = r.c.Date, Vin = r.c.Vin, VehicleDescription = r.c.VehicleDescription, BuyerName = r.c.BuyerName,
            VehicleRevenue = r.VehicleRevenue, CustomerChargesRevenue = r.c.CustomerChargesNet, CostAtSale = r.c.CostPrice,
            PostSaleAdjustments = r.Adjustments, ShowroomExpenses = r.Expenses, CustomerChargesCost = r.ChargesCost,
            NetProfit = r.Profit, NetProfitPercent = r.Revenue == 0 ? 0m : Math.Round(r.Profit / r.Revenue * 100, 2),
        });

    /// <summary>المتوقع مقابل الفعلي لكل مصروف: فعلي له تقدير سابق (استبدله أو تحقق منه)، وتقدير لم يتحقق بعد.</summary>
    private IQueryable<CarEstimatedVsActualRowDto> EstimatedVsActualQuery(CarReportQueryDto q)
    {
        var lines = Charges.Where(l => l.Status == ChargeStatus.Estimated
            || (l.Status == ChargeStatus.Actual && (l.ReplacesLineId != null || l.EstimatedCostAmount != null)));
        if (q.DateFrom.HasValue) lines = lines.Where(l => l.Date >= q.DateFrom);
        if (q.DateTo.HasValue) lines = lines.Where(l => l.Date < q.EndExclusive());
        return lines.Select(l => new
        {
            l,
            Estimated = l.Status == ChargeStatus.Estimated ? l.CostAmount
                : l.EstimatedCostAmount ?? Charges.Where(r => r.Id == l.ReplacesLineId).Select(r => (decimal?)r.CostAmount).FirstOrDefault() ?? 0m,
        }).Select(r => new CarEstimatedVsActualRowDto
        {
            Id = r.l.Id, Date = r.l.Date, ExpenseTypeCode = r.l.ExpenseTypeCode, ExpenseTypeNameAr = r.l.ExpenseTypeNameAr,
            DocumentType = r.l.DocumentType, DocumentNumber = r.l.DocumentNumber, Treatment = r.l.Treatment, Status = r.l.Status,
            Estimated = r.Estimated,
            Actual = r.l.Status == ChargeStatus.Actual ? r.l.CostAmount : null,
            Variance = r.l.Status == ChargeStatus.Actual ? r.l.CostAmount - r.Estimated : null,
        });
    }

    /// <summary>رسوم تم الشهرية حسب النوع (العمليات الناجحة فقط).</summary>
    private IQueryable<CarTammMonthlyFeesRowDto> TammMonthlyFeesQuery(CarReportQueryDto q)
    {
        var lines = Charges.Where(l => l.Source == ChargeSource.Tamm && l.Status == ChargeStatus.Actual);
        if (q.DateFrom.HasValue) lines = lines.Where(l => l.Date >= q.DateFrom);
        if (q.DateTo.HasValue) lines = lines.Where(l => l.Date < q.EndExclusive());
        return lines.GroupBy(l => new { l.Date.Year, l.Date.Month, l.ExpenseTypeCode, l.ExpenseTypeNameAr })
            .Select(g => new CarTammMonthlyFeesRowDto
            {
                Key = g.Key.Year.ToString() + "-" + (g.Key.Month < 10 ? "0" : "") + g.Key.Month.ToString() + "|" + g.Key.ExpenseTypeCode,
                Year = g.Key.Year, Month = g.Key.Month, ExpenseTypeCode = g.Key.ExpenseTypeCode, ExpenseTypeNameAr = g.Key.ExpenseTypeNameAr,
                OperationsCount = g.Count(), TotalFees = g.Sum(l => l.BaseAmount),
            });
    }

    /// <summary>مصاريف على سيارات لم تُشترَ: استعلامات تم قبل الشراء، ومصاريف فعلية على سيارات أُلغي شراؤها.</summary>
    private IQueryable<CarUnpurchasedVehicleExpenseRowDto> UnpurchasedVehicleExpensesQuery(CarReportQueryDto q)
    {
        var cancelled = _db.Set<Vehicle>().AsNoTracking().Where(v => v.Status == VehicleStatus.Cancelled);
        var lines = Charges.Where(l => l.Status == ChargeStatus.Actual
            && (l.DocumentType == ChargeDocumentTypes.TammOperation || l.Allocations.Any(a => cancelled.Any(v => v.Id == a.VehicleId))));
        if (q.DateFrom.HasValue) lines = lines.Where(l => l.Date >= q.DateFrom);
        if (q.DateTo.HasValue) lines = lines.Where(l => l.Date < q.EndExclusive());
        return lines.Select(l => new CarUnpurchasedVehicleExpenseRowDto
        {
            Id = l.Id, Date = l.Date, ExpenseTypeNameAr = l.ExpenseTypeNameAr, Source = l.Source, DocumentNumber = l.DocumentNumber, Amount = l.CostAmount,
            PurchaseCancelled = l.DocumentType != ChargeDocumentTypes.TammOperation,
            Vin = l.DocumentType == ChargeDocumentTypes.TammOperation
                ? _db.Set<TammOperation>().Where(o => o.Id == l.TammOperationId).Select(o => o.Vin).FirstOrDefault()
                : cancelled.Where(v => l.Allocations.Any(a => a.VehicleId == v.Id)).Select(v => v.ChassisNumber).FirstOrDefault(),
        });
    }

    /// <summary>سيارات في الطريق وتكلفتها حتى الآن والمتوقعة عند الوصول.</summary>
    private IQueryable<CarVehicleInTransitRowDto> VehiclesInTransitQuery()
    {
        var now = DateTime.UtcNow;
        return _db.Set<Vehicle>().AsNoTracking().Where(v => v.Status == VehicleStatus.InTransit).Select(v => new
        {
            v,
            Actual = Shares.Where(a => a.VehicleId == v.Id && a.Line.Status == ChargeStatus.Actual && a.Line.Treatment == ChargeTreatment.Capitalize).Sum(a => (decimal?)a.Amount) ?? 0m,
            Estimated = Shares.Where(a => a.VehicleId == v.Id && a.Line.Status == ChargeStatus.Estimated && a.Line.Treatment == ChargeTreatment.Capitalize).Sum(a => (decimal?)a.Amount) ?? 0m,
            Order = _db.Set<CarProcurementOrder>().Where(o => o.Id == v.ProcurementOrderId).Select(o => new { o.OrderNumber, o.SupplierName }).FirstOrDefault(),
            Hours = EF.Functions.DateDiffHour(v.CreatedAt, now),
        }).Select(r => new CarVehicleInTransitRowDto
        {
            Id = r.v.Id, ChassisNumber = r.v.ChassisNumber, Description = r.v.BrandNameAr + " " + r.v.ModelNameAr + " " + r.v.Year.ToString(),
            OrderNumber = r.Order!.OrderNumber, SupplierName = r.Order.SupplierName, DaysInTransit = r.Hours > 0 ? r.Hours / 24 : 0,
            PurchasePrice = r.v.PurchasePrice ?? 0m, ActualCharges = r.Actual, EstimatedCharges = r.Estimated,
            CostToDate = (r.v.PurchasePrice ?? 0m) + r.Actual, ExpectedCost = (r.v.PurchasePrice ?? 0m) + r.Actual + r.Estimated,
        });
    }

    /// <summary>عمر المخزون مع تكلفته: الأيام منذ دخول السيارة المخزون (الاستلام)، بشرائح عمرية.</summary>
    private IQueryable<CarInventoryAgingRowDto> InventoryAgingQuery()
    {
        var now = DateTime.UtcNow;
        return _db.Set<Vehicle>().AsNoTracking().Where(v => v.Status == VehicleStatus.Available || v.Status == VehicleStatus.Reserved)
            .Select(v => new { v, Hours = EF.Functions.DateDiffHour(v.ReceivedAt ?? v.CreatedAt, now) })
            .Select(r => new { r.v, Days = r.Hours > 0 ? r.Hours / 24 : 0 })
            .Select(r => new CarInventoryAgingRowDto
            {
                Id = r.v.Id, ChassisNumber = r.v.ChassisNumber, Description = r.v.BrandNameAr + " " + r.v.ModelNameAr + " " + r.v.Year.ToString(), Status = r.v.Status,
                DaysInStock = r.Days, AgeBucket = r.Days <= 30 ? "0-30" : r.Days <= 60 ? "31-60" : r.Days <= 90 ? "61-90" : r.Days <= 180 ? "91-180" : "180+",
                PurchasePrice = r.v.PurchasePrice ?? 0m, AdditionalCosts = r.v.AdditionalCosts ?? 0m, PreparationCost = r.v.PreparationCost ?? 0m,
                TotalCost = r.v.TotalCost, SellingPrice = r.v.SellingPrice,
            });
    }

    public Task<LoadResult> LoadVehicleNetProfitAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(VehicleNetProfitQuery(q), options, nameof(CarVehicleNetProfitRowDto.Id), ct, EntityLoader.Asc(nameof(CarVehicleNetProfitRowDto.Date)));
    public Task<LoadResult> LoadEstimatedVsActualAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(EstimatedVsActualQuery(q), options, nameof(CarEstimatedVsActualRowDto.Id), ct, EntityLoader.Asc(nameof(CarEstimatedVsActualRowDto.Date)));
    public Task<LoadResult> LoadTammMonthlyFeesAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(TammMonthlyFeesQuery(q), options, nameof(CarTammMonthlyFeesRowDto.Key), ct, EntityLoader.Asc(nameof(CarTammMonthlyFeesRowDto.Key)));
    public Task<LoadResult> LoadUnpurchasedVehicleExpensesAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(UnpurchasedVehicleExpensesQuery(q), options, nameof(CarUnpurchasedVehicleExpenseRowDto.Id), ct, EntityLoader.Asc(nameof(CarUnpurchasedVehicleExpenseRowDto.Date)));
    public Task<LoadResult> LoadVehiclesInTransitAsync(DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(VehiclesInTransitQuery(), options, nameof(CarVehicleInTransitRowDto.Id), ct, EntityLoader.Desc(nameof(CarVehicleInTransitRowDto.DaysInTransit)));
    public Task<LoadResult> LoadInventoryAgingAsync(DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(InventoryAgingQuery(), options, nameof(CarInventoryAgingRowDto.Id), ct, EntityLoader.Desc(nameof(CarInventoryAgingRowDto.DaysInStock)));

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
