using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// تكاليف الاستيراد المقدمة: تُدفع لأمر شراء قبل وصول سياراته (مدين تكاليف استيراد تحت التسوية + ضريبة المدخلات، دائن البنك)،
/// وتُسوّى بسحبها في سند التكاليف الإضافية (دائن حساب التسوية لا المورد) فتُوزَّع على السيارات، وإلغاء السند يعيد رصيدها.
/// </summary>
[Collection("api")]
public class ImportCostAdvanceTests : TestBase
{
    public ImportCostAdvanceTests(ErpFactory f) : base(f) { }

    private const string Clearing = "1144", InTransit = "1143", InputVat = "1131", Bank = "1112";

    private static async Task<Dictionary<string, string>> TypeIdsAsync(Client api)
        => (await api.Get("/expensetypes?pageSize=200")).Data!["items"]!.AsArray().ToDictionary(t => t!["code"].S(), t => t!["id"].S());

    private static async Task<decimal> BalanceAsync(Client api, string code)
    {
        var r = await api.Get($"/accounts/ByCode/{code}");
        return r.Status == 200 ? r.Data!["balance"].D() : 0;
    }

    private static async Task AssertBalancedAsync(Client api)
    {
        decimal d = 0, c = 0;
        foreach (var row in (await api.Get("/reports/TrialBalance")).Data!.AsArray()) { d += row!["periodDebit"].D(); c += row["periodCredit"].D(); }
        Assert.Equal(d, c);
    }

    /// <summary>أمر شراء في مرحلة أمر الشراء: سياراته في الطريق (بلا رسوم جمارك/موانئ على الأمر).</summary>
    private static async Task<string> OrderAsync(Client api, int qty = 2)
    {
        var (supplier, _) = await SeedSupplierAsync(api, "مورد السيارات " + Guid.NewGuid().ToString("N")[..4]);
        var order = await api.Post("/carprocurementorders", new
        {
            supplierId = supplier, paymentType = "credit", creditDays = 30, currency = "SAR", exchangeRate = 1, date = DateTime.UtcNow,
            customsDutyFee = 0, portStorageFee = 0,
            items = new[] { new { brandName = "تويوتا", modelName = "كامري", year = 2025, quantity = qty, unitPrice = 80000 } },
        });
        Assert.Equal(201, order.Status);
        var id = order.Data!["id"].S();
        foreach (var stage in new[] { "requisition_approved", "rfq", "rfq_approved", "purchase_order" })
            Assert.Equal(200, (await api.Post($"/carprocurementorders/{id}/AdvanceStage", new { targetStage = stage })).Status);
        return id;
    }

    private static async Task<List<JsonNode>> OrderVehiclesAsync(Client api, string orderId)
        => (await api.Get($"/carprocurementorders/{orderId}/vehicles?pageSize=50")).Data!["items"]!.AsArray().Select(v => v!).ToList();

    private static async Task BankMethodAsync(Client api)
        => Assert.Equal(201, (await api.Post("/paymentmethods", new { code = "BANK", nameAr = "تحويل بنكي", nameEn = "Bank", type = "bank", linkedAccountCode = Bank, status = "active" })).Status);

    private static object Advance(string orderId, string typeId, decimal amount, decimal vat = 0, string? currency = null, decimal rate = 1, string? method = "BANK") => new
    {
        procurementOrderId = orderId, expenseTypeId = typeId, description = "شحن بحري مقدم", supplierInvoiceNumber = "SHIP-1",
        currencyCode = currency, exchangeRate = rate, amount, vatAmount = vat, paymentMethodCode = method,
    };

    private static async Task<JsonNode> ApprovedAdvanceAsync(Client api, object body)
    {
        var created = await api.Post("/importcostadvances", body);
        Assert.Equal(201, created.Status);
        var approved = await api.Post($"/importcostadvances/{created.Data!["id"].S()}/approve");
        Assert.Equal(200, approved.Status);
        return approved.Data!;
    }

    private static object Pull(string advanceId, string typeId, decimal amount, decimal vat = 0) => new { expenseTypeId = typeId, importCostAdvanceId = advanceId, amount, vatAmount = vat, allocationMethod = "equal" };

    private static object Voucher(string orderId, string? supplier, string? currency, params object[] lines) => new
    {
        date = DateTime.UtcNow.Date, supplierId = supplier, currencyCode = currency, exchangeRate = currency == null ? 1 : 3.75m, procurementOrderId = orderId, lines,
    };

