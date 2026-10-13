using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>المرحلة الثانية: دورة الشراء بحالة «في الطريق»، رسوم الأمر كأسطر، وسند التكاليف الإضافية والتوزيع.</summary>
[Collection("api")]
public class LandedCostTests : TestBase
{
    public LandedCostTests(ErpFactory f) : base(f) { }

    private static async Task<Dictionary<string, string>> TypeIdsAsync(Client api)
        => (await api.Get("/expensetypes?pageSize=200")).Data!["items"]!.AsArray().ToDictionary(t => t!["code"].S(), t => t!["id"].S());

    private async Task<(string OrderId, string Supplier)> OrderAtPurchaseOrderAsync(Client api, int qty = 3, decimal unitPrice = 80000, decimal customs = 1000, decimal port = 100)
    {
        var (supplier, _) = await SeedSupplierAsync(api);
        var order = await api.Post("/carprocurementorders", new
        {
            supplierId = supplier, paymentType = "credit", creditDays = 30, currency = "SAR", exchangeRate = 1, date = DateTime.UtcNow,
            customsDutyFee = customs, portStorageFee = port,
            items = new[] { new { brandName = "تويوتا", modelName = "كامري", year = 2025, quantity = qty, unitPrice } },
        });
        Assert.Equal(201, order.Status);
        var id = order.Data!["id"].S();
        foreach (var stage in new[] { "requisition_approved", "rfq", "rfq_approved", "purchase_order" })
            Assert.Equal(200, (await api.Post($"/carprocurementorders/{id}/AdvanceStage", new { targetStage = stage })).Status);
        return (id, supplier.ToString());
    }

    private static async Task<List<JsonNode>> OrderVehiclesAsync(Client api, string orderId)
        => (await api.Get($"/carprocurementorders/{orderId}/vehicles?pageSize=50")).Data!["items"]!.AsArray().Select(v => v!).ToList();

    private static async Task<Res> ReceiveAsync(Client api, string orderId, int qty)
        => await api.Post($"/carprocurementorders/{orderId}/AdvanceStage", new
        {
            targetStage = "vin_received", pdiInspectionPassed = true, warehouseLocation = "المعرض",
            vins = Enumerable.Range(0, qty).Select(_ => new { vin = Client.NewVin('4') }).ToArray(),
        });

    private static async Task<string> SeedCarAsync(Client api, decimal purchase = 40000, decimal? volume = null, decimal? weight = null)
    {
        var car = await api.Post("/vehicles", new
        {
            chassisNumber = Client.NewVin('5'), brandNameAr = "تويوتا", modelNameAr = "يارس", year = 2024, condition = "new", purchasePrice = purchase, sellingPrice = purchase + 10000,
            vatMode = "standard_15", fuelType = "petrol", transmission = "automatic", colorExterior = "أبيض", colorInterior = "أسود", location = "المعرض",
            volumeCbm = volume, weightKg = weight,
        });
        Assert.Equal(201, car.Status);
        return car.Data!["id"].S();
    }

