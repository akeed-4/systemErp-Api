using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>المرحلة الخامسة: الحالات الخاصة (القسم 6 من المتطلبات) وتقارير المصاريف والتكاليف.</summary>
[Collection("api")]
public class SpecialCasesTests : TestBase
{
    public SpecialCasesTests(ErpFactory f) : base(f) { }

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

    private static async Task<string> SeedCarAsync(Client api, decimal purchase = 40000, decimal selling = 50000)
    {
        var car = await api.Post("/vehicles", new
        {
            chassisNumber = Client.NewVin('6'), brandNameAr = "تويوتا", modelNameAr = "يارس", year = 2024, condition = "new", purchasePrice = purchase, sellingPrice = selling,
            vatMode = "standard_15", fuelType = "petrol", transmission = "automatic", colorExterior = "أبيض", colorInterior = "أسود", location = "المعرض",
        });
        Assert.Equal(201, car.Status);
        return car.Data!["id"].S();
    }

    private static async Task<JsonNode> SellAsync(Client api, string vehicleId)
    {
        var sale = await api.Post("/carsalescontracts/QuickSale", new { contract = new { cycleType = "individual", buyerName = "مشتري", buyerNationalIdOrCr = "1010101010", buyerPhone = "0550000000",
            vehicleId, sellingPrice = 50000, vatMode = "standard_15", paymentMethod = "cash", condition = "new" } });
        Assert.Equal(201, sale.Status);
        return sale.Data!;
    }

    private async Task<Client> UserAsync(Client owner, string role)
    {
        var email = $"u{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await owner.Post("/users", new { name = "مستخدم", email, phone = "1", password = "Passw0rd!", role })).Status);
        var user = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");
        Assert.NotNull(user.Token);
        return user;
    }

    private static async Task<JsonArray> ReportAsync(Client api, string name)
    {
        var r = await api.Get($"/reports/car/{name}");
        Assert.Equal(200, r.Status);
        return r.Data!.AsArray();
    }

    // ───────── مصروف فعلي وصل بعد بيع السيارة + لا تعديل لتكلفة مباعة إلا بقيد موثق ─────────
    [Fact]
    public async Task Actual_charge_after_the_sale_goes_to_cost_of_sales_through_a_documented_adjustment()
    {
        var api = await NewTenantAsync();
        var types = await TypeIdsAsync(api);
        var vid = await SeedCarAsync(api);
        var contract = await SellAsync(api, vid);
        var cogs = await BalanceAsync(api, "512");
        var inventory = await BalanceAsync(api, "1142");
        Assert.Equal(40000, cogs);
        Task<Res> Late(Client who, decimal amount, string? reason) => who.Post("/additionalcharges", new
        { documentType = "vehicle", documentId = vid, expenseTypeId = types["REPAIR"], amount, status = "actual", adjustmentReason = reason });

        // صلاحية مستقلة وسبب موثق
        var rep = await UserAsync(api, "sales_rep");
        Assert.Equal(403, (await Late(rep, 300, "فاتورة ورشة متأخرة")).Status);
        Assert.Equal(400, (await Late(api, 300, null)).Status);
        var late = await Late(api, 300, "فاتورة ورشة وصلت بعد البيع");
        Assert.Equal(201, late.Status);

        // الفرق على تكلفة المبيعات مباشرة لا على المخزون
        Assert.Equal(cogs + 300, await BalanceAsync(api, "512"));
        Assert.Equal(inventory, await BalanceAsync(api, "1142"));
        Assert.Equal(40300, (await api.Get($"/vehicles/{vid}")).Data!["totalCost"].D());
        var adjustments = (await api.Get($"/vehicles/{vid}/cost-adjustments")).Data!.AsArray();
        var adjustment = adjustments.Single()!;
        Assert.StartsWith("VCA-", adjustment["adjustmentNumber"].S());
        Assert.Equal(300, adjustment["amount"].D()); Assert.Equal("فاتورة ورشة وصلت بعد البيع", adjustment["reason"].S());
        Assert.Equal(contract["id"].S(), adjustment["contractId"].S());
        Assert.NotNull(adjustment["journalEntryId"]);

        // رسم تم مُرسمَل بعد البيع: يوثَّق تلقائياً ويذهب لتكلفة المبيعات
        var op = await api.Post("/tammoperations", new { type = "registration_renewal", vehicleId = vid, fee = 100 });
        Assert.Equal("capitalize", (await api.Post($"/tammoperations/{op.Data!["id"].S()}/status", new { status = "success" })).Data!["chargeTreatment"].S());
        Assert.Equal(cogs + 400, await BalanceAsync(api, "512"));
        Assert.Equal(2, (await api.Get($"/vehicles/{vid}/cost-adjustments")).Data!.AsArray().Count);

        // تقرير ربح السيارة بعد كل المصاريف
        var profit = (await ReportAsync(api, "VehicleNetProfit")).Single()!;
        Assert.Equal(40000, profit["costAtSale"].D()); Assert.Equal(400, profit["postSaleAdjustments"].D());
        Assert.Equal(9600, profit["netProfit"].D()); Assert.Equal(19.2m, profit["netProfitPercent"].D());

        // إلغاء المصروف المتأخر بسبب: قيد تعديل عكسي
        Assert.Equal(400, (await api.Post($"/additionalcharges/{late.Data!["id"].S()}/cancel")).Status);
        Assert.Equal(200, (await api.Post($"/additionalcharges/{late.Data["id"].S()}/cancel", new { reason = "فاتورة مكررة" })).Status);
        Assert.Equal(cogs + 100, await BalanceAsync(api, "512"));
        Assert.Equal(-300, (await api.Get($"/vehicles/{vid}/cost-adjustments")).Data!.AsArray().Last()!["amount"].D());
        Assert.Equal(40100, (await api.Get($"/vehicles/{vid}")).Data!["totalCost"].D());
        await AssertBalancedAsync(api);
    }