    [Fact]
    public async Task Advance_is_a_draft_until_approved_then_posts_the_payment_and_is_locked()
    {
        var api = await NewTenantAsync();
        var types = await TypeIdsAsync(api);
        await BankMethodAsync(api);
        var orderId = await OrderAsync(api);

        // التحقق: طريقة الدفع إلزامية، والمبلغ موجب، ونوع «ضريبة استيراد» لا تكلفة فيه، وأمر الشراء موجود
        Assert.Equal(400, (await api.Post("/importcostadvances", Advance(orderId, types["FREIGHT"], 1000, method: null))).Status);
        Assert.Equal(400, (await api.Post("/importcostadvances", Advance(orderId, types["FREIGHT"], 0))).Status);
        Assert.Equal(400, (await api.Post("/importcostadvances", Advance(orderId, types["IMPORT_VAT"], 1000))).Status);
        Assert.Equal(400, (await api.Post("/importcostadvances", Advance(Guid.NewGuid().ToString(), types["FREIGHT"], 1000))).Status);

        var draft = await api.Post("/importcostadvances", Advance(orderId, types["FREIGHT"], 1000, vat: 150));
        Assert.Equal(201, draft.Status);
        var id = draft.Data!["id"].S();
        Assert.StartsWith("ICA-", draft.Data["number"].S());
        Assert.Equal("draft", draft.Data["status"].S());
        Assert.Equal(1000, draft.Data["costAmount"].D()); Assert.Equal(150, draft.Data["inputVatAmount"].D());
        Assert.Equal(Bank, draft.Data["paymentAccountCode"].S());
        Assert.Equal(0, await BalanceAsync(api, Clearing)); // المسودة بلا قيد
        Assert.Equal(200, (await api.Put($"/importcostadvances/{id}", Advance(orderId, types["FREIGHT"], 1200, vat: 180))).Status);

        // الاعتماد بصلاحية اعتماد السندات
        var email = $"u{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await api.Post("/users", new { name = "مندوب", email, phone = "1", password = "Passw0rd!", role = "sales_rep" })).Status);
        var rep = await api.LoginAsAsync(NewHttp(), email, "Passw0rd!");
        Assert.Equal(403, (await rep.Post($"/importcostadvances/{id}/approve")).Status);
        var approved = await api.Post($"/importcostadvances/{id}/approve");
        Assert.Equal(200, approved.Status);
        Assert.Equal("open", approved.Data!["status"].S());
        Assert.Equal(1200, approved.Data["remainingAmount"].D()); Assert.Equal(1200, approved.Data["remainingCost"].D());

        // مدين التسوية بالتكلفة ومدين ضريبة المدخلات، دائن البنك بالمدفوع
        Assert.Equal(1200, await BalanceAsync(api, Clearing));
        Assert.Equal(180, await BalanceAsync(api, InputVat));
        Assert.Equal(-1380, await BalanceAsync(api, Bank));
        await AssertBalancedAsync(api);

        // المعتمد لا يُعدَّل ولا يُعتمد ثانية ولا يُحذف؛ ويُلغى قبل أي تسوية بعكس قيده
        Assert.Equal(409, (await api.Put($"/importcostadvances/{id}", Advance(orderId, types["FREIGHT"], 1))).Status);
        Assert.Equal(409, (await api.Post($"/importcostadvances/{id}/approve")).Status);
        Assert.Equal(409, (await api.Delete($"/importcostadvances/{id}")).Status);
        var cancelled = await api.Post($"/importcostadvances/{id}/cancel");
        Assert.Equal(200, cancelled.Status);
        Assert.Equal("cancelled", cancelled.Data!["status"].S());
        Assert.Equal(0, await BalanceAsync(api, Clearing)); Assert.Equal(0, await BalanceAsync(api, InputVat)); Assert.Equal(0, await BalanceAsync(api, Bank));
        Assert.Equal(409, (await api.Post($"/importcostadvances/{id}/cancel")).Status);
        await AssertBalancedAsync(api);
    }

