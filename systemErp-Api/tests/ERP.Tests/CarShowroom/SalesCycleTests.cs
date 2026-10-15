using System.Text.Json.Nodes;
using ERP.Service.Services.CarShowroom;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>المرحلة الثالثة: رسوم البيع على العميل أو المعرض، الإضافات، الحجز بتاريخ انتهاء، والربط مع عمليات تم.</summary>
[Collection("api")]
public class SalesCycleTests : TestBase
{
    public SalesCycleTests(ErpFactory f) : base(f) { }

    private static async Task<Dictionary<string, string>> TypeIdsAsync(Client api)
        => (await api.Get("/expensetypes?pageSize=200")).Data!["items"]!.AsArray().ToDictionary(t => t!["code"].S(), t => t!["id"].S());

    private static async Task<(string Id, string Vin)> SeedCarAsync(Client api, decimal purchase = 40000, decimal selling = 50000, string? vin = null)
    {
        vin ??= Client.NewVin('7');
        var car = await api.Post("/vehicles", new
        {
            chassisNumber = vin, brandNameAr = "تويوتا", modelNameAr = "يارس", year = 2024, condition = "new", purchasePrice = purchase, sellingPrice = selling,
            vatMode = "standard_15", fuelType = "petrol", transmission = "automatic", colorExterior = "أبيض", colorInterior = "أسود", location = "المعرض",
        });
        Assert.Equal(201, car.Status);
        return (car.Data!["id"].S(), vin);
    }

    private static Task<Res> ContractAsync(Client api, string vehicleId, Guid? customerId = null, string buyer = "مشتري", object? extra = null)
        => api.Post("/carsalescontracts", new
        {
            cycleType = "individual", buyerName = buyer, buyerNationalIdOrCr = "1010101010", buyerPhone = "0550000000", vehicleId, customerId,
            sellingPrice = 50000, paymentMethod = "cash", condition = "new",
        });

    private static Task<Res> LineAsync(Client api, string contractId, string typeId, decimal amount, string? treatment = null, decimal? salePrice = null, string status = "estimated")
        => api.Post("/additionalcharges", new { documentType = "car_sales_contract", documentId = contractId, expenseTypeId = typeId, amount, status, treatment, salePrice });

    private async Task<Client> UserAsync(Client owner, string role)
    {
        var email = $"u{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await owner.Post("/users", new { name = "مستخدم", email, phone = "1", password = "Passw0rd!", role })).Status);
        var user = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");
        Assert.NotNull(user.Token);
        return user;
    }

    [Fact]
    public void Vehicle_profit_is_revenue_minus_every_cost_before_vat()
    {
        // 50000 سيارة + 1650 رسوم وإضافات على العميل − (40000 تكلفة + 500 ستُضاف + 100 على المعرض + 950 تكلفة ما حُمّل)
        Assert.Equal((10100m, 19.55m), VehicleProfit.Compute(50000, 1650, 40000, 500, 100, 950));
        // بيع بخسارة
        Assert.Equal((-2000m, -5m), VehicleProfit.Compute(40000, 0, 41000, 0, 1000, 0));
        // لا إيراد: لا نسبة
        Assert.Equal((-300m, 0m), VehicleProfit.Compute(0, 0, 0, 0, 300, 0));
        // رسم يُمرَّر بتكلفته لا يغيّر الربح
        Assert.Equal(VehicleProfit.Compute(50000, 0, 40000, 0, 0, 0).Profit, VehicleProfit.Compute(50000, 150, 40000, 0, 0, 150).Profit);
    }