    // ───────── اختلاف الفعلي عن التقديري: استبدال وتعديل الفرق مع سجل بالقيمتين ─────────
    [Fact]
    public async Task Estimate_turned_actual_keeps_both_values_and_reports_the_variance()
    {
        var api = await NewTenantAsync();
        var types = await TypeIdsAsync(api);
        var vid = await SeedCarAsync(api);
        var estimate = await api.Post("/additionalcharges", new { documentType = "vehicle", documentId = vid, expenseTypeId = types["FREIGHT"], amount = 1000, status = "estimated" });
        await api.Post("/additionalcharges", new { documentType = "vehicle", documentId = vid, expenseTypeId = types["POLISHING"], amount = 250, status = "estimated" });
        Assert.Equal(40000, (await api.Get($"/vehicles/{vid}")).Data!["totalCost"].D());

        var actual = await api.Put($"/additionalcharges/{estimate.Data!["id"].S()}", new { documentType = "vehicle", documentId = vid, expenseTypeId = types["FREIGHT"], amount = 1200, status = "actual" });
        Assert.Equal(1000, actual.Data!["estimatedCostAmount"].D());
        Assert.Equal(1200, actual.Data["costAmount"].D());
        Assert.Equal(41200, (await api.Get($"/vehicles/{vid}")).Data!["totalCost"].D()); // التكلفة بالفعلي

        var row = (await api.Get($"/vehicles/{vid}/cost-card")).Data!["lines"]!.AsArray().Single(l => l!["expenseTypeCode"].S() == "FREIGHT")!;
        Assert.Equal(1000, row["estimatedAmount"].D()); Assert.Equal(1200, row["actualAmount"].D());

        var report = await ReportAsync(api, "EstimatedVsActual");
        var freight = report.Single(r => r!["expenseTypeCode"].S() == "FREIGHT")!;
        Assert.Equal(1000, freight["estimated"].D()); Assert.Equal(1200, freight["actual"].D()); Assert.Equal(200, freight["variance"].D());
        var pending = report.Single(r => r!["expenseTypeCode"].S() == "POLISHING")!;
        Assert.Equal(250, pending["estimated"].D()); Assert.Null(pending["actual"]); Assert.Null(pending["variance"]);
    }