    [Fact]
    public async Task Pulling_advances_into_vouchers_settles_them_partially_and_cancelling_the_voucher_restores_the_balance()
    {
        var api = await NewTenantAsync();
        var types = await TypeIdsAsync(api);
        await BankMethodAsync(api);
        var orderId = await OrderAsync(api);
        var vehicles = await OrderVehiclesAsync(api, orderId);
        Assert.All(vehicles, v => Assert.Equal("in_transit", v["status"].S()));
        var advance = await ApprovedAdvanceAsync(api, Advance(orderId, types["FREIGHT"], 1000, vat: 150));
        var advanceId = advance["id"].S();
        var (shipper, shipperAccount) = await SeedSupplierAsync(api, "شركة التخليص");
        var transitBefore = await BalanceAsync(api, InTransit);

        // الزر في الواجهة يقرأ المفتوحة لأمر الشراء
        var open = (await api.Get($"/importcostadvances/open?procurementOrderId={orderId}")).Data!.AsArray();
        Assert.Equal(advanceId, open.Single()!["id"].S());

        // (1) سند كل أسطره مسحوبة: بلا مورد، 600 من 1000، بلا ضريبة جديدة؛ دائنه حساب التسوية
        var first = await api.Post("/landedcostvouchers", Voucher(orderId, null, null, Pull(advanceId, types["FREIGHT"], 600)));
        Assert.Equal(201, first.Status);
        Assert.Equal(advance["number"].S(), first.Data!["lines"]!.AsArray()[0]!["importCostAdvanceNumber"].S());
        Assert.Equal(1000, (await api.Get($"/importcostadvances/{advanceId}")).Data!["remainingAmount"].D()); // المسودة لا تسوّي
        Assert.Equal(200, (await api.Post($"/landedcostvouchers/{first.Data["id"].S()}/approve")).Status);
        var a = (await api.Get($"/importcostadvances/{advanceId}")).Data!;
        Assert.Equal("partially_settled", a["status"].S());
        Assert.Equal(600, a["settledAmount"].D()); Assert.Equal(400, a["remainingAmount"].D()); Assert.Equal(400, a["remainingCost"].D());
        Assert.Equal(400, await BalanceAsync(api, Clearing));
        Assert.Equal(transitBefore + 600, await BalanceAsync(api, InTransit)); // السيارات في الطريق
        Assert.Equal(150, await BalanceAsync(api, InputVat));                   // لا ضريبة ثانية
        Assert.All(await OrderVehiclesAsync(api, orderId), v => Assert.Equal(80300, v["totalCost"].D()));
        await AssertBalancedAsync(api);

        // (2) الباقي 400 مع سطر عادي على مورد التخليص (200 + ضريبة 30): المسحوب على التسوية والعادي على المورد
        var second = await api.Post("/landedcostvouchers", Voucher(orderId, shipper.ToString(), null,
            Pull(advanceId, types["FREIGHT"], 400), new { expenseTypeId = types["CUSTOMS_CLEARANCE"], amount = 200, vatAmount = 30, allocationMethod = "equal" }));
        Assert.Equal(201, second.Status);
        Assert.Equal(200, (await api.Post($"/landedcostvouchers/{second.Data!["id"].S()}/approve")).Status);
        a = (await api.Get($"/importcostadvances/{advanceId}")).Data!;
        Assert.Equal("settled", a["status"].S()); Assert.Equal(0, a["remainingAmount"].D());
        Assert.Equal(0, await BalanceAsync(api, Clearing));
        Assert.Equal(230, await BalanceAsync(api, shipperAccount)); // حساب المورد دائن الطبيعة
        Assert.Equal(180, await BalanceAsync(api, InputVat));
        Assert.Empty((await api.Get($"/importcostadvances/open?procurementOrderId={orderId}")).Data!.AsArray());
        await AssertBalancedAsync(api);

        // مستند مسوّى بالكامل لا يُسحب منه، ولا يُلغى بعد تسويته
        Assert.Equal(400, (await api.Post("/landedcostvouchers", Voucher(orderId, null, null, Pull(advanceId, types["FREIGHT"], 1)))).Status);
        Assert.Equal(409, (await api.Post($"/importcostadvances/{advanceId}/cancel")).Status);

        // (3) إلغاء السند الثاني يعيد ما سحبه للرصيد تلقائياً ويعكس قيده
        Assert.Equal(200, (await api.Post($"/landedcostvouchers/{second.Data["id"].S()}/cancel")).Status);
        a = (await api.Get($"/importcostadvances/{advanceId}")).Data!;
        Assert.Equal("partially_settled", a["status"].S()); Assert.Equal(400, a["remainingAmount"].D());
        Assert.Equal(400, await BalanceAsync(api, Clearing));
        Assert.Equal(0, await BalanceAsync(api, shipperAccount));
        Assert.All(await OrderVehiclesAsync(api, orderId), v => Assert.Equal(80300, v["totalCost"].D()));
        await AssertBalancedAsync(api);

        // سند قديم بلا سحب يعمل كما كان (دائن المورد كاملاً)
        var plain = await api.Post("/landedcostvouchers", Voucher(orderId, shipper.ToString(), null, new { expenseTypeId = types["SABER"], amount = 100, allocationMethod = "equal" }));
        Assert.Equal(200, (await api.Post($"/landedcostvouchers/{plain.Data!["id"].S()}/approve")).Status);
        Assert.Equal(100, await BalanceAsync(api, shipperAccount));
        Assert.Equal(400, await BalanceAsync(api, Clearing));
        await AssertBalancedAsync(api);
    }