    [Fact]
    public async Task Vehicles_start_in_transit_at_the_purchase_order_and_order_fees_follow_them_into_cost()
    {
        var api = await NewTenantAsync();
        var types = await TypeIdsAsync(api);
        var (supplier, _) = await SeedSupplierAsync(api);
        var order = await api.Post("/carprocurementorders", new
        {
            supplierId = supplier, paymentType = "credit", creditDays = 30, currency = "SAR", exchangeRate = 1, date = DateTime.UtcNow, customsDutyFee = 1000, portStorageFee = 100,
            items = new[] { new { brandName = "تويوتا", modelName = "كامري", year = 2025, quantity = 3, unitPrice = 80000 } },
        });
        var id = order.Data!["id"].S();

        // طلب الشراء: رسوم الحقلين أسطر تقديرية، ومصروف تقديري يدوي، والتكلفة المتوقعة الكلية تجمعها
        Assert.Equal(201, (await api.Post("/additionalcharges", new { documentType = "car_procurement_order", documentId = id, expenseTypeId = types["FREIGHT"], amount = 900, status = "estimated" })).Status);
        var expected = (await api.Get($"/carprocurementorders/{id}/expected-cost")).Data!;
        Assert.Equal(240000, expected["purchaseTotal"].D());
        Assert.Equal(2000, expected["estimatedCharges"].D());
        Assert.Equal(0, expected["actualCharges"].D());
        Assert.Equal(242000, expected["expectedTotalCost"].D());
        Assert.Empty(await OrderVehiclesAsync(api, id));

        // أمر الشراء: السيارات تُنشأ «في الطريق» بشاسيه مؤقت، والتقديرات تُوزَّع عليها دون أن تدخل تكلفتها
        foreach (var stage in new[] { "requisition_approved", "rfq", "rfq_approved", "purchase_order" })
            Assert.Equal(200, (await api.Post($"/carprocurementorders/{id}/AdvanceStage", new { targetStage = stage })).Status);
        var inTransit = await OrderVehiclesAsync(api, id);
        Assert.Equal(3, inTransit.Count);
        Assert.All(inTransit, v =>
        {
            Assert.Equal("in_transit", v["status"].S());
            Assert.StartsWith("TMP", v["chassisNumber"].S());
            Assert.Equal(80000, v["totalCost"].D());
        });
        Assert.Equal(3, (await api.Get("/vehicles?status=in_transit")).Data!["totalCount"].D());
        Assert.Empty((await api.Get("/vehicles/available")).Data!.AsArray());
        var card = (await api.Get($"/vehicles/{inTransit[0]["id"].S()}/cost-card")).Data!;
        Assert.Equal(3, card["lines"]!.AsArray().Count);
        Assert.Equal(666.66m, card["estimatedCapitalized"].D()); // 333.33 جمارك + 33.33 ميناء + 300 شحن
        Assert.Equal(80666.66m, card["expectedTotalCost"].D());

        // لا تُباع ولا يُقبض عليها عربون قبل استلامها
        var sale = await api.Post("/carsalescontracts", new { cycleType = "individual", buyerName = "م", buyerNationalIdOrCr = "1010101010", buyerPhone = "0550000000", vehicleId = inTransit[0]["id"].S(), sellingPrice = 100000, paymentMethod = "cash" });
        Assert.Equal(409, sale.Status);

        // الاستلام: السيارات نفسها تأخذ شواسيها الحقيقية وتدخل المخزون، ورسوم الأمر تصير فعلية بحصص مجموعها الرسوم تماماً
        var received = await ReceiveAsync(api, id, 3);
        Assert.Equal("received", received.Data!["status"].S());
        var vehicles = await OrderVehiclesAsync(api, id);
        Assert.Equal(inTransit.Select(v => v["id"].S()).OrderBy(x => x), vehicles.Select(v => v["id"].S()).OrderBy(x => x));
        Assert.All(vehicles, v => { Assert.Equal("available", v["status"].S()); Assert.StartsWith("4", v["chassisNumber"].S()); });
        var extras = vehicles.Select(v => v["additionalCosts"].D()).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { 366.66m, 366.66m, 366.68m }, extras); // فرق التقريب على آخر سيارة
        Assert.Equal(1100m, extras.Sum());
        Assert.All(vehicles, v => Assert.Equal(v["totalCost"].D(), v["sellingPrice"].D())); // سعر مبدئي = التكلفة حتى يُحدَّد
        expected = (await api.Get($"/carprocurementorders/{id}/expected-cost")).Data!;
        Assert.Equal(1100, expected["actualCharges"].D());
        Assert.Equal(900, expected["estimatedCharges"].D());