    [Fact]
    public async Task Sale_fees_go_to_the_customer_or_the_showroom_and_add_ons_carry_their_own_price_and_cost()
    {
        var api = await NewTenantAsync();
        var types = await TypeIdsAsync(api);
        var (vid, _) = await SeedCarAsync(api);
        var contract = await ContractAsync(api, vid);
        Assert.Equal(201, contract.Status);
        var id = contract.Data!["id"].S();
        Assert.Equal(57500, contract.Data["grandTotalWithCharges"].D());

        // نقل الملكية على العميل (افتراضي الإعدادات)، اللوحات على المعرض (خيار السطر)، وإضافة لها سعر بيع وتكلفة مستقلة
        var transfer = await LineAsync(api, id, types["TAMM_TRANSFER_OUT"], 150);
        Assert.Equal("recharge_to_customer", transfer.Data!["treatment"].S());
        Assert.Equal("expense", (await LineAsync(api, id, types["PLATES"], 100, "expense")).Data!["treatment"].S());
        var addon = await LineAsync(api, id, types["CUSTOMER_ADDONS"], 800, salePrice: 1500);
        Assert.Equal(201, addon.Status);
        Assert.Equal(400, (await LineAsync(api, id, types["PLATES"], 100, "expense", salePrice: 200)).Status); // سعر بيع لسطر ليس على العميل

        var current = (await api.Get($"/carsalescontracts/{id}")).Data!;
        Assert.Equal(1650, current["customerChargesNet"].D());
        Assert.Equal(225, current["customerChargesVat"].D()); // الإضافة خاضعة، ونقل الملكية خارج النطاق
        Assert.Equal(59375, current["grandTotalWithCharges"].D());
        Assert.Equal(57500, current["totalWithVat"].D());

        // ما يظهر للعميل في عرض السعر
        var shown = (await api.Get($"/carsalescontracts/{id}/customer-charges")).Data!.AsArray();
        Assert.Equal(2, shown.Count);
        Assert.Equal(1725, shown.Single(c => c!["price"].D() == 1500)!["total"].D());
        Assert.Equal(150, shown.Single(c => c!["price"].D() == 150)!["total"].D());

        // اللوحة الداخلية: لمن يرى التكلفة فقط
        var panel = (await api.Get($"/carsalescontracts/{id}/profit")).Data!;
        Assert.Equal(50000, panel["vehicleNetPrice"].D()); Assert.Equal(40000, panel["currentCost"].D());
        Assert.Equal(100, panel["showroomExpenses"].D()); Assert.Equal(950, panel["customerChargesCost"].D());
        Assert.Equal(10600, panel["expectedProfit"].D()); // 51650 − (40000 + 100 + 950)
        Assert.Equal(20.52m, panel["expectedProfitPercent"].D());
        Assert.Equal(59375, panel["customerTotal"].D());
        var rep = await UserAsync(api, "sales_rep");
        Assert.Equal(403, (await rep.Get($"/carsalescontracts/{id}/profit")).Status);
        Assert.Equal(200, (await rep.Get($"/carsalescontracts/{id}/customer-charges")).Status);

        // مصروف أُضيف لتكلفة السيارة بعد إنشاء العقد يدخل تكلفة المبيعات عند الفوترة
        Assert.Equal(201, (await api.Post("/additionalcharges", new { documentType = "vehicle", documentId = vid, expenseTypeId = types["POLISHING"], amount = 500, status = "actual" })).Status);

        // الفوترة: الرسوم على العميل والإضافات بنود مستقلة في فاتورته
        var done = await api.Post($"/carsalescontracts/{id}/complete");
        Assert.Equal(200, done.Status);
        Assert.Equal(40500, done.Data!["costPrice"].D());
        var invoice = (await api.Get($"/invoices/{done.Data["invoiceId"].S()}")).Data!;
        Assert.Equal(59375, invoice["grandTotal"].D());
        Assert.Equal(7725, invoice["vatTotal"].D());
        Assert.Equal(3, invoice["items"]!.AsArray().Count);
        Assert.Equal(40500, invoice["totalCost"].D());
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
        // الأسطر تنتقل مع العقد إلى فاتورته
        Assert.Equal(3, (await api.Get($"/additionalcharges?documentType=invoice&documentId={done.Data["invoiceId"].S()}")).Data!.AsArray().Count);

        // بعد الفوترة: ما على العميل مقفل، وما على المعرض يبقى متاحاً
        Assert.Equal(409, (await LineAsync(api, id, types["PLATES"], 50)).Status);
        Assert.Equal(409, (await api.Post($"/additionalcharges/{addon.Data!["id"].S()}/cancel")).Status);
        Assert.Equal(201, (await LineAsync(api, id, types["SALES_COMMISSION"], 700, status: "actual")).Status);
        Assert.Equal(1650, (await api.Get($"/carsalescontracts/{id}")).Data!["customerChargesNet"].D());
    }