    [Fact]
    public async Task Pulling_is_refused_beyond_the_balance_for_another_order_another_currency_with_vat_or_without_a_supplier_for_other_lines()
    {
        var api = await NewTenantAsync();
        var types = await TypeIdsAsync(api);
        await BankMethodAsync(api);
        Assert.Equal(201, (await api.Post("/currencies", new { code = "USD", nameAr = "دولار", nameEn = "US Dollar", symbol = "$", exchangeRate = 3.75 })).Status);
        var orderId = await OrderAsync(api);
        var otherOrder = await OrderAsync(api, qty: 1);
        var advanceId = (await ApprovedAdvanceAsync(api, Advance(orderId, types["FREIGHT"], 1000)))["id"].S();
        var draftAdvance = await api.Post("/importcostadvances", Advance(orderId, types["FREIGHT"], 500));

        Assert.Equal(400, (await api.Post("/landedcostvouchers", Voucher(orderId, null, null, Pull(advanceId, types["FREIGHT"], 1000.01m)))).Status); // يتجاوز الرصيد
        Assert.Equal(400, (await api.Post("/landedcostvouchers", Voucher(orderId, null, null,
            Pull(advanceId, types["FREIGHT"], 700), Pull(advanceId, types["FREIGHT"], 301)))).Status);                                                // مجموع السطرين يتجاوزه
        Assert.Equal(400, (await api.Post("/landedcostvouchers", Voucher(otherOrder, null, null, Pull(advanceId, types["FREIGHT"], 100)))).Status);   // أمر شراء آخر
        Assert.Equal(400, (await api.Post("/landedcostvouchers", Voucher(orderId, null, "USD", Pull(advanceId, types["FREIGHT"], 100)))).Status);      // عملة أخرى
        Assert.Equal(400, (await api.Post("/landedcostvouchers", Voucher(orderId, null, null, Pull(advanceId, types["FREIGHT"], 100, vat: 15)))).Status); // بضريبة
        Assert.Equal(400, (await api.Post("/landedcostvouchers", Voucher(orderId, null, null, Pull(draftAdvance.Data!["id"].S(), types["FREIGHT"], 100)))).Status); // مسودة
        Assert.Equal(400, (await api.Post("/landedcostvouchers", Voucher(orderId, null, null,
            Pull(advanceId, types["FREIGHT"], 100), new { expenseTypeId = types["SABER"], amount = 50, allocationMethod = "equal" }))).Status);       // سطر عادي بلا مورد

        // مسودتان تسحبان الرصيد نفسه: الثانية تُرفض عند الاعتماد بعد اعتماد الأولى
        var v1 = await api.Post("/landedcostvouchers", Voucher(orderId, null, null, Pull(advanceId, types["FREIGHT"], 700)));
        var v2 = await api.Post("/landedcostvouchers", Voucher(orderId, null, null, Pull(advanceId, types["FREIGHT"], 700)));
        Assert.Equal(201, v1.Status); Assert.Equal(201, v2.Status);
        // ولا يُلغى المستند وهو مسحوب في مسودة
        Assert.Equal(409, (await api.Post($"/importcostadvances/{advanceId}/cancel")).Status);
        Assert.Equal(200, (await api.Post($"/landedcostvouchers/{v1.Data!["id"].S()}/approve")).Status);
        Assert.Equal(400, (await api.Post($"/landedcostvouchers/{v2.Data!["id"].S()}/approve")).Status);
        Assert.Equal(300, await BalanceAsync(api, Clearing));
        await AssertBalancedAsync(api);
    }