        // الفوترة: القيد بمجموع الأسطر نفسه = مجموع حصص السيارات
        Assert.Equal(200, (await api.Post($"/carprocurementorders/{id}/AdvanceStage", new { targetStage = "invoiced" })).Status);
        Assert.Equal(241100, (await api.Get("/accounts/ByCode/1142")).Data!["balance"].D());
        Assert.Equal(1100, (await api.Get("/accounts/ByCode/212")).Data!["balance"].D());
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
        // رسوم الأمر يديرها الأمر: لا تُعدَّل من أسطر المصاريف
        var feeLine = (await api.Get($"/additionalcharges?documentType=car_procurement_order&documentId={id}")).Data!.AsArray().First(l => l!["source"].S() == "order_fields")!;
        Assert.Equal("actual", feeLine["status"].S());
        Assert.NotNull(feeLine["journalEntryId"]);
        Assert.Equal(409, (await api.Delete($"/additionalcharges/{feeLine["id"].S()}")).Status);
    }

    [Fact]
    public async Task Editing_the_order_keeps_in_transit_vehicles_and_fee_allocations_in_step()
    {
        var api = await NewTenantAsync();
        var (id, supplier) = await OrderAtPurchaseOrderAsync(api, qty: 2, customs: 1000, port: 0);
        object Body(int qty, decimal customs) => new
        {
            supplierId = supplier, paymentType = "credit", creditDays = 30, currency = "SAR", exchangeRate = 1, date = DateTime.UtcNow, customsDutyFee = customs,
            items = new[] { new { brandName = "تويوتا", modelName = "كامري", year = 2025, quantity = qty, unitPrice = 90000 } },
        };
        var before = (await OrderVehiclesAsync(api, id)).Select(v => v["id"].S()).ToList();

        // زيادة الكمية وتغيير السعر: تُضاف سيارة وتبقى القائمتان، والسعر الجديد على الكل
        Assert.Equal(200, (await api.Put($"/carprocurementorders/{id}", Body(3, 1500))).Status);
        var three = await OrderVehiclesAsync(api, id);
        Assert.Equal(3, three.Count);
        Assert.All(before, b => Assert.Contains(three, v => v["id"].S() == b));
        Assert.All(three, v => Assert.Equal(90000, v["purchasePrice"].D()));
        var fee = (await api.Get($"/additionalcharges?documentType=car_procurement_order&documentId={id}")).Data!.AsArray().Single()!;
        Assert.Equal(1500, fee["amount"].D());
        Assert.Equal(new[] { 500m, 500m, 500m }, fee["allocations"]!.AsArray().Select(a => a!["amount"].D()).ToArray());

        // إنقاص الكمية وحذف الرسم: تُحذف السيارات الزائدة وسطر الرسم
        Assert.Equal(200, (await api.Put($"/carprocurementorders/{id}", Body(1, 0))).Status);
        Assert.Single(await OrderVehiclesAsync(api, id));
        Assert.Empty((await api.Get($"/additionalcharges?documentType=car_procurement_order&documentId={id}")).Data!.AsArray());

        // مصروف مسجَّل على السيارة نفسها يمنع حذفها بتعديل البنود
        var types = await TypeIdsAsync(api);
        Assert.Equal(200, (await api.Put($"/carprocurementorders/{id}", Body(2, 0))).Status);
        var second = (await OrderVehiclesAsync(api, id)).OrderBy(v => v["createdAt"].S()).Last()["id"].S();
        Assert.Equal(201, (await api.Post("/additionalcharges", new { documentType = "vehicle", documentId = second, expenseTypeId = types["FREIGHT"], amount = 200, status = "estimated" })).Status);
        Assert.Equal(409, (await api.Put($"/carprocurementorders/{id}", Body(1, 0))).Status);
        Assert.Equal(2, (await OrderVehiclesAsync(api, id)).Count);
    }

    [Fact]
    public async Task Rejecting_an_order_removes_untouched_in_transit_vehicles_and_keeps_those_with_actual_spend()
    {
        var api = await NewTenantAsync();
        var types = await TypeIdsAsync(api);
        var (id, _) = await OrderAtPurchaseOrderAsync(api, qty: 2, customs: 600, port: 0);
        var vehicles = await OrderVehiclesAsync(api, id);
        var spent = vehicles[0]["id"].S();
        // شحن فعلي دُفع على سيارة واحدة قبل الإلغاء
        Assert.Equal(201, (await api.Post("/additionalcharges", new
        {
            documentType = "car_procurement_order", documentId = id, expenseTypeId = types["FREIGHT"], amount = 700, status = "actual",
            allocations = new[] { new { vehicleId = spent, amount = 0 } },
        })).Status);

        Assert.Equal(200, (await api.Post($"/carprocurementorders/{id}/reject", new { reason = "تأخر المورد" })).Status);
        var left = await OrderVehiclesAsync(api, id);
        Assert.Equal(spent, left.Single()["id"].S());
        Assert.Equal("cancelled", left[0]["status"].S());
        Assert.Equal(80700, left[0]["totalCost"].D()); // المصروف الفعلي باقٍ عليها حتى يُعالَج (خسارة أو مطالبة)
        var lines = (await api.Get($"/additionalcharges?documentType=car_procurement_order&documentId={id}")).Data!.AsArray();
        Assert.Equal("cancelled", lines.Single(l => l!["source"].S() == "order_fields")!["status"].S());
        Assert.Equal("actual", lines.Single(l => l!["expenseTypeCode"].S() == "FREIGHT")!["status"].S());
        // لا مصاريف جديدة تدخل تكلفة سيارة أُلغي شراؤها
        Assert.Equal(409, (await api.Post("/additionalcharges", new { documentType = "vehicle", documentId = spent, expenseTypeId = types["REPAIR"], amount = 100, status = "actual" })).Status);
    }

    [Fact]
    public async Task Landed_cost_voucher_previews_allocates_and_replaces_estimates_with_actuals()
    {
        var api = await NewTenantAsync();
        var types = await TypeIdsAsync(api);
        var (orderId, _) = await OrderAtPurchaseOrderAsync(api, qty: 2, customs: 1000, port: 0);
        Assert.Equal(201, (await api.Post("/additionalcharges", new { documentType = "car_procurement_order", documentId = orderId, expenseTypeId = types["FREIGHT"], amount = 900, status = "estimated" })).Status);
        Assert.Equal(200, (await ReceiveAsync(api, orderId, 2)).Status);
        var vehicles = await OrderVehiclesAsync(api, orderId);
        Assert.All(vehicles, v => Assert.Equal(80500, v["totalCost"].D())); // 80000 + نصف الجمارك المستحقة
        var (shipper, _) = await SeedSupplierAsync(api, "شركة الشحن");
        Assert.Equal(201, (await api.Post("/currencies", new { code = "USD", nameAr = "دولار", nameEn = "US Dollar", symbol = "$", exchangeRate = 3.75 })).Status);

        // فاتورة المورد بالدولار بسعر صرف تاريخها: جمارك 400$ (= 1500) وشحن 200$ (= 750) ومخلّص 100$ (= 375) بلا تقدير سابق
        var body = new
        {
            date = new DateTime(2026, 10, 1), supplierId = shipper, supplierInvoiceNumber = "SH-77", currencyCode = "USD", exchangeRate = 3.75, procurementOrderId = orderId,
            lines = new object[]
            {
                new { expenseTypeId = types["CUSTOMS_DUTY"], amount = 400, allocationMethod = "equal" },
                new { expenseTypeId = types["FREIGHT"], amount = 200, vatAmount = 30, allocationMethod = "equal" },
                new { expenseTypeId = types["CUSTOMS_CLEARANCE"], amount = 100, allocationMethod = "manual",
                      allocations = new[] { new { vehicleId = vehicles[0]["id"].S(), amount = 300m }, new { vehicleId = vehicles[1]["id"].S(), amount = 75m } } },
            },
        };

        // المعاينة لا تحفظ شيئاً وتُظهر الحصص والتقديري المقابل والفرق وأثرها على كل سيارة
        var preview = await api.Post("/landedcostvouchers/preview", body);
        Assert.Equal(200, preview.Status);
        Assert.Equal(2625, preview.Data!["baseCostTotal"].D());
        var pLines = preview.Data["lines"]!.AsArray();
        Assert.Equal(1000, pLines[0]!["estimatedAmount"].D()); Assert.Equal(500, pLines[0]!["variance"].D());
        Assert.Equal(900, pLines[1]!["estimatedAmount"].D()); Assert.Equal(-150, pLines[1]!["variance"].D());
        Assert.Null(pLines[2]!["estimatedAmount"]);
        Assert.Equal(new[] { 750m, 750m }, pLines[0]!["allocations"]!.AsArray().Select(a => a!["amount"].D()).ToArray());
        var pVehicles = preview.Data["vehicles"]!.AsArray();
        Assert.All(pVehicles, v => { Assert.Equal(80500, v!["currentCost"].D()); Assert.Equal(500, v["replacedActual"].D()); });
        Assert.Equal(new[] { 81200m, 81425m }, pVehicles.Select(v => v!["costAfter"].D()).OrderBy(x => x).ToArray());
        Assert.Equal(0, (await api.Get("/landedcostvouchers")).Data!["totalCount"].D());
        Assert.All(await OrderVehiclesAsync(api, orderId), v => Assert.Equal(80500, v["totalCost"].D()));

        // المسودة لا تمسّ التكلفة
        var draft = await api.Post("/landedcostvouchers", body);
        Assert.Equal(201, draft.Status);
        var id = draft.Data!["id"].S();
        Assert.StartsWith("LCV-", draft.Data["voucherNumber"].S());
        Assert.Equal("draft", draft.Data["status"].S());
        Assert.Equal(700, draft.Data["subtotal"].D()); Assert.Equal(730, draft.Data["grandTotal"].D());
        Assert.All(await OrderVehiclesAsync(api, orderId), v => Assert.Equal(80500, v["totalCost"].D()));

        // الاعتماد بصلاحية مستقلة
        var rep = await UserAsync(api, "sales_rep");
        Assert.Equal(403, (await rep.Get($"/landedcostvouchers/{id}")).Status);
        Assert.Equal(403, (await rep.Post($"/landedcostvouchers/{id}/approve")).Status);
        var approved = await api.Post($"/landedcostvouchers/{id}/approve");
        Assert.Equal(200, approved.Status);
        Assert.Equal("approved", approved.Data!["status"].S());
        var aLines = approved.Data["lines"]!.AsArray();
        Assert.Equal(1000, aLines[0]!["estimatedAmount"].D()); Assert.Equal(500, aLines[0]!["variance"].D());
        Assert.Equal(-150, aLines[1]!["variance"].D());

        // الفعلي حلّ محل التقديري والمستحق، والتكلفة عُدّلت بالفرق
        var after = (await OrderVehiclesAsync(api, orderId)).ToDictionary(v => v["id"].S(), v => v["totalCost"].D());
        Assert.Equal(81425, after[vehicles[0]["id"].S()]); // 80000 + 750 + 375 + 300
        Assert.Equal(81200, after[vehicles[1]["id"].S()]); // 80000 + 750 + 375 + 75
        var orderLines = (await api.Get($"/additionalcharges?documentType=car_procurement_order&documentId={orderId}")).Data!.AsArray();
        Assert.All(orderLines, l => Assert.Equal("replaced", l!["status"].S()));
        var card = (await api.Get($"/vehicles/{vehicles[0]["id"].S()}/cost-card")).Data!;
        var customs = card["lines"]!.AsArray().Single(l => l!["expenseTypeCode"].S() == "CUSTOMS_DUTY")!;
        Assert.Equal(500, customs["estimatedAmount"].D()); Assert.Equal(750, customs["actualAmount"].D());
        Assert.Equal("landed_cost_voucher", customs["source"].S());
        Assert.Equal(0, card["estimatedCapitalized"].D());
        var expected = (await api.Get($"/carprocurementorders/{orderId}/expected-cost")).Data!;
        Assert.Equal(2625, expected["actualCharges"].D()); Assert.Equal(0, expected["estimatedCharges"].D());

        // المعتمد لا يُعدَّل ولا يُحذف ولا يُعتمد مرتين، وأسطره لا تُعدَّل من أسطر المصاريف
        Assert.Equal(409, (await api.Post($"/landedcostvouchers/{id}/approve")).Status);
        Assert.Equal(409, (await api.Put($"/landedcostvouchers/{id}", body)).Status);
        Assert.Equal(409, (await api.Delete($"/landedcostvouchers/{id}")).Status);
        Assert.Equal(409, (await api.Delete($"/additionalcharges/{aLines[0]!["chargeLineId"].S()}")).Status);

        // الإلغاء يعكس كل شيء: التكلفة والأسطر المستبدَلة تعود كما كانت
        Assert.Equal("cancelled", (await api.Post($"/landedcostvouchers/{id}/cancel")).Data!["status"].S());
        Assert.All(await OrderVehiclesAsync(api, orderId), v => Assert.Equal(80500, v["totalCost"].D()));
        orderLines = (await api.Get($"/additionalcharges?documentType=car_procurement_order&documentId={orderId}")).Data!.AsArray();
        Assert.Equal("actual", orderLines.Single(l => l!["source"].S() == "order_fields")!["status"].S());
        Assert.Equal("estimated", orderLines.Single(l => l!["expenseTypeCode"].S() == "FREIGHT")!["status"].S());
        Assert.Equal(409, (await api.Post($"/landedcostvouchers/{id}/cancel")).Status);
    }

    [Fact]
    public async Task Voucher_allocates_by_volume_and_weight_and_validates_its_input()
    {
        var api = await NewTenantAsync();
        var types = await TypeIdsAsync(api);
        var (shipper, _) = await SeedSupplierAsync(api, "سطحة");
        var small = await SeedCarAsync(api, purchase: 30000, volume: 10, weight: 1200);
        var large = await SeedCarAsync(api, purchase: 60000, volume: 30);
        object Voucher(string method, object? line = null) => new
        {
            supplierId = shipper, vehicleIds = new[] { small, large },
            lines = new[] { line ?? new { expenseTypeId = types["INLAND_TRANSPORT"], amount = 400m, allocationMethod = method } },
        };

        var byVolume = await api.Post("/landedcostvouchers/preview", Voucher("by_volume"));
        Assert.Equal(new[] { 100m, 300m }, byVolume.Data!["lines"]![0]!["allocations"]!.AsArray().Select(a => a!["amount"].D()).ToArray());
        var byValue = await api.Post("/landedcostvouchers/preview", Voucher("by_value"));
        Assert.Equal(new[] { 133.33m, 266.67m }, byValue.Data!["lines"]![0]!["allocations"]!.AsArray().Select(a => a!["amount"].D()).ToArray());
        Assert.Equal(400, (await api.Post("/landedcostvouchers/preview", Voucher("by_weight"))).Status); // سيارة بلا وزن

        // سند على سيارتين بلا أمر شراء: سطحة واحدة تُوزَّع وتدخل التكلفة عند الاعتماد
        var draft = await api.Post("/landedcostvouchers", Voucher("by_volume"));
        Assert.Equal(201, draft.Status);
        var id = draft.Data!["id"].S();
        Assert.Equal(200, (await api.Put($"/landedcostvouchers/{id}", Voucher("equal"))).Status);
        Assert.Equal(200, (await api.Post($"/landedcostvouchers/{id}/approve")).Status);
        Assert.Equal(30200, (await api.Get($"/vehicles/{small}")).Data!["totalCost"].D());
        Assert.Equal(60200, (await api.Get($"/vehicles/{large}")).Data!["totalCost"].D());

        // تحقق المدخلات
        Assert.Equal(400, (await api.Post("/landedcostvouchers", new { supplierId = shipper, vehicleIds = new[] { small }, lines = Array.Empty<object>() })).Status);
        Assert.Equal(400, (await api.Post("/landedcostvouchers", new { supplierId = shipper, lines = new[] { new { expenseTypeId = types["FREIGHT"], amount = 100 } } })).Status); // بلا سيارات
        Assert.Equal(400, (await api.Post("/landedcostvouchers", Voucher("equal", new { expenseTypeId = types["FREIGHT"], amount = 0m, allocationMethod = "equal" }))).Status);
        Assert.Equal(400, (await api.Post("/landedcostvouchers", new { supplierId = Guid.NewGuid(), vehicleIds = new[] { small }, lines = new[] { new { expenseTypeId = types["FREIGHT"], amount = 100 } } })).Status);
        Assert.Equal(400, (await api.Post("/landedcostvouchers", new { supplierId = shipper, currencyCode = "EUR", exchangeRate = 4, vehicleIds = new[] { small }, lines = new[] { new { expenseTypeId = types["FREIGHT"], amount = 100 } } })).Status);
        var deletable = await api.Post("/landedcostvouchers", Voucher("equal"));
        Assert.Equal(200, (await api.Delete($"/landedcostvouchers/{deletable.Data!["id"].S()}")).Status);

        // سيارة مباعة: السند يمسّ تكلفتها بقيد تعديل موثق فقط (صلاحية مستقلة)، وفرقه على تكلفة المبيعات
        var soldDraft = await api.Post("/landedcostvouchers", Voucher("equal"));
        Assert.Equal(201, (await api.Post("/carsalescontracts/QuickSale", new { contract = new { cycleType = "individual", buyerName = "مشتري", buyerNationalIdOrCr = "1010101010", buyerPhone = "0550000000",
            vehicleId = small, sellingPrice = 45000, vatMode = "standard_15", paymentMethod = "cash", condition = "new" } })).Status);
        Assert.Equal(200, (await api.Post($"/landedcostvouchers/{soldDraft.Data!["id"].S()}/approve")).Status);
        var adjustments = (await api.Get($"/vehicles/{small}/cost-adjustments")).Data!.AsArray();
        Assert.Equal(200, adjustments.Single()!["amount"].D());
        Assert.Equal("landed_cost_voucher", adjustments[0]!["source"].S());
        Assert.Equal(30400, (await api.Get($"/vehicles/{small}")).Data!["totalCost"].D());
    }

    private async Task<Client> UserAsync(Client owner, string role)
    {
        var email = $"u{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await owner.Post("/users", new { name = "مستخدم", email, phone = "1", password = "Passw0rd!", role })).Status);
        var user = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");
        Assert.NotNull(user.Token);
        return user;
    }
}