    // ───────── مرتجع بيع ─────────
    [Fact]
    public async Task Sale_return_brings_the_vehicle_back_at_its_original_cost_and_keeps_customer_charges_per_setting()
    {
        var api = await NewTenantAsync();
        var types = await TypeIdsAsync(api);
        async Task<(string Vehicle, string Contract)> SoldWithChargesAsync()
        {
            var vid = await SeedCarAsync(api);
            var id = (await api.Post("/carsalescontracts", new { cycleType = "individual", buyerName = "مشتري", buyerNationalIdOrCr = "1010101010", buyerPhone = "0550000000", vehicleId = vid, sellingPrice = 50000, paymentMethod = "cash", condition = "new" })).Data!["id"].S();
            await api.Post("/additionalcharges", new { documentType = "car_sales_contract", documentId = id, expenseTypeId = types["TAMM_TRANSFER_OUT"], amount = 150, status = "estimated" });
            var op = await api.Post("/tammoperations", new { type = "transfer_to_buyer", vehicleId = vid, contractId = id, fee = 150 });
            await api.Post($"/tammoperations/{op.Data!["id"].S()}/status", new { status = "success" });
            Assert.Equal(200, (await api.Post($"/carsalescontracts/{id}/complete")).Status);
            return (vid, id);
        }

        // (أ) الإعداد الافتراضي: الرسوم المحمّلة على العميل لا تُرد
        var (first, firstContract) = await SoldWithChargesAsync();
        Assert.Equal(50000, await BalanceAsync(api, "412")); Assert.Equal(40000, await BalanceAsync(api, "512")); Assert.Equal(0, await BalanceAsync(api, "116"));
        // مصروف وصل بعد البيع: يبقى مصروفاً ولا يعود مع السيارة للمخزون
        Assert.Equal(201, (await api.Post("/additionalcharges", new { documentType = "vehicle", documentId = first, expenseTypeId = types["REPAIR"], amount = 300, status = "actual", adjustmentReason = "فاتورة متأخرة" })).Status);
        Assert.Equal(40300, (await api.Get($"/vehicles/{first}")).Data!["totalCost"].D());

        Assert.Equal(400, (await api.Post($"/carsalescontracts/{firstContract}/return", new { reason = "" })).Status);
        var returned = await api.Post($"/carsalescontracts/{firstContract}/return", new { reason = "عيب مصنعي" });
        Assert.Equal(200, returned.Status);
        Assert.Equal("returned", returned.Data!["status"].S());
        var vehicle = (await api.Get($"/vehicles/{first}")).Data!;
        Assert.Equal("available", vehicle["status"].S());
        Assert.Equal(40000, vehicle["totalCost"].D()); // بتكلفتها الأصلية
        Assert.Equal(0, await BalanceAsync(api, "412"));
        Assert.Equal(300, await BalanceAsync(api, "512"));   // مصروف ما بعد البيع باقٍ على تكلفة المبيعات
        Assert.Equal(0, await BalanceAsync(api, "116"));     // الرسم دفعه العميل ولم يُرد
        var creditNotes = (await api.Get("/invoices?kind=sales_return")).Data!["items"]!.AsArray();
        Assert.Equal(57500, creditNotes.Single()!["grandTotal"].D()); // السيارة فقط
        // لا يتكرر المرتجع، والسيارة تُباع من جديد
        Assert.Equal(409, (await api.Post($"/carsalescontracts/{firstContract}/return", new { reason = "x" })).Status);
        await SellAsync(api, first);

        // (ب) اختيار رد الرسوم: ما دفعه المعرض لتم ولا يُسترد يتحول لمصروف عليه
        var expenseBefore = await BalanceAsync(api, "529");
        var (second, secondContract) = await SoldWithChargesAsync();
        var refund = await api.Post($"/carsalescontracts/{secondContract}/return", new { reason = "تراجع العميل", refundCustomerCharges = true });
        Assert.Equal(200, refund.Status);
        creditNotes = (await api.Get("/invoices?kind=sales_return")).Data!["items"]!.AsArray();
        Assert.Contains(creditNotes, n => n!["grandTotal"].D() == 57650); // السيارة + رسم نقل الملكية
        Assert.Equal(0, await BalanceAsync(api, "116"));
        Assert.Equal(expenseBefore + 150, await BalanceAsync(api, "529"));
        var lines = (await api.Get($"/additionalcharges?documentType=car_sales_contract&documentId={secondContract}")).Data!.AsArray();
        Assert.Equal("expense", lines.Single(l => l!["status"].S() == "actual")!["treatment"].S());
        Assert.Equal("available", (await api.Get($"/vehicles/{second}")).Data!["status"].S());
        await AssertBalancedAsync(api);
    }

