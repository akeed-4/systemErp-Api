using System.Text.Json.Nodes;
using ERP.Core.Models.Shared;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>التقارير تُحسب في قاعدة البيانات: الأرصدة الجارية والمجاميع صحيحة حتى مع الترقيم والفرز والفلترة.</summary>
[Collection("api")]
public class ReportQueryTests : TestBase
{
    public ReportQueryTests(ErpFactory f) : base(f) { }

    private static string Q(params (string Key, string Json)[] parts)
        => string.Join("&", parts.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Json)}"));

    private static async Task<JsonArray> RowsAsync(Client api, string path)
    {
        var r = await api.Get(path);
        Assert.True(r.Status == 200, $"{path} → {r.Status}: {r.Body}");
        return r.Data!.AsArray();
    }

    private static object Sale(Guid product, decimal qty, string method, Guid? party = null)
        => new { kind = "sales", invoiceType = "simplified", paymentMethod = method, partyId = party, items = new[] { new { itemId = product, quantity = qty, unitPrice = 100, vatRate = 15 } } };

    /// <summary>مخزون افتتاحي 10 بتكلفة 40، بيع 2 نقداً و3 آجلاً، سند قبض 100، ثم شراء 4 بتكلفة 50.</summary>
    private async Task<(Client Api, Guid Product, string CustomerAccount)> SeededAsync()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        var (customer, account) = await SeedCustomerAsync(api);
        var (supplier, _) = await SeedSupplierAsync(api);
        Assert.Equal(201, (await api.PostInvoice(Sale(product, 2, "cash"))).Status);
        Assert.Equal(201, (await api.PostInvoice(Sale(product, 3, "credit", customer))).Status);
        await api.Post("/vouchers", new { type = "receipt", amount = 100, partyName = "عميل", partyAccountCode = account, treasuryAccountCode = "1111", paymentMethod = "cash" });
        await api.PostInvoice(new { kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier, items = new[] { new { itemId = product, quantity = 4, unitPrice = 50, vatRate = 15 } } });
        await MarkSalesReportedToZatcaAsync(api); // التقرير التجاري يعدّ المبيعات المُبلَّغة للهيئة فقط
        return (api, product, account);
    }

    [Fact]
    public async Task Account_statement_running_balance_survives_paging_sorting_and_period()
    {
        var (api, _, account) = await SeededAsync();
        // سند القبض مؤرَّخ بأول اليوم فيسبق فاتورة اليوم نفسه
        var entries = await RowsAsync(api, $"/reports/AccountStatement/{account}/Entries");
        Assert.Equal(new[] { -100m, 245m }, entries.Select(e => e!["runningBalance"].D()));

        // الصفحة الثانية وحدها: رصيدها الجاري يشمل ما قبلها
        var second = await RowsAsync(api, $"/reports/AccountStatement/{account}/Entries?" + Q(("skip", "1"), ("take", "1")));
        Assert.Equal(245, Assert.Single(second)!["runningBalance"].D());
        var credits = await RowsAsync(api, $"/reports/AccountStatement/{account}/Entries?" + Q(("filter", "[\"credit\",\">\",0]")));
        Assert.Equal(-100, Assert.Single(credits)!["runningBalance"].D());
        var last = await RowsAsync(api, $"/reports/AccountStatement/{account}/Entries?" + Q(("sort", "[{\"selector\":\"runningBalance\",\"desc\":false}]"), ("take", "1")));
        Assert.Equal(100, last[0]!["credit"].D());

        // فترة تبدأ بعد كل الحركات: كلها رصيد افتتاحي
        var later = (await api.Get($"/reports/AccountStatement/{account}?from=2100-01-01")).Data!;
        Assert.Equal(245, later["openingBalance"].D()); Assert.Equal(245, later["closingBalance"].D()); Assert.Empty(later["entries"]!.AsArray());
        var whole = (await api.Get($"/reports/AccountStatement/{account}")).Data!;
        Assert.Equal(0, whole["openingBalance"].D()); Assert.Equal(245, whole["closingBalance"].D());
        Assert.Equal(new[] { -100m, 245m }, whole["entries"]!.AsArray().Select(e => e!["runningBalance"].D()));
    }

    [Fact]
    public async Task Trial_balance_splits_opening_from_period_movement()
    {
        var (api, _, account) = await SeededAsync();
        var row = Assert.Single(await RowsAsync(api, "/reports/TrialBalance?" + Q(("filter", $"[\"accountCode\",\"=\",\"{account}\"]"))));
        Assert.Equal(345, row!["periodDebit"].D()); Assert.Equal(100, row["periodCredit"].D());
        Assert.Equal(0, row["openingDebit"].D()); Assert.Equal(245, row["closingDebit"].D()); Assert.Equal(0, row["closingCredit"].D());
        Assert.False(string.IsNullOrEmpty(row["accountNameAr"].S()));

        var later = Assert.Single(await RowsAsync(api, "/reports/TrialBalance?from=2100-01-01&" + Q(("filter", $"[\"accountCode\",\"=\",\"{account}\"]"))));
        Assert.Equal(245, later!["openingDebit"].D()); Assert.Equal(0, later["periodDebit"].D()); Assert.Equal(245, later["closingDebit"].D());
        Assert.Empty(await RowsAsync(api, "/reports/TrialBalance?to=2000-01-01"));
    }

    [Fact]
    public async Task Period_ending_today_includes_documents_issued_during_the_day()
    {
        // الفاتورة تحمل وقت إصدارها؛ فترة تنتهي باليوم نفسه (to بلا وقت) تشمل اليوم كله
        var (api, _, account) = await SeededAsync();
        var today = SaudiTime.Today.ToString("yyyy-MM-dd");
        var row = Assert.Single(await RowsAsync(api, $"/reports/TrialBalance?from={today}&to={today}&" + Q(("filter", $"[\"accountCode\",\"=\",\"{account}\"]"))));
        Assert.Equal(345, row!["periodDebit"].D()); Assert.Equal(245, row["closingDebit"].D());

        var statement = (await api.Get($"/reports/AccountStatement/{account}?from={today}&to={today}")).Data!;
        Assert.Equal(245, statement["closingBalance"].D());
        Assert.Equal(2, statement["entries"]!.AsArray().Count);
        Assert.Equal(2, (await RowsAsync(api, $"/reports/AccountStatement/{account}/Entries?from={today}&to={today}")).Count);

        // تقارير المخزون والتجارة: الواجهة ترسل dateTo بتاريخ اليوم بلا وقت
        Assert.Equal(3, (await RowsAsync(api, $"/reports/TradeCommercial?dateFrom={today}&dateTo={today}")).Count);
    }

    [Fact]
    public async Task Trade_report_counts_posted_purchases_and_only_zatca_accepted_sales_per_module()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        var (supplier, _) = await SeedSupplierAsync(api);
        Assert.Equal(201, (await api.PostInvoice(Sale(product, 1, "cash"))).Status);
        Assert.Equal(201, (await api.PostInvoice(new { kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "cash", partyId = supplier,
            items = new[] { new { itemId = product, quantity = 2, unitPrice = 50, vatRate = 15 } } })).Status);

        // البيع لم يُرسل للهيئة بعد: المشتريات المرحّلة وحدها
        var purchase = Assert.Single(await RowsAsync(api, "/reports/TradeCommercial?module=accounting"));
        Assert.Equal("purchase", purchase!["type"].S());

        await MarkSalesReportedToZatcaAsync(api);
        var rows = await RowsAsync(api, "/reports/TradeCommercial?module=accounting");
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r!["type"].S() == "sales" && r["grossProfit"].D() == 60); // 100 − تكلفة 40
        Assert.Empty(await RowsAsync(api, "/reports/TradeCommercial?module=car_showroom")); // فواتير المحاسبة لا تظهر في المعارض
    }

    [Fact]
    public async Task Item_ledger_and_movement_summary_are_computed_in_the_database()
    {
        var (api, product, _) = await SeededAsync();
        var ledger = await RowsAsync(api, $"/reports/ItemLedger?itemId={product}");
        Assert.Equal(new[] { 10m, 8m, 5m, 9m }, ledger.Select(r => r!["runningStockBalance"].D()));
        Assert.Equal(new[] { 400m, 320m, 200m, 400m }, ledger.Select(r => r!["runningStockValue"].D()));
        Assert.Equal("adjustment_in", ledger[0]!["docType"].S()); Assert.Equal(10, ledger[0]!["quantityIn"].D()); Assert.Equal(2, ledger[1]!["quantityOut"].D());
        var page = await RowsAsync(api, $"/reports/ItemLedger?itemId={product}&" + Q(("skip", "3"), ("take", "1")));
        Assert.Equal(9, Assert.Single(page)!["runningStockBalance"].D());
        Assert.Empty(await RowsAsync(api, $"/reports/ItemLedger?itemId={product}&dateFrom=2100-01-01"));
        // نوع الحركة: التسويات وحدها (والرصيد الجاري يبقى محسوباً على كل الحركات)
        var adjustments = await RowsAsync(api, $"/reports/ItemLedger?itemId={product}&movementType=adjustment");
        Assert.All(adjustments, r => Assert.StartsWith("adjustment_", r!["docType"].S()));
        Assert.Equal(10, adjustments[0]!["runningStockBalance"].D());
        Assert.Equal(400, (await api.Get("/reports/ItemLedger")).Status);

        var summary = Assert.Single(await RowsAsync(api, $"/reports/ItemMovements?itemId={product}"));
        Assert.Equal(0, summary!["openingStock"].D()); Assert.Equal(4, summary["purchaseInQty"].D()); Assert.Equal(5, summary["salesOutQty"].D());
        Assert.Equal(9, summary["closingStock"].D()); Assert.Equal(Math.Round(9 * summary["averageCost"].D(), 2), summary["totalStockValue"].D());
        var later = Assert.Single(await RowsAsync(api, $"/reports/ItemMovements?itemId={product}&dateFrom=2100-01-01"));
        Assert.Equal(9, later!["openingStock"].D()); Assert.Equal(0, later["purchaseInQty"].D()); Assert.Equal(9, later["closingStock"].D());
        // صنف بلا حركات يظهر بأصفار
        await api.Post("/products", new { sku = "P2", nameAr = "منتج P2", category = "C1", unit = "PCS", sellingPrice = 10, vatRate = 15 });
        var idle = Assert.Single(await RowsAsync(api, "/reports/ItemMovements?" + Q(("filter", "[\"sku\",\"=\",\"P2\"]"))));
        Assert.Equal(0, idle!["closingStock"].D());

        var trade = await RowsAsync(api, "/reports/TradeCommercial?" + Q(("filter", "[\"grandTotal\",\"=\",345]")));
        var sale = Assert.Single(trade);
        Assert.Equal(1, sale!["itemsCount"].D()); Assert.Equal(300, sale["subtotal"].D()); Assert.Equal(60, sale["grossProfitMarginPercent"].D());
        var audit = Assert.Single(await RowsAsync(api, $"/reports/InventoryAudit?itemId={product}"));
        Assert.Equal(9, audit!["systemQuantity"].D()); Assert.Equal(Math.Round(9 * audit["averageUnitCost"].D(), 2), audit["systemValuation"].D());
    }

    [Fact]
    public async Task Car_reports_return_real_rows_from_the_database()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var order = await api.Post("/carprocurementorders", new
        {
            supplierId = supplier, paymentType = "credit", creditDays = 30, currency = "SAR", exchangeRate = 1, date = DateTime.UtcNow,
            items = new[] { new { brandName = "تويوتا", modelName = "كامري", year = 2025, quantity = 2, unitPrice = 80000 }, new { brandName = "هوندا", modelName = "أكورد", year = 2025, quantity = 1, unitPrice = 50000 } },
        });
        Assert.Equal(201, order.Status);
        var orderId = order.Data!["id"].S();
        foreach (var stage in new[] { "requisition_approved", "rfq", "rfq_approved", "purchase_order" })
            Assert.Equal(200, (await api.Post($"/carprocurementorders/{orderId}/AdvanceStage", new { targetStage = stage })).Status);
        Assert.Equal(200, (await api.Post($"/carprocurementorders/{orderId}/AdvanceStage", new
        {
            targetStage = "vin_received", pdiInspectionPassed = true, warehouseLocation = "المعرض",
            vins = new[] { new { vin = Client.NewVin('1') }, new { vin = Client.NewVin('2') }, new { vin = Client.NewVin('3') } },
        })).Status);

        var car = await api.Post("/vehicles", new { chassisNumber = Client.NewVin('4'), brandNameAr = "هوندا", modelNameAr = "أكورد", year = 2020, condition = "used", mileageKm = 60000,
            purchasePrice = 50000, sellingPrice = 60000, vatMode = "profit_margin_15", fuelType = "petrol", transmission = "automatic", colorExterior = "أسود", colorInterior = "بيج", location = "المعرض" });
        var contract = await api.Post("/carsalescontracts", new { cycleType = "individual", buyerName = "م", buyerNationalIdOrCr = "2", buyerPhone = "2", vehicleId = car.Data!["id"].S(), sellingPrice = 60000, vatMode = "profit_margin_15", paymentMethod = "bank_transfer", condition = "used" });
        var id = contract.Data!["id"].S();
        foreach (var s in new[] { "approved", "allocated" }) await api.Post($"/carsalescontracts/{id}/AdvanceStatus", new { targetStatus = s });
        await api.Post($"/carsalescontracts/{id}/AdvanceStatus", new { targetStatus = "delivered", handoverProtocolNumber = "H", handoverSignee = "م", handoverSigneeNationalId = "2" });
        Assert.Equal(200, (await api.Post($"/carsalescontracts/{id}/AdvanceStatus", new { targetStatus = "invoiced" })).Status);

        var tracking = Assert.Single(await RowsAsync(api, "/reports/car/ProcurementTracking"));
        Assert.Equal(3, tracking!["requestedQty"].D()); Assert.Equal(3, tracking["receivedVinQty"].D()); Assert.Equal(65000, tracking["unitPrice"].D());
        Assert.Contains("تويوتا كامري", tracking["brandAndModel"].S()); Assert.Contains("هوندا أكورد", tracking["brandAndModel"].S());

        var suppliers = Assert.Single(await RowsAsync(api, "/reports/car/SuppliersProcurement"));
        Assert.Equal(1, suppliers!["totalOrdersCount"].D()); Assert.Equal(3, suppliers["vehiclesCount"].D());
        Assert.Equal(1, suppliers["inProgressOrdersCount"].D()); Assert.Equal("مورد", suppliers["supplierName"].S());

        Assert.Equal(3, (await RowsAsync(api, "/reports/car/VinInventory")).Count); // المباعة لا تظهر
        var performance = Assert.Single(await RowsAsync(api, "/reports/car/SalesPerformance"));
        Assert.Equal(58695.65m, performance!["sellingPrice"].D());
        var pl = Assert.Single(await RowsAsync(api, "/reports/car/ProfitLoss"));
        Assert.Equal(8695.65m, pl!["profitAmount"].D()); Assert.True(pl["isProfit"]!.GetValue<bool>()); Assert.Equal(14.81m, pl["profitPercentage"].D());
        Assert.False(string.IsNullOrEmpty(pl["invoiceNumber"].S()));
        Assert.Single(await RowsAsync(api, "/reports/car/ZatcaMarginTax"));

        var today = DateTime.UtcNow;
        var monthly = Assert.Single(await RowsAsync(api, "/reports/car/DailyMonthlySales"));
        Assert.Equal(today.ToString("yyyy-MM"), monthly!["periodKey"].S()); Assert.Equal("monthly", monthly["periodType"].S());
        Assert.Equal(1, monthly["carsSoldCount"].D()); Assert.Equal(1, monthly["cashSalesCount"].D()); Assert.Equal(58695.65m, monthly["averageCarPrice"].D());
        Assert.Equal(new DateTime(today.Year, today.Month, 1).AddMonths(1).AddDays(-1), monthly["endDate"]!.GetValue<DateTime>());
        var daily = Assert.Single(await RowsAsync(api, "/reports/car/DailyMonthlySales?period=daily"));
        Assert.Equal(today.ToString("yyyy-MM-dd"), daily!["periodKey"].S()); Assert.Equal(daily["startDate"]!.GetValue<DateTime>(), daily["endDate"]!.GetValue<DateTime>());
    }
}