    [Fact]
    public async Task Showroom_settings_choose_who_bears_sale_fees_by_default()
    {
        var api = await NewTenantAsync();
        var types = await TypeIdsAsync(api);
        var (vid, _) = await SeedCarAsync(api);
        var id = (await ContractAsync(api, vid)).Data!["id"].S();

        var defaults = (await api.Get("/carshowroomsettings")).Data!;
        Assert.Equal("customer", defaults["saleFeesBorneBy"].S());
        Assert.Equal(7, defaults["reservationDays"].D());
        Assert.Equal(400, (await api.Put("/carshowroomsettings", new { saleFeesBorneBy = "showroom", reservationDays = 0, standardVatRate = 15 })).Status);
        var rep = await UserAsync(api, "sales_rep");
        Assert.Equal(403, (await rep.Put("/carshowroomsettings", new { saleFeesBorneBy = "showroom", reservationDays = 3, standardVatRate = 15 })).Status);
        Assert.Equal("showroom", (await api.Put("/carshowroomsettings", new { saleFeesBorneBy = "showroom", reservationDays = 3, standardVatRate = 15 })).Data!["saleFeesBorneBy"].S());

        // الافتراضي صار «على المعرض»، وخيار «على العميل» يبقى متاحاً لكل سطر
        Assert.Equal("expense", (await LineAsync(api, id, types["TAMM_TRANSFER_OUT"], 150)).Data!["treatment"].S());
        Assert.Equal("recharge_to_customer", (await LineAsync(rep, id, types["PLATES"], 100, "recharge_to_customer")).Data!["treatment"].S());
        Assert.Equal(100, (await api.Get($"/carsalescontracts/{id}")).Data!["customerChargesNet"].D());
    }

    [Fact]
    public async Task Legacy_contract_fee_fields_become_showroom_lines_without_changing_the_customer_total()
    {
        var api = await NewTenantAsync();
        var (vid, _) = await SeedCarAsync(api);
        object Body(decimal registration, decimal accessories) => new
        {
            cycleType = "individual", buyerName = "مشتري", buyerNationalIdOrCr = "1010101010", buyerPhone = "0550000000", vehicleId = vid,
            sellingPrice = 50000, paymentMethod = "cash", condition = "new", registrationFee = registration, accessoriesCost = accessories, insuranceCost = 0,
        };
        var contract = await api.Post("/carsalescontracts", Body(500, 300));
        var id = contract.Data!["id"].S();
        var lines = (await api.Get($"/additionalcharges?documentType=car_sales_contract&documentId={id}")).Data!.AsArray();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => { Assert.Equal("contract_fields", l!["source"].S()); Assert.Equal("expense", l["treatment"].S()); Assert.Equal("estimated", l["status"].S()); });
        Assert.Equal(new[] { 300m, 500m }, lines.Select(l => l!["amount"].D()).OrderBy(x => x).ToArray());
        Assert.Equal(800, (await api.Get($"/carsalescontracts/{id}/profit")).Data!["showroomExpenses"].D());