    // ───────── إلغاء شراء بعد مصاريف: خسارة أو مطالبة على المورد ─────────
    [Theory]
    [InlineData("loss", "530")]
    [InlineData("supplier_claim", "118")]
    public async Task Cancelled_purchase_charges_become_a_loss_or_a_supplier_claim(string treatment, string account)
    {
        var api = await NewTenantAsync();
        var types = await TypeIdsAsync(api);
        var (supplier, _) = await SeedSupplierAsync(api);
        var order = await api.Post("/carprocurementorders", new
        {
            supplierId = supplier, paymentType = "credit", creditDays = 30, currency = "SAR", exchangeRate = 1, date = DateTime.UtcNow,
            items = new[] { new { brandName = "تويوتا", modelName = "كامري", year = 2025, quantity = 2, unitPrice = 80000 } },
        });
        var id = order.Data!["id"].S();
        foreach (var stage in new[] { "requisition_approved", "rfq", "rfq_approved", "purchase_order" })
            await api.Post($"/carprocurementorders/{id}/AdvanceStage", new { targetStage = stage });
        Assert.Equal(409, (await api.Post($"/carprocurementorders/{id}/settle-cancelled-charges", new { treatment })).Status); // الأمر قائم

        var vehicles = (await api.Get($"/carprocurementorders/{id}/vehicles")).Data!["items"]!.AsArray();
        Assert.Equal(201, (await api.Post("/additionalcharges", new
        {
            documentType = "car_procurement_order", documentId = id, expenseTypeId = types["FREIGHT"], amount = 700, status = "actual",
            allocations = vehicles.Select(v => new { vehicleId = v!["id"].S(), amount = 0 }).ToArray(),
        })).Status);
        Assert.Equal(700, await BalanceAsync(api, "1143"));
        Assert.Equal(200, (await api.Post($"/carprocurementorders/{id}/reject", new { reason = "المورد تعذّر عليه التوريد" })).Status);
        Assert.Single(await ReportAsync(api, "UnpurchasedVehicleExpenses"));

        Assert.Equal(400, (await api.Post($"/carprocurementorders/{id}/settle-cancelled-charges", new { treatment = "ignore" })).Status);
        var settled = await api.Post($"/carprocurementorders/{id}/settle-cancelled-charges", new { treatment });
        Assert.Equal(200, settled.Status);
        Assert.Equal(treatment, settled.Data!["cancelledChargesSettlement"].S());
        Assert.Equal(700, settled.Data["cancelledChargesAmount"].D());
        Assert.Equal(0, await BalanceAsync(api, "1143"));
        Assert.Equal(700, await BalanceAsync(api, account));
        Assert.Equal(409, (await api.Post($"/carprocurementorders/{id}/settle-cancelled-charges", new { treatment })).Status); // مرة واحدة
        await AssertBalancedAsync(api);

        // أمر مرفوض بلا مصاريف فعلية: لا شيء يُعالَج
        var empty = await api.Post("/carprocurementorders", new
        {
            supplierId = supplier, paymentType = "cash", currency = "SAR", exchangeRate = 1, date = DateTime.UtcNow,
            items = new[] { new { brandName = "تويوتا", modelName = "كامري", year = 2025, quantity = 1, unitPrice = 80000 } },
        });
        await api.Post($"/carprocurementorders/{empty.Data!["id"].S()}/reject", new { reason = "عدول" });
        Assert.Equal(409, (await api.Post($"/carprocurementorders/{empty.Data["id"].S()}/settle-cancelled-charges", new { treatment })).Status);
    }