    [Fact]
    public async Task Last_settlement_takes_the_exact_remaining_cost_and_a_foreign_currency_advance_settles_at_its_own_rate()
    {
        var api = await NewTenantAsync();
        var types = await TypeIdsAsync(api);
        await BankMethodAsync(api);
        Assert.Equal(201, (await api.Post("/currencies", new { code = "USD", nameAr = "دولار", nameEn = "US Dollar", symbol = "$", exchangeRate = 3.75 })).Status);
        var orderId = await OrderAsync(api, qty: 3);

        // 100$ بسعر 3.7533 = 375.33؛ ثلاث تسويات بثلث المبلغ تُقفل الرصيد تماماً بلا هللة متبقية، والسند بسعر آخر لا يغيّر التكلفة
        var advanceId = (await ApprovedAdvanceAsync(api, Advance(orderId, types["FREIGHT"], 100, currency: "USD", rate: 3.7533m)))["id"].S();
        Assert.Equal(375.33m, await BalanceAsync(api, Clearing));
        foreach (var amount in new[] { 33.33m, 33.33m, 33.34m })
        {
            var v = await api.Post("/landedcostvouchers", Voucher(orderId, null, "USD", Pull(advanceId, types["FREIGHT"], amount)));
            Assert.Equal(201, v.Status);
            Assert.Equal(200, (await api.Post($"/landedcostvouchers/{v.Data!["id"].S()}/approve")).Status);
        }
        Assert.Equal(0, await BalanceAsync(api, Clearing));
        Assert.Equal("settled", (await api.Get($"/importcostadvances/{advanceId}")).Data!["status"].S());
        Assert.Equal(375.33m, (await OrderVehiclesAsync(api, orderId)).Sum(v => v["totalCost"].D()) - 3 * 80000);
        await AssertBalancedAsync(api);
    }

    [Fact]
    public async Task Balance_report_lists_paid_settled_remaining_and_arrived_cars_and_flags_completed_orders_with_a_balance()
    {
        var api = await NewTenantAsync();
        var types = await TypeIdsAsync(api);
        await BankMethodAsync(api);
        var orderId = await OrderAsync(api, qty: 2);
        var advanceId = (await ApprovedAdvanceAsync(api, Advance(orderId, types["FREIGHT"], 1000)))["id"].S();
        await ApprovedAdvanceAsync(api, Advance(orderId, types["FREIGHT_INSURANCE"], 200));
        var v = await api.Post("/landedcostvouchers", Voucher(orderId, null, null, Pull(advanceId, types["FREIGHT"], 1000)));
        Assert.Equal(200, (await api.Post($"/landedcostvouchers/{v.Data!["id"].S()}/approve")).Status);

        var row = (await api.Get("/importcostadvances/balances")).Data!.AsArray().Single()!;
        Assert.Equal(orderId, row["procurementOrderId"].S());
        Assert.Equal(2, row["advancesCount"].D());
        Assert.Equal(1200, row["paid"].D()); Assert.Equal(1000, row["settled"].D()); Assert.Equal(200, row["remaining"].D());
        Assert.Equal(2, row["vehiclesCount"].D()); Assert.Equal(0, row["arrivedVehicles"].D());
        Assert.False(row["orderCompleted"]!.GetValue<bool>()); Assert.False(row["needsAttention"]!.GetValue<bool>());
        Assert.Equal(200, await BalanceAsync(api, Clearing)); // التقرير يطابق رصيد الحساب

        // وصلت السيارات وما زال على الأمر رصيد: يُبرز
        Assert.Equal(200, (await api.Post($"/carprocurementorders/{orderId}/AdvanceStage", new
        {
            targetStage = "vin_received", pdiInspectionPassed = true, warehouseLocation = "المعرض",
            vins = Enumerable.Range(0, 2).Select(_ => new { vin = Client.NewVin('4') }).ToArray(),
        })).Status);
        row = (await api.Get("/importcostadvances/balances")).Data!.AsArray().Single()!;
        Assert.Equal(2, row["arrivedVehicles"].D());
        Assert.True(row["orderCompleted"]!.GetValue<bool>()); Assert.True(row["needsAttention"]!.GetValue<bool>());
    }
}