        // تعديل الحقول يحدّث الأسطر، وما يدفعه العميل لا يتغيّر
        Assert.Equal(200, (await api.Put($"/carsalescontracts/{id}", Body(0, 450))).Status);
        lines = (await api.Get($"/additionalcharges?documentType=car_sales_contract&documentId={id}")).Data!.AsArray();
        Assert.Equal(450, lines.Single()!["amount"].D());
        var done = await api.Post($"/carsalescontracts/{id}/complete");
        Assert.Equal(57500, (await api.Get($"/invoices/{done.Data!["invoiceId"].S()}")).Data!["grandTotal"].D());
    }

    [Fact]
    public async Task Reservation_is_edited_in_place_moving_the_hold_to_the_new_vehicle()
    {
        var api = await NewTenantAsync();
        var (first, _) = await SeedCarAsync(api);
        var (second, _) = await SeedCarAsync(api);
        var (ali, _) = await SeedCustomerAsync(api, name: "علي");
        var (omar, _) = await SeedCustomerAsync(api, name: "عمر");
        var rid = (await api.Post("/vehiclereservations", new { vehicleId = first, customerId = ali })).Data!["id"].S();
        var deposit = await api.Post("/vouchers", new { type = "receipt", amount = 1000, partyName = "علي", treasuryAccountCode = "1111", paymentMethod = "cash", depositVehicleId = first, depositCustomerId = ali });
        Assert.True(deposit.Success);

        var expires = DateTime.UtcNow.AddDays(30);
        var edited = await api.Put($"/vehiclereservations/{rid}", new { vehicleId = second, customerId = omar, expiresAt = expires, notes = "تعديل" });
        Assert.Equal(200, edited.Status);
        Assert.Equal("عمر", edited.Data!["customerName"].S()); Assert.Equal(second.ToString(), edited.Data["vehicleId"].S());
        Assert.Equal("available", (await api.Get($"/vehicles/{first}")).Data!["status"].S());
        Assert.Equal("reserved", (await api.Get($"/vehicles/{second}")).Data!["status"].S());
        var voucher = (await api.Get($"/vouchers/{deposit.Data!["id"].S()}")).Data!;
        Assert.Equal("عمر", voucher["partyName"].S()); Assert.Equal(second.ToString(), voucher["depositVehicleId"].S());

        Assert.Equal(400, (await api.Put($"/vehiclereservations/{rid}", new { vehicleId = second })).Status); // بلا عميل
        Assert.Equal(404, (await api.Put($"/vehiclereservations/{Guid.NewGuid()}", new { vehicleId = second, customerId = ali })).Status);
    }

    [Fact]
    public async Task Reservation_holds_the_vehicle_for_its_customer_until_it_expires_or_is_cancelled()
    {
        var api = await NewTenantAsync();
        var (vid, _) = await SeedCarAsync(api);
        var (ali, _) = await SeedCustomerAsync(api, name: "علي");
        var (omar, _) = await SeedCustomerAsync(api, name: "عمر");

        Assert.Equal(400, (await api.Post("/vehiclereservations", new { vehicleId = vid })).Status); // بلا عميل
        Assert.Equal(400, (await api.Post("/vehiclereservations", new { vehicleId = vid, customerId = ali, expiresAt = DateTime.UtcNow.AddDays(-1) })).Status);
        var reservation = await api.Post("/vehiclereservations", new { vehicleId = vid, customerId = ali });
        Assert.Equal(201, reservation.Status);
        Assert.Equal("علي", reservation.Data!["customerName"].S());
        Assert.True(DateTime.Parse(reservation.Data["expiresAt"].S()) > DateTime.UtcNow.AddDays(6)); // مدة الحجز الافتراضية 7 أيام
        var rid = reservation.Data["id"].S();

        // محجوزة: لا تُعرض، ولا تُحجز ولا تُباع ولا يُقبض عليها عربون لعميل آخر
        Assert.Equal("reserved", (await api.Get($"/vehicles/{vid}")).Data!["status"].S());
        Assert.Empty((await api.Get("/vehicles/available")).Data!.AsArray());
        Assert.Equal(409, (await api.Post("/vehiclereservations", new { vehicleId = vid, customerId = omar })).Status);
        Assert.Equal(409, (await ContractAsync(api, vid, omar, "عمر")).Status);
        Assert.Equal(409, (await api.Post("/vouchers", new { type = "receipt", amount = 1000, partyName = "عمر", treasuryAccountCode = "1111", paymentMethod = "cash", depositVehicleId = vid, depositCustomerId = omar })).Status);

        // صاحب الحجز: يدفع عربوناً ويُنشأ عقده، والحجز يرتبط بهما
        var deposit = await api.Post("/vouchers", new { type = "receipt", amount = 1000, partyName = "علي", treasuryAccountCode = "1111", paymentMethod = "cash", depositVehicleId = vid, depositCustomerId = ali });
        Assert.True(deposit.Success);
        var held = (await api.Get($"/vehiclereservations?vehicleId={vid}&status=active")).Data!["items"]!.AsArray().Single()!;
        Assert.Equal(deposit.Data!["id"].S(), held["depositVoucherId"].S());
        Assert.Equal(200, (await api.Post($"/vehiclereservations/{rid}/extend", new { expiresAt = DateTime.UtcNow.AddDays(20) })).Status);
        Assert.Equal(201, (await ContractAsync(api, vid, ali, "علي")).Status);

        // حذف العربون يفك إمساكه؛ الحجز اليدوي باقٍ حتى يُلغى
        Assert.Equal(200, (await api.Delete($"/vouchers/{deposit.Data["id"].S()}")).Status);
        Assert.Equal("reserved", (await api.Get($"/vehicles/{vid}")).Data!["status"].S());
        var cancelled = await api.Post($"/vehiclereservations/{rid}/cancel", new { reason = "تراجع العميل" });
        Assert.Equal("released", cancelled.Data!["status"].S());
        Assert.Equal("available", (await api.Get($"/vehicles/{vid}")).Data!["status"].S());
        Assert.Equal(409, (await api.Post($"/vehiclereservations/{rid}/cancel", new { reason = "x" })).Status);

        // انتهاء المدة بلا عربون ولا عقد يعيد السيارة للعرض تلقائياً
        var (second, _) = await SeedCarAsync(api);
        Assert.Equal(201, (await api.Post("/vehiclereservations", new { vehicleId = second, customerName = "زائر", expiresAt = DateTime.UtcNow.AddSeconds(1) })).Status);
        Assert.DoesNotContain((await api.Get("/vehicles/available")).Data!.AsArray(), v => v!["id"].S() == second);
        await Task.Delay(1600);
        Assert.Contains((await api.Get("/vehicles/available")).Data!.AsArray(), v => v!["id"].S() == second);
        Assert.Equal("expired", (await api.Get($"/vehiclereservations?vehicleId={second}")).Data!["items"]![0]!["status"].S());
        Assert.Equal(201, (await ContractAsync(api, second, omar, "عمر")).Status);
    }

    [Fact]
    public async Task Contract_approval_and_sale_drive_the_reservation_record()
    {
        var api = await NewTenantAsync();
        var (vid, _) = await SeedCarAsync(api);
        var id = (await ContractAsync(api, vid, buyer: "سالم")).Data!["id"].S();
        Assert.Empty((await api.Get($"/vehiclereservations?vehicleId={vid}")).Data!["items"]!.AsArray()); // المسودة لا تحجز

        Assert.Equal(200, (await api.Post($"/carsalescontracts/{id}/AdvanceStatus", new { targetStatus = "approved" })).Status);
        var reservation = (await api.Get($"/vehiclereservations?vehicleId={vid}")).Data!["items"]!.AsArray().Single()!;
        Assert.Equal("active", reservation["status"].S()); Assert.Equal(id, reservation["contractId"].S()); Assert.Equal("سالم", reservation["customerName"].S());
        Assert.Equal(409, (await api.Post($"/vehiclereservations/{reservation["id"].S()}/cancel", new { reason = "x" })).Status); // يمسكه العقد

        // إلغاء العقد يفك الحجز
        Assert.Equal(200, (await api.Post($"/carsalescontracts/{id}/cancel", new { reason = "تراجع" })).Status);
        Assert.Equal("released", (await api.Get($"/vehiclereservations?vehicleId={vid}")).Data!["items"]![0]!["status"].S());
        Assert.Equal("available", (await api.Get($"/vehicles/{vid}")).Data!["status"].S());

        // بيع السيارة يحوّل الحجز إلى بيع
        var second = (await ContractAsync(api, vid, buyer: "ماجد")).Data!["id"].S();
        Assert.Equal(200, (await api.Post($"/carsalescontracts/{second}/complete")).Status);
        var all = (await api.Get($"/vehiclereservations?vehicleId={vid}")).Data!["items"]!.AsArray();
        Assert.Contains(all, r => r!["status"].S() == "converted" && r["contractId"].S() == second);
    }

    [Fact]
    public async Task Tamm_success_creates_the_charge_once_and_failure_or_unknown_create_nothing()
    {
        var api = await NewTenantAsync();
        var (vid, _) = await SeedCarAsync(api);
        async Task<JsonArray> LinesAsync() => (await api.Get($"/additionalcharges?vehicleId={vid}")).Data!.AsArray();

        Assert.Equal(400, (await api.Post("/tammoperations", new { type = "transfer_to_showroom", fee = 230 })).Status); // بلا سيارة ولا شاسيه
        var failed = await api.Post("/tammoperations", new { type = "transfer_to_showroom", vehicleId = vid, fee = 230, reference = "T-1" });
        Assert.Equal(201, failed.Status);
        Assert.StartsWith("TAMM-", failed.Data!["operationNumber"].S());
        Assert.Equal("pending", failed.Data["status"].S());

        // الفشل لا ينشئ شيئاً
        Assert.Equal("failed", (await api.Post($"/tammoperations/{failed.Data["id"].S()}/status", new { status = "failed", failureReason = "رفض المرور" })).Data!["status"].S());
        Assert.Empty(await LinesAsync());

        // المجهول لا ينشئ شيئاً حتى يُحسم يدوياً
        var op = await api.Post("/tammoperations", new { type = "transfer_to_showroom", vehicleId = vid, fee = 230 });
        var opId = op.Data!["id"].S();
        Assert.Equal("unknown", (await api.Post($"/tammoperations/{opId}/status", new { status = "unknown" })).Data!["status"].S());
        Assert.Empty(await LinesAsync());
        Assert.Equal(400, (await api.Post($"/tammoperations/{opId}/status", new { status = "pending" })).Status);

        var success = await api.Post($"/tammoperations/{opId}/status", new { status = "success", fee = 250, reference = "T-2" });
        Assert.Equal("success", success.Data!["status"].S());
        Assert.True(success.Data["resolvedManually"]!.GetValue<bool>());
        Assert.Equal("capitalize", success.Data["chargeTreatment"].S());
        var line = (await LinesAsync()).Single()!;
        Assert.Equal("TAMM_TRANSFER_IN", line["expenseTypeCode"].S()); Assert.Equal("actual", line["status"].S());
        Assert.Equal("tamm", line["source"].S()); Assert.Equal(opId, line["tammOperationId"].S()); Assert.Equal(250, line["amount"].D());
        Assert.Equal(40250, (await api.Get($"/vehicles/{vid}")).Data!["totalCost"].D());

        // تكرار إشعار النجاح لا يكرر المصروف، والعملية المنتهية لا تتغيّر
        Assert.Equal(200, (await api.Post($"/tammoperations/{opId}/status", new { status = "success", fee = 999 })).Status);
        Assert.Single(await LinesAsync());
        Assert.Equal(40250, (await api.Get($"/vehicles/{vid}")).Data!["totalCost"].D());
        Assert.Equal(409, (await api.Post($"/tammoperations/{opId}/status", new { status = "failed" })).Status);
        Assert.Equal(409, (await api.Post($"/tammoperations/{failed.Data["id"].S()}/status", new { status = "success" })).Status);
        // مصروف تم يديره مصدره
        Assert.Equal(409, (await api.Delete($"/additionalcharges/{line["id"].S()}")).Status);

        // عملية نوعها مصروف على المعرض لا تدخل التكلفة
        var hold = await api.Post("/tammoperations", new { type = "hold", vehicleId = vid, fee = 40 });
        Assert.Equal("expense", (await api.Post($"/tammoperations/{hold.Data!["id"].S()}/status", new { status = "success" })).Data!["chargeTreatment"].S());
        Assert.Equal(40250, (await api.Get($"/vehicles/{vid}")).Data!["totalCost"].D());
        Assert.Equal(3, (await api.Get($"/tammoperations?vehicleId={vid}")).Data!["totalCount"].D());
        Assert.Equal(2, (await api.Get($"/tammoperations?vehicleId={vid}&status=success")).Data!["totalCount"].D());
    }

    [Fact]
    public async Task Tamm_inquiry_on_an_unpurchased_vehicle_is_a_general_expense_until_it_is_bought()
    {
        var api = await NewTenantAsync();
        var vin = Client.NewVin('8');
        var inquiry = await api.Post("/tammoperations", new { type = "violations_inquiry", vin, fee = 30 });
        Assert.Equal(201, inquiry.Status);
        Assert.Null(inquiry.Data!["vehicleId"]);
        var done = await api.Post($"/tammoperations/{inquiry.Data["id"].S()}/status", new { status = "success" });
        Assert.Equal("expense", done.Data!["chargeTreatment"].S());
        var chargeId = done.Data["chargeLineId"].S();
        var general = (await api.Get($"/additionalcharges/{chargeId}")).Data!;
        Assert.Equal("tamm_operation", general["documentType"].S());
        Assert.Empty(general["allocations"]!.AsArray());

        // اشتُريت لاحقاً: المصروف يُنقل لتكلفتها
        var (vid, _) = await SeedCarAsync(api, vin: vin);
        var moved = (await api.Get($"/additionalcharges/{chargeId}")).Data!;
        Assert.Equal("vehicle", moved["documentType"].S()); Assert.Equal("capitalize", moved["treatment"].S());
        Assert.Equal(vid, moved["allocations"]![0]!["vehicleId"].S());
        Assert.Equal(40030, (await api.Get($"/vehicles/{vid}")).Data!["totalCost"].D());
        Assert.Equal(vid, (await api.Get($"/tammoperations/{inquiry.Data["id"].S()}")).Data!["vehicleId"].S());
    }

    [Fact]
    public async Task Buyer_transfer_success_follows_the_treatment_chosen_on_the_contract()
    {
        var api = await NewTenantAsync();
        var types = await TypeIdsAsync(api);

        // على العميل: الفعلي يحل محل التقديري، وما وُعد به العميل سعراً يبقى سعره
        var (first, _) = await SeedCarAsync(api);
        var onCustomer = (await ContractAsync(api, first)).Data!["id"].S();
        var estimate = await LineAsync(api, onCustomer, types["TAMM_TRANSFER_OUT"], 150);
        var op = await api.Post("/tammoperations", new { type = "transfer_to_buyer", vehicleId = first, contractId = onCustomer, fee = 172.5 });
        var done = await api.Post($"/tammoperations/{op.Data!["id"].S()}/status", new { status = "success" });
        Assert.Equal("recharge_to_customer", done.Data!["chargeTreatment"].S());
        var actual = (await api.Get($"/additionalcharges/{done.Data["chargeLineId"].S()}")).Data!;
        Assert.Equal("car_sales_contract", actual["documentType"].S());
        Assert.Equal(172.5m, actual["amount"].D()); Assert.Equal(150, actual["salePrice"].D());
        Assert.Equal(estimate.Data!["id"].S(), actual["replacesLineId"].S());
        Assert.Equal("replaced", (await api.Get($"/additionalcharges/{estimate.Data["id"].S()}")).Data!["status"].S());
        Assert.Equal(150, (await api.Get($"/carsalescontracts/{onCustomer}")).Data!["customerChargesNet"].D());
        var panel = (await api.Get($"/carsalescontracts/{onCustomer}/profit")).Data!;
        Assert.Equal(172.5m, panel["customerChargesCost"].D());
        Assert.Equal(9977.5m, panel["expectedProfit"].D()); // 50150 − (40000 + 172.5)

        // على المعرض: المصروف الفعلي على المعرض ولا يظهر للعميل
        var (second, _) = await SeedCarAsync(api);
        var onShowroom = (await ContractAsync(api, second)).Data!["id"].S();
        await LineAsync(api, onShowroom, types["TAMM_TRANSFER_OUT"], 150, "expense");
        var op2 = await api.Post("/tammoperations", new { type = "transfer_to_buyer", vehicleId = second, contractId = onShowroom, fee = 172.5 });
        Assert.Equal("expense", (await api.Post($"/tammoperations/{op2.Data!["id"].S()}/status", new { status = "success" })).Data!["chargeTreatment"].S());
        Assert.Equal(0, (await api.Get($"/carsalescontracts/{onShowroom}")).Data!["customerChargesNet"].D());
        Assert.Equal(172.5m, (await api.Get($"/carsalescontracts/{onShowroom}/profit")).Data!["showroomExpenses"].D());

        // عقد سيارة أخرى يُرفض
        Assert.Equal(400, (await api.Post("/tammoperations", new { type = "transfer_to_buyer", vehicleId = first, contractId = onShowroom, fee = 10 })).Status);

        // نقل ملكية بعد صدور الفاتورة بلا تقدير في العقد: يتحمله المعرض
        var (third, _) = await SeedCarAsync(api);
        var invoiced = (await ContractAsync(api, third)).Data!["id"].S();
        Assert.Equal(200, (await api.Post($"/carsalescontracts/{invoiced}/complete")).Status);
        var late = await api.Post("/tammoperations", new { type = "transfer_to_buyer", vehicleId = third, contractId = invoiced, fee = 172.5 });
        Assert.Equal("expense", (await api.Post($"/tammoperations/{late.Data!["id"].S()}/status", new { status = "success" })).Data!["chargeTreatment"].S());
    }
}