    // ───────── سيارة محجوزة ثم أُلغي الحجز: العربون حسب السياسة ─────────
    [Fact]
    public async Task Cancelling_a_reservation_refunds_or_forfeits_its_deposit_per_policy()
    {
        var api = await NewTenantAsync();
        var (customer, _) = await SeedCustomerAsync(api, name: "علي");
        async Task<(string Reservation, string Voucher)> DepositAsync(string vehicleId, decimal amount)
        {
            var voucher = await api.Post("/vouchers", new { type = "receipt", amount, partyName = "علي", treasuryAccountCode = "1111", paymentMethod = "cash", depositVehicleId = vehicleId, depositCustomerId = customer });
            Assert.True(voucher.Success);
            var reservation = (await api.Get($"/vehiclereservations?vehicleId={vehicleId}&status=active")).Data!["items"]!.AsArray().Single()!;
            return (reservation["id"].S(), voucher.Data!["id"].S());
        }

        // السياسة الافتراضية: يُرد العربون
        var first = await SeedCarAsync(api);
        var (r1, v1) = await DepositAsync(first, 2000);
        Assert.Equal(2000, await BalanceAsync(api, "216")); Assert.Equal(2000, await BalanceAsync(api, "1111"));
        Assert.Equal(400, (await api.Post($"/vehiclereservations/{r1}/cancel", new { reason = "تراجع", depositAction = "keep" })).Status);
        Assert.Equal("released", (await api.Post($"/vehiclereservations/{r1}/cancel", new { reason = "تراجع العميل" })).Data!["status"].S());
        Assert.Equal(0, await BalanceAsync(api, "216")); Assert.Equal(0, await BalanceAsync(api, "1111"));
        Assert.Equal("refunded", (await api.Get($"/vouchers/{v1}")).Data!["depositStatus"].S());
        Assert.Equal("available", (await api.Get($"/vehicles/{first}")).Data!["status"].S());
        Assert.Equal(409, (await api.Delete($"/vouchers/{v1}")).Status); // عربون عولج لا يُحذف سنده

        // اختيار المصادرة لهذا الإلغاء: إيراد
        var second = await SeedCarAsync(api);
        var (r2, v2) = await DepositAsync(second, 1500);
        Assert.Equal(200, (await api.Post($"/vehiclereservations/{r2}/cancel", new { reason = "لم يكمل الشراء", depositAction = "forfeit" })).Status);
        Assert.Equal(0, await BalanceAsync(api, "216")); Assert.Equal(1500, await BalanceAsync(api, "1111")); Assert.Equal(1500, await BalanceAsync(api, "423"));
        Assert.Equal("forfeited", (await api.Get($"/vouchers/{v2}")).Data!["depositStatus"].S());

        // سياسة المعرض مصادرة: تُطبَّق حين لا يُحدَّد اختيار
        Assert.Equal(200, (await api.Put("/carshowroomsettings", new { saleFeesBorneBy = "customer", reservationDays = 7, standardVatRate = 15, marginBasis = "purchase_price", depositCancellationPolicy = "forfeit" })).Status);
        var third = await SeedCarAsync(api);
        var (r3, _) = await DepositAsync(third, 500);
        Assert.Equal(200, (await api.Post($"/vehiclereservations/{r3}/cancel", new { reason = "انتهت المهلة" })).Status);
        Assert.Equal(2000, await BalanceAsync(api, "423"));
        await AssertBalancedAsync(api);
    }

    // ───────── اشتراك تم السنوي: مدفوع مقدماً وإطفاء شهري ─────────
    [Fact]
    public async Task Prepaid_subscription_is_amortized_monthly_and_never_twice_for_the_same_month()
    {
        var api = await NewTenantAsync();
        var types = await TypeIdsAsync(api);
        var start = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(-2);
        string Period(int offset) => start.AddMonths(offset).ToString("yyyy-MM");

        Assert.Equal(400, (await api.Post("/prepaidexpenses", new { expenseTypeId = types["TAMM_SUBSCRIPTION"], startDate = start, months = 0, totalAmount = 1000 })).Status);
        var prepaid = await api.Post("/prepaidexpenses", new { expenseTypeId = types["TAMM_SUBSCRIPTION"], description = "اشتراك تم", startDate = start, months = 3, totalAmount = 1000, vatAmount = 150, paymentAccountCode = "1112" });
        Assert.Equal(201, prepaid.Status);
        Assert.StartsWith("PPE-", prepaid.Data!["number"].S());
        Assert.Equal(1000, await BalanceAsync(api, "117")); Assert.Equal(150, await BalanceAsync(api, "1131")); Assert.Equal(-1150, await BalanceAsync(api, "1112"));

        // زر «ترحيل إطفاء الشهر»: مرة واحدة لكل شهر
        Assert.Equal(400, (await api.Post("/prepaidexpenses/amortize", new { period = "2026/13" })).Status);
        Assert.Equal(0, (await api.Post("/prepaidexpenses/amortize", new { period = Period(-1) })).Data!["posted"].D()); // قبل بدايته
        var month1 = await api.Post("/prepaidexpenses/amortize", new { period = Period(0) });
        Assert.Equal(1, month1.Data!["posted"].D()); Assert.Equal(333.33m, month1.Data["amount"].D());
        Assert.Equal(0, (await api.Post("/prepaidexpenses/amortize", new { period = Period(0) })).Data!["posted"].D());
        Assert.Equal(666.67m, await BalanceAsync(api, "117")); Assert.Equal(333.33m, await BalanceAsync(api, "529"));
        Assert.Equal(409, (await api.Post($"/prepaidexpenses/{prepaid.Data["id"].S()}/cancel")).Status); // أُطفئ منه

        // الشهر الأخير يحمل فرق التقريب، وينتهي المصروف
        Assert.Equal(333.33m, (await api.Post("/prepaidexpenses/amortize", new { period = Period(1) })).Data!["amount"].D());
        Assert.Equal(333.34m, (await api.Post("/prepaidexpenses/amortize")).Data!["amount"].D()); // فارغ = الشهر الحالي
        Assert.Equal(0, await BalanceAsync(api, "117")); Assert.Equal(1000, await BalanceAsync(api, "529"));
        Assert.Equal(0, (await api.Post("/prepaidexpenses/amortize", new { period = Period(3) })).Data!["posted"].D()); // بعد نهايته
        var listed = (await api.Get("/prepaidexpenses")).Data!["items"]!.AsArray().Single()!;
        Assert.Equal("completed", listed["status"].S()); Assert.Equal(0, listed["remainingAmount"].D());
        Assert.Equal(3, listed["amortizations"]!.AsArray().Count);

        // مصروف لم يُطفأ منه يُلغى ويُعكس قيده
        var other = await api.Post("/prepaidexpenses", new { expenseTypeId = types["TAMM_SUBSCRIPTION"], startDate = start, months = 12, totalAmount = 600, paymentAccountCode = "1112" });
        Assert.Equal("cancelled", (await api.Post($"/prepaidexpenses/{other.Data!["id"].S()}/cancel")).Data!["status"].S());
        Assert.Equal(0, await BalanceAsync(api, "117"));
        await AssertBalancedAsync(api);
    }

    // ───────── التقارير ─────────
    [Fact]
    public async Task Cost_reports_cover_transit_aging_tamm_fees_and_unpurchased_vehicles()
    {
        var api = await NewTenantAsync();
        var types = await TypeIdsAsync(api);
        var (supplier, _) = await SeedSupplierAsync(api, "وكيل");
        var order = await api.Post("/carprocurementorders", new
        {
            supplierId = supplier, paymentType = "credit", creditDays = 30, currency = "SAR", exchangeRate = 1, date = DateTime.UtcNow, customsDutyFee = 1000,
            items = new[] { new { brandName = "تويوتا", modelName = "كامري", year = 2025, quantity = 2, unitPrice = 80000 } },
        });
        var id = order.Data!["id"].S();
        foreach (var stage in new[] { "requisition_approved", "rfq", "rfq_approved", "purchase_order" })
            await api.Post($"/carprocurementorders/{id}/AdvanceStage", new { targetStage = stage });
        var inTransitIds = (await api.Get($"/carprocurementorders/{id}/vehicles")).Data!["items"]!.AsArray().Select(v => v!["id"].S()).ToList();
        await api.Post("/additionalcharges", new { documentType = "vehicle", documentId = inTransitIds[0], expenseTypeId = types["FREIGHT"], amount = 400, status = "actual" });

        // سيارات في الطريق وتكلفتها: ثمن الشراء + الفعلي حتى الآن، والمتوقع بالتقديري
        var transit = await ReportAsync(api, "VehiclesInTransit");
        Assert.Equal(2, transit.Count);
        var withFreight = transit.Single(r => r!["id"].S() == inTransitIds[0])!;
        Assert.Equal(80000, withFreight["purchasePrice"].D()); Assert.Equal(400, withFreight["actualCharges"].D()); Assert.Equal(500, withFreight["estimatedCharges"].D());
        Assert.Equal(80400, withFreight["costToDate"].D()); Assert.Equal(80900, withFreight["expectedCost"].D());
        Assert.Equal(order.Data["orderNumber"].S(), withFreight["orderNumber"].S()); Assert.Equal("وكيل", withFreight["supplierName"].S());
        Assert.Empty(await ReportAsync(api, "InventoryAging")); // ما في الطريق ليس مخزوناً

        // عمر المخزون مع تكلفته بعد الاستلام
        await api.Post($"/carprocurementorders/{id}/AdvanceStage", new { targetStage = "vin_received", pdiInspectionPassed = true, vins = new[] { new { vin = Client.NewVin('1') }, new { vin = Client.NewVin('1') } } });
        Assert.Empty(await ReportAsync(api, "VehiclesInTransit"));
        var aging = await ReportAsync(api, "InventoryAging");
        Assert.Equal(2, aging.Count);
        Assert.All(aging, r => { Assert.Equal("0-30", r!["ageBucket"].S()); Assert.Equal(0, r["daysInStock"].D()); Assert.Equal(80000, r["purchasePrice"].D()); });
        Assert.Equal(161400, aging.Sum(r => r!["totalCost"].D())); // 160000 + 1000 جمارك + 400 شحن

        // رسوم تم الشهرية حسب النوع: الناجحة فقط
        foreach (var fee in new[] { 230m, 250m })
        {
            var op = await api.Post("/tammoperations", new { type = "transfer_to_showroom", vehicleId = inTransitIds[0], fee });
            await api.Post($"/tammoperations/{op.Data!["id"].S()}/status", new { status = "success" });
        }
        var hold = await api.Post("/tammoperations", new { type = "hold", vehicleId = inTransitIds[1], fee = 40 });
        await api.Post($"/tammoperations/{hold.Data!["id"].S()}/status", new { status = "success" });
        var failed = await api.Post("/tammoperations", new { type = "hold", vehicleId = inTransitIds[1], fee = 40 });
        await api.Post($"/tammoperations/{failed.Data!["id"].S()}/status", new { status = "failed" });
        var inquiry = await api.Post("/tammoperations", new { type = "violations_inquiry", vin = Client.NewVin('5'), fee = 30 });
        await api.Post($"/tammoperations/{inquiry.Data!["id"].S()}/status", new { status = "success" });

        var fees = await ReportAsync(api, "TammMonthlyFees");
        Assert.Equal(3, fees.Count);
        var transfers = fees.Single(r => r!["expenseTypeCode"].S() == "TAMM_TRANSFER_IN")!;
        Assert.Equal(2, transfers["operationsCount"].D()); Assert.Equal(480, transfers["totalFees"].D());
        Assert.Equal(DateTime.UtcNow.Month, transfers["month"].D());
        Assert.Equal(40, fees.Single(r => r!["expenseTypeCode"].S() == "TAMM_HOLD")!["totalFees"].D());

        // مصاريف على سيارات لم تُشترَ
        var unpurchased = (await ReportAsync(api, "UnpurchasedVehicleExpenses")).Single()!;
        Assert.Equal(30, unpurchased["amount"].D()); Assert.False(unpurchased["purchaseCancelled"]!.GetValue<bool>()); Assert.StartsWith("5", unpurchased["vin"].S());

        // التقارير تعرض تكلفة: لمن يملك صلاحية رؤيتها فقط
        var manager = await UserAsync(api, "general_manager");
        Assert.Equal(200, (await manager.Get("/reports/car/InventoryAging")).Status);
        var saved = (await api.Get("/permissions?roleId=general_manager")).Data!.AsArray().Select(p => new
        {
            screenId = p!["screenId"].S(), screenNameAr = p["screenNameAr"].S(), screenNameEn = p["screenNameEn"].S(),
            canView = p["screenId"].S() != "car-cost-view" && p["canView"]!.GetValue<bool>(), canCreate = p["canCreate"]!.GetValue<bool>(),
            canEdit = p["canEdit"]!.GetValue<bool>(), canDelete = p["canDelete"]!.GetValue<bool>(), canApprove = p["canApprove"]!.GetValue<bool>(),
        }).ToList();
        Assert.Equal(200, (await api.Post("/permissions", new { roleId = "general_manager", permissions = saved })).Status);
        foreach (var report in new[] { "InventoryAging", "VehiclesInTransit", "TammMonthlyFees", "UnpurchasedVehicleExpenses", "EstimatedVsActual", "VehicleNetProfit", "MarginBasisComparison" })
            Assert.Equal(403, (await manager.Get($"/reports/car/{report}")).Status);
    }
}
