using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>المرحلة الأولى من المصاريف والتكاليف الإضافية: أنواع المصاريف، أسطرها، كرت تكلفة السيارة، وحجب التكلفة.</summary>
[Collection("api")]
public class VehicleCostTests : TestBase
{
    public VehicleCostTests(ErpFactory f) : base(f) { }

    private static async Task<string> SeedCarAsync(Client api, char vinPrefix, decimal purchase = 40000, decimal selling = 50000,
        decimal additional = 0, decimal preparation = 0, string vatMode = "standard_15", string condition = "new")
    {
        var car = await api.Post("/vehicles", new
        {
            chassisNumber = Client.NewVin(vinPrefix), brandNameAr = "تويوتا", modelNameAr = "يارس", year = 2024, condition, mileageKm = condition == "used" ? 50000 : 0,
            purchasePrice = purchase, additionalCosts = additional, preparationCost = preparation, sellingPrice = selling,
            vatMode, fuelType = "petrol", transmission = "automatic", colorExterior = "أبيض", colorInterior = "أسود", location = "المعرض",
        });
        Assert.Equal(201, car.Status);
        return car.Data!["id"].S();
    }

    private static async Task<Dictionary<string, JsonNode>> TypesAsync(Client api)
    {
        var list = await api.Get("/expensetypes?pageSize=200");
        Assert.Equal(200, list.Status);
        return list.Data!["items"]!.AsArray().ToDictionary(t => t!["code"].S(), t => t!);
    }

    private static Task<Res> ChargeAsync(Client api, string vehicleId, string typeId, decimal amount, string status = "actual", string? treatment = null)
        => api.Post("/additionalcharges", new { documentType = "vehicle", documentId = vehicleId, expenseTypeId = typeId, amount, status, treatment });

    private async Task<Client> UserAsync(Client owner, string role)
    {
        var email = $"u{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await owner.Post("/users", new { name = "مستخدم", email, phone = "1", password = "Passw0rd!", role })).Status);
        var user = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");
        Assert.NotNull(user.Token);
        return user;
    }

    /// <summary>أمر شراء مستلَم بسيارتين مختلفتَي الثمن (بلا رسوم جمارك/موانئ على الأمر).</summary>
    private async Task<(string OrderId, List<JsonNode> Vehicles)> ReceivedOrderAsync(Client api, decimal firstPrice = 60000, decimal secondPrice = 30000, int secondQty = 1)
    {
        var (supplier, _) = await SeedSupplierAsync(api);
        var order = await api.Post("/carprocurementorders", new
        {
            supplierId = supplier, paymentType = "credit", creditDays = 30, currency = "SAR", exchangeRate = 1, date = DateTime.UtcNow,
            items = new[]
            {
                new { brandName = "تويوتا", modelName = "كامري", year = 2025, quantity = 1, unitPrice = firstPrice },
                new { brandName = "تويوتا", modelName = "يارس", year = 2025, quantity = secondQty, unitPrice = secondPrice },
            },
        });
        Assert.Equal(201, order.Status);
        var id = order.Data!["id"].S();
        foreach (var stage in new[] { "requisition_approved", "rfq", "rfq_approved", "purchase_order" })
            Assert.Equal(200, (await api.Post($"/carprocurementorders/{id}/AdvanceStage", new { targetStage = stage })).Status);
        var received = await api.Post($"/carprocurementorders/{id}/AdvanceStage", new
        {
            targetStage = "vin_received", pdiInspectionPassed = true, warehouseLocation = "المعرض",
            vins = Enumerable.Range(0, 1 + secondQty).Select(_ => new { vin = Client.NewVin('2') }).ToArray(),
        });
        Assert.Equal(200, received.Status);
        var vehicles = (await api.Get($"/vehicles?pageSize=50")).Data!["items"]!.AsArray().Select(v => v!).OrderByDescending(v => v["purchasePrice"].D()).ToList();
        return (id, vehicles);
    }

    [Fact]
    public async Task Expense_types_are_seeded_per_tenant_and_system_types_are_protected()
    {
        var api = await NewTenantAsync();
        var types = await TypesAsync(api);
        foreach (var code in new[] { "FREIGHT", "CUSTOMS_DUTY", "IMPORT_VAT", "TAMM_TRANSFER_IN", "TAMM_TRANSFER_OUT", "MAINTENANCE", "PLATES", "SALES_COMMISSION", "TAMM_SUBSCRIPTION", "OPENING_ADDITIONAL" })
            Assert.True(types.ContainsKey(code), code);
        Assert.Equal("capitalize", types["FREIGHT"]["defaultTreatment"].S());
        Assert.Equal("recharge_to_customer", types["TAMM_TRANSFER_OUT"]["defaultTreatment"].S());
        Assert.Equal("expense", types["SALES_COMMISSION"]["defaultTreatment"].S());
        Assert.True(types["IMPORT_VAT"]["isInputTax"]!.GetValue<bool>());

        // القائمة لا تكرر البذور
        Assert.Equal(types.Count, (await TypesAsync(api)).Count);

        var freight = types["FREIGHT"];
        Assert.Equal(409, (await api.Delete($"/expensetypes/{freight["id"].S()}")).Status);
        Assert.Equal(409, (await api.Put($"/expensetypes/{freight["id"].S()}", new { code = "FREIGHT2", nameAr = "شحن", stage = "purchase", defaultTreatment = "capitalize", defaultAllocationMethod = "equal", vatCategory = "standard" })).Status);
        // النوع المزروع يُعدَّل ويُعطَّل
        var edited = await api.Put($"/expensetypes/{freight["id"].S()}", new { code = "FREIGHT", nameAr = "شحن بحري", stage = "purchase", defaultTreatment = "expense", defaultAllocationMethod = "by_value", vatCategory = "standard", isActive = false });
        Assert.Equal(200, edited.Status);
        Assert.Equal("expense", edited.Data!["defaultTreatment"].S());
        Assert.True(edited.Data["isSystem"]!.GetValue<bool>());

        var custom = await api.Post("/expensetypes", new { code = "towing", nameAr = "سطحة", stage = "purchase", defaultTreatment = "capitalize", defaultAllocationMethod = "equal", vatCategory = "standard", defaultAmount = 300 });
        Assert.Equal(201, custom.Status);
        Assert.Equal("TOWING", custom.Data!["code"].S());
        Assert.Equal(409, (await api.Post("/expensetypes", new { code = "TOWING", nameAr = "مكرر" })).Status);
        Assert.Equal(400, (await api.Post("/expensetypes", new { code = "X1", nameAr = "" })).Status);
        Assert.Equal(400, (await api.Post("/expensetypes", new { code = "X2", nameAr = "حساب", accountCode = "99999" })).Status);
        Assert.Equal(200, (await api.Delete($"/expensetypes/{custom.Data["id"].S()}")).Status);
    }

    [Fact]
    public async Task Costs_entered_with_a_vehicle_become_opening_lines_and_cost_fields_turn_read_only()
    {
        var api = await NewTenantAsync();
        var vid = await SeedCarAsync(api, '3', purchase: 50000, additional: 1000, preparation: 500, selling: 60000);

        var card = await api.Get($"/vehicles/{vid}/cost-card");
        Assert.Equal(200, card.Status);
        Assert.Equal(51500, card.Data!["totalCost"].D());
        Assert.Equal(50000, card.Data["purchasePrice"].D());
        var lines = card.Data["lines"]!.AsArray();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Equal("opening_balance", l!["source"].S()));
        Assert.Equal(1000, lines.Single(l => l!["expenseTypeCode"].S() == "OPENING_ADDITIONAL")!["actualAmount"].D());
        Assert.Equal(500, lines.Single(l => l!["expenseTypeCode"].S() == "OPENING_PREPARATION")!["actualAmount"].D());

        // التحويل آمن للتكرار: لا أسطر جديدة ولا تغيّر في التكلفة
        Assert.Equal(0, (await api.Post("/vehicles/MigrateLegacyCosts")).Data!["created"].D());
        Assert.Equal(0, (await api.Post("/vehicles/MigrateLegacyCosts")).Data!["created"].D());
        Assert.Equal(2, (await api.Get($"/vehicles/{vid}/cost-card")).Data!["lines"]!.AsArray().Count);

        var vehicle = (await api.Get($"/vehicles/{vid}")).Data!;
        object Body(decimal? purchase, decimal? additional) => new
        {
            chassisNumber = vehicle["chassisNumber"].S(), brandNameAr = "تويوتا", modelNameAr = "يارس", year = 2024, condition = "new", sellingPrice = 61000,
            purchasePrice = purchase, additionalCosts = additional, preparationCost = (decimal?)null, vatMode = "standard_15", fuelType = "petrol", transmission = "automatic",
            colorExterior = "أحمر", colorInterior = "أسود", location = "المعرض",
        };
        Assert.Equal(409, (await api.Put($"/vehicles/{vid}", Body(55000, 1000))).Status);
        Assert.Equal(409, (await api.Put($"/vehicles/{vid}", Body(50000, 0))).Status);
        // القيم المطابقة أو الفارغة تُقبل وتبقى التكلفة كما هي
        var same = await api.Put($"/vehicles/{vid}", Body(50000, 1000));
        Assert.Equal(200, same.Status);
        var blank = await api.Put($"/vehicles/{vid}", Body(null, null));
        Assert.Equal(200, blank.Status);
        Assert.Equal(51500, blank.Data!["totalCost"].D());
        Assert.Equal(61000, blank.Data["sellingPrice"].D());

        // أسطر الرصيد الافتتاحي لا تُعدَّل ولا تُحذف، وتُحذف مع المركبة
        var opening = lines[0]!["lineId"].S();
        Assert.Equal(409, (await api.Delete($"/additionalcharges/{opening}")).Status);
        Assert.Equal(409, (await api.Post($"/additionalcharges/{opening}/cancel")).Status);
        Assert.Equal(200, (await api.Delete($"/vehicles/{vid}")).Status);
    }

    [Fact]
    public async Task Only_actual_capitalized_lines_enter_the_vehicle_cost()
    {
        var api = await NewTenantAsync();
        var types = await TypesAsync(api);
        var vid = await SeedCarAsync(api, '4', purchase: 40000, selling: 50000);

        // تقديري: يظهر في الكرت ولا يدخل التكلفة
        var estimated = await ChargeAsync(api, vid, types["FREIGHT"]["id"].S(), 2000, "estimated");
        Assert.Equal(201, estimated.Status);
        Assert.Equal("capitalize", estimated.Data!["treatment"].S());
        Assert.Equal(vid, estimated.Data["allocations"]![0]!["vehicleId"].S());
        Assert.Equal(40000, (await api.Get($"/vehicles/{vid}")).Data!["totalCost"].D());

        // فعلي مُرسمَل: شراء ← تكاليف إضافية، تجهيز ← تكلفة التجهيز
        Assert.Equal(201, (await ChargeAsync(api, vid, types["CUSTOMS_DUTY"]["id"].S(), 1500)).Status);
        var repair = await ChargeAsync(api, vid, types["REPAIR"]["id"].S(), 700);
        Assert.Equal(201, repair.Status);
        // مصروف على المعرض ومحمَّل على العميل: لا يدخلان التكلفة
        Assert.Equal(201, (await ChargeAsync(api, vid, types["SALES_COMMISSION"]["id"].S(), 900)).Status);
        Assert.Equal(201, (await ChargeAsync(api, vid, types["PLATES"]["id"].S(), 400)).Status);

        var vehicle = (await api.Get($"/vehicles/{vid}")).Data!;
        Assert.Equal(1500, vehicle["additionalCosts"].D());
        Assert.Equal(700, vehicle["preparationCost"].D());
        Assert.Equal(42200, vehicle["totalCost"].D());

        var card = (await api.Get($"/vehicles/{vid}/cost-card")).Data!;
        Assert.Equal(42200, card["totalCost"].D());
        Assert.Equal(2000, card["estimatedCapitalized"].D());
        Assert.Equal(44200, card["expectedTotalCost"].D());
        Assert.Equal(900, card["actualExpensed"].D());
        Assert.Equal(400, card["actualRecharged"].D());
        Assert.Equal(5, card["lines"]!.AsArray().Count);
        var freightRow = card["lines"]!.AsArray().Single(l => l!["expenseTypeCode"].S() == "FREIGHT")!;
        Assert.Equal(2000, freightRow["estimatedAmount"].D());
        Assert.Null(freightRow["actualAmount"]);

        // تحقُّق التقديري يُدخله التكلفة، وتعديل المبلغ يُعيد اشتقاقها
        var actualized = await api.Put($"/additionalcharges/{estimated.Data["id"].S()}", new { documentType = "vehicle", documentId = vid, expenseTypeId = types["FREIGHT"]["id"].S(), amount = 2300, status = "actual" });
        Assert.Equal(200, actualized.Status);
        Assert.Equal(44500, (await api.Get($"/vehicles/{vid}")).Data!["totalCost"].D());

        // الإلغاء يُخرج السطر من التكلفة ويبقيه في السجل، والحذف يزيله
        Assert.Equal("cancelled", (await api.Post($"/additionalcharges/{repair.Data!["id"].S()}/cancel")).Data!["status"].S());
        Assert.Equal(43800, (await api.Get($"/vehicles/{vid}")).Data!["totalCost"].D());
        Assert.Equal(409, (await api.Post($"/additionalcharges/{repair.Data["id"].S()}/cancel")).Status);
        Assert.Equal(200, (await api.Delete($"/additionalcharges/{estimated.Data["id"].S()}")).Status);
        Assert.Equal(41500, (await api.Get($"/vehicles/{vid}")).Data!["totalCost"].D());
        Assert.Equal(4, (await api.Get($"/additionalcharges?vehicleId={vid}")).Data!.AsArray().Count);

        // تحقق المدخلات
        Assert.Equal(400, (await ChargeAsync(api, vid, types["FREIGHT"]["id"].S(), 0)).Status);
        Assert.Equal(400, (await ChargeAsync(api, vid, types["FREIGHT"]["id"].S(), 100, "cancelled")).Status);
        Assert.Equal(400, (await ChargeAsync(api, vid, Guid.NewGuid().ToString(), 100)).Status);
        Assert.Equal(400, (await api.Post("/additionalcharges", new { documentType = "bogus", documentId = vid, expenseTypeId = types["FREIGHT"]["id"].S(), amount = 10 })).Status);
        Assert.Equal(400, (await api.Get("/additionalcharges")).Status);
    }

    [Fact]
    public async Task One_charge_is_allocated_across_the_vehicles_of_a_document()
    {
        var api = await NewTenantAsync();
        var types = await TypesAsync(api);
        var (orderId, vehicles) = await ReceivedOrderAsync(api, firstPrice: 60000, secondPrice: 30000, secondQty: 2);
        var ids = vehicles.Select(v => v["id"].S()).ToList();
        object Allocations(params decimal[] amounts) => ids.Select((id, i) => new { vehicleId = id, amount = amounts.Length > i ? amounts[i] : 0 }).ToArray();
        Task<Res> Charge(string type, decimal amount, string method, object allocations) => api.Post("/additionalcharges", new
        {
            documentType = "car_procurement_order", documentId = orderId, expenseTypeId = types[type]["id"].S(), amount, status = "actual", allocationMethod = method, allocations,
        });

        // بالتساوي: فرق التقريب على آخر سيارة
        var equal = await Charge("FREIGHT", 100, "equal", Allocations());
        Assert.Equal(201, equal.Status);
        Assert.Equal(new[] { 33.33m, 33.33m, 33.34m }, equal.Data!["allocations"]!.AsArray().Select(a => a!["amount"].D()).ToArray());

        // حسب القيمة: 60000 / 30000 / 30000
        var byValue = await Charge("CUSTOMS_DUTY", 1000, "by_value", Allocations());
        Assert.Equal(new[] { 500m, 250m, 250m }, byValue.Data!["allocations"]!.AsArray().Select(a => a!["amount"].D()).ToArray());

        // يدوي: المجموع يجب أن يساوي المبلغ
        Assert.Equal(400, (await Charge("PORT_STORAGE", 300, "manual", Allocations(100, 100, 50))).Status);
        Assert.Equal(201, (await Charge("PORT_STORAGE", 300, "manual", Allocations(200, 100, 0))).Status);
        Assert.Equal(400, (await Charge("FREIGHT", 300, "by_volume", Allocations())).Status);

        var costs = new List<decimal>();
        foreach (var id in ids) costs.Add((await api.Get($"/vehicles/{id}")).Data!["additionalCosts"].D());
        Assert.Equal(new[] { 733.33m, 383.33m, 283.34m }, costs);
        Assert.Equal(1400m, costs.Sum());

        // مصروف تقديري على مستوى الأمر بلا سيارات مقبول، والفعلي المُرسمَل يتطلب سيارة
        Assert.Equal(201, (await api.Post("/additionalcharges", new { documentType = "car_procurement_order", documentId = orderId, expenseTypeId = types["FREIGHT"]["id"].S(), amount = 500, status = "estimated" })).Status);
        Assert.Equal(400, (await api.Post("/additionalcharges", new { documentType = "car_procurement_order", documentId = orderId, expenseTypeId = types["FREIGHT"]["id"].S(), amount = 500, status = "actual" })).Status);
        // سيارة لا تتبع المستند
        var other = await SeedCarAsync(api, '5');
        Assert.Equal(400, (await api.Post("/additionalcharges", new
        {
            documentType = "car_procurement_order", documentId = orderId, expenseTypeId = types["FREIGHT"]["id"].S(), amount = 500, status = "actual",
            allocations = new[] { new { vehicleId = other, amount = 0 } },
        })).Status);
        Assert.Equal(4, (await api.Get($"/additionalcharges?documentType=car_procurement_order&documentId={orderId}")).Data!.AsArray().Count);
    }

    [Fact]
    public async Task Foreign_currency_and_recoverable_import_vat_are_handled_in_base_currency()
    {
        var api = await NewTenantAsync();
        var types = await TypesAsync(api);
        var vid = await SeedCarAsync(api, '6', purchase: 40000);
        Assert.Equal(201, (await api.Post("/currencies", new { code = "USD", nameAr = "دولار", nameEn = "US Dollar", symbol = "$", exchangeRate = 3.75 })).Status);

        var freight = await api.Post("/additionalcharges", new { documentType = "vehicle", documentId = vid, expenseTypeId = types["FREIGHT"]["id"].S(), amount = 100, currencyCode = "usd", exchangeRate = 3.75, vatAmount = 15, status = "actual" });
        Assert.Equal(201, freight.Status);
        Assert.Equal(375, freight.Data!["baseAmount"].D());
        Assert.Equal(375, freight.Data["costAmount"].D()); // الضريبة قابلة للاسترداد فلا تدخل التكلفة
        Assert.Equal(400, (await api.Post("/additionalcharges", new { documentType = "vehicle", documentId = vid, expenseTypeId = types["FREIGHT"]["id"].S(), amount = 100, currencyCode = "EUR", exchangeRate = 4, status = "actual" })).Status);
        Assert.Equal(400, (await api.Post("/additionalcharges", new { documentType = "vehicle", documentId = vid, expenseTypeId = types["FREIGHT"]["id"].S(), amount = 100, currencyCode = "USD", exchangeRate = 0, status = "actual" })).Status);

        // ضريبة الاستيراد: ضريبة مدخلات مستردة لا تدخل التكلفة
        var importVat = await ChargeAsync(api, vid, types["IMPORT_VAT"]["id"].S(), 6000);
        Assert.Equal(201, importVat.Status);
        Assert.Equal(0, importVat.Data!["costAmount"].D());
        Assert.Equal(40375, (await api.Get($"/vehicles/{vid}")).Data!["totalCost"].D());
        // رسوم خارج نطاق الضريبة لا تُسجَّل عليها ضريبة
        Assert.Equal(400, (await api.Post("/additionalcharges", new { documentType = "vehicle", documentId = vid, expenseTypeId = types["CUSTOMS_DUTY"]["id"].S(), amount = 100, vatAmount = 15, status = "actual" })).Status);

        // ضريبة غير مستردة تدخل في المصروف نفسه (إعداد على النوع يحدده المحاسب)
        var repairType = types["REPAIR"];
        Assert.Equal(200, (await api.Put($"/expensetypes/{repairType["id"].S()}", new { code = "REPAIR", nameAr = "إصلاح", stage = "holding", defaultTreatment = "capitalize", defaultAllocationMethod = "equal", vatCategory = "standard", isVatRecoverable = false })).Status);
        var repair = await api.Post("/additionalcharges", new { documentType = "vehicle", documentId = vid, expenseTypeId = repairType["id"].S(), amount = 1000, vatAmount = 150, status = "actual" });
        Assert.Equal(1150, repair.Data!["costAmount"].D());
        Assert.Equal(41525, (await api.Get($"/vehicles/{vid}")).Data!["totalCost"].D());
    }

    [Fact]
    public async Task Cost_of_a_sold_vehicle_changes_only_with_a_documented_reason()
    {
        var api = await NewTenantAsync();
        var types = await TypesAsync(api);
        var vid = await SeedCarAsync(api, '7');
        var before = await ChargeAsync(api, vid, types["POLISHING"]["id"].S(), 200);
        var sale = await api.Post("/carsalescontracts/QuickSale", new { contract = new { cycleType = "individual", buyerName = "مشتري", buyerNationalIdOrCr = "1010101010", buyerPhone = "0550000000",
            vehicleId = vid, sellingPrice = 50000, vatMode = "standard_15", paymentMethod = "cash", condition = "new" } });
        Assert.Equal(201, sale.Status);
        Assert.Equal(40200, sale.Data!["costPrice"].D()); // التكلفة الكلية بعد المصروف المُرسمَل

        // بلا سبب موثق لا تُمسّ تكلفة السيارة المباعة
        Assert.Equal(400, (await ChargeAsync(api, vid, types["REPAIR"]["id"].S(), 300)).Status);
        Assert.Equal(400, (await api.Post($"/additionalcharges/{before.Data!["id"].S()}/cancel")).Status);
        Assert.Equal(400, (await api.Delete($"/additionalcharges/{before.Data["id"].S()}")).Status);
        // ما لا يمسّ التكلفة يبقى متاحاً بعد البيع
        Assert.Equal(201, (await ChargeAsync(api, vid, types["REPAIR"]["id"].S(), 300, "estimated")).Status);
        Assert.Equal(201, (await ChargeAsync(api, vid, types["SALES_COMMISSION"]["id"].S(), 500)).Status);
        Assert.Equal(40200, (await api.Get($"/vehicles/{vid}")).Data!["totalCost"].D());
    }

    [Fact]
    public async Task Cost_and_profit_are_hidden_without_the_cost_view_permission()
    {
        var owner = await NewTenantAsync();
        var types = await TypesAsync(owner);
        var vid = await SeedCarAsync(owner, '9', purchase: 40000, additional: 1000, selling: 50000);
        var used = await SeedCarAsync(owner, '9', purchase: 30000, selling: 40000, vatMode: "profit_margin_15", condition: "used");
        await ChargeAsync(owner, vid, types["PLATES"]["id"].S(), 400, "estimated");
        var rep = await UserAsync(owner, "sales_rep");
        var accountant = await UserAsync(owner, "chief_accountant");

        // المندوب: يرى المركبة وسعرها دون التكلفة
        var seen = (await rep.Get($"/vehicles/{vid}")).Data!;
        Assert.Equal(50000, seen["sellingPrice"].D());
        Assert.Null(seen["totalCost"]); Assert.Null(seen["purchasePrice"]); Assert.Null(seen["additionalCosts"]);
        Assert.Equal(7500, seen["vatAmount"].D());
        Assert.Null((await rep.Get($"/vehicles/{used}")).Data!["vatAmount"]); // ضريبة الهامش تكشف التكلفة
        Assert.All((await rep.Get("/vehicles")).Data!["items"]!.AsArray(), v => Assert.Null(v!["totalCost"]));
        Assert.All((await rep.Get("/vehicles/available")).Data!.AsArray(), v => Assert.Null(v!["totalCost"]));
        Assert.Equal(403, (await rep.Get($"/vehicles/{vid}/cost-card")).Status);

        // الأسطر: يرى المحمَّل على العميل فقط
        var visible = (await rep.Get($"/additionalcharges?vehicleId={vid}")).Data!.AsArray();
        Assert.Equal("PLATES", visible.Single()!["expenseTypeCode"].S());
        var openingId = (await owner.Get($"/additionalcharges?vehicleId={vid}")).Data!.AsArray().First(l => l!["source"].S() == "opening_balance")!["id"].S();
        Assert.Equal(403, (await rep.Get($"/additionalcharges/{openingId}")).Status);

        // العقد: بلا تكلفة ولا هامش
        var body = new { cycleType = "individual", buyerName = "مشتري", buyerNationalIdOrCr = "1010101010", buyerPhone = "0550000000", vehicleId = vid, sellingPrice = 50000, paymentMethod = "cash", condition = "new" };
        var contract = await rep.Post("/carsalescontracts", body);
        Assert.Equal(201, contract.Status);
        Assert.Null(contract.Data!["costPrice"]); Assert.Null(contract.Data["profitMargin"]);
        Assert.Equal(57500, contract.Data["totalWithVat"].D());
        var asOwner = (await owner.Get($"/carsalescontracts/{contract.Data["id"].S()}")).Data!;
        Assert.Equal(41000, asOwner["costPrice"].D()); Assert.Equal(9000, asOwner["profitMargin"].D());
        Assert.All((await rep.Get("/carsalescontracts")).Data!["items"]!.AsArray(), c => Assert.Null(c!["costPrice"]));

        // تعديل المعالجة الافتراضية بصلاحية مستقلة
        Assert.Equal(403, (await ChargeAsync(rep, vid, types["REPAIR"]["id"].S(), 300, "estimated", "expense")).Status);
        // الرسم البيعي: «على العميل» أو «على المعرض» خيار لكل سطر بلا صلاحية خاصة
        Assert.Equal(201, (await ChargeAsync(rep, vid, types["PLATES"]["id"].S(), 300, "estimated", "expense")).Status);
        Assert.Equal(403, (await ChargeAsync(rep, vid, types["PLATES"]["id"].S(), 300, "estimated", "capitalize")).Status);
        Assert.Equal(201, (await ChargeAsync(rep, vid, types["PLATES"]["id"].S(), 300, "estimated")).Status);
        Assert.Equal(201, (await ChargeAsync(accountant, vid, types["REPAIR"]["id"].S(), 300, "estimated", "expense")).Status);

        // رئيس الحسابات يرى التكلفة افتراضياً
        Assert.Equal(41000, (await accountant.Get($"/vehicles/{vid}")).Data!["totalCost"].D());
        Assert.Equal(200, (await accountant.Get($"/vehicles/{vid}/cost-card")).Status);
        Assert.Equal(200, (await accountant.Get("/reports/car/ProfitLoss")).Status);
    }

    [Fact]
    public async Task Saved_permissions_that_predate_the_cost_screens_stay_restricted_until_granted()
    {
        var owner = await NewTenantAsync();
        var vid = await SeedCarAsync(owner, 'A');
        var rep = await UserAsync(owner, "sales_rep");
        string[] costScreens = { "car-cost-view", "charge-treatment-override", "landed-cost-approval", "sold-vehicle-adjustment" };

        var defaults = (await owner.Get("/permissions?roleId=sales_rep")).Data!.AsArray();
        Assert.All(costScreens, s => Assert.False(defaults.Single(p => p!["screenId"].S() == s)!["canView"]!.GetValue<bool>()));
        object Row(JsonNode p, bool? view = null) => new
        {
            screenId = p["screenId"].S(), screenNameAr = p["screenNameAr"].S(), screenNameEn = p["screenNameEn"].S(),
            canView = view ?? p["canView"]!.GetValue<bool>(), canCreate = p["canCreate"]!.GetValue<bool>(),
            canEdit = p["canEdit"]!.GetValue<bool>(), canDelete = p["canDelete"]!.GetValue<bool>(), canApprove = p["canApprove"]!.GetValue<bool>(),
        };

        // صلاحيات محفوظة بلا الشاشات الجديدة (كما لو حُفظت قبل إضافتها): تبقى التكلفة محجوبة وتظهر الشاشات في شبكة التعديل
        var legacy = defaults.Where(p => !costScreens.Contains(p!["screenId"].S())).Select(p => Row(p!)).ToList();
        Assert.Equal(200, (await owner.Post("/permissions", new { roleId = "sales_rep", permissions = legacy })).Status);
        Assert.Null((await rep.Get($"/vehicles/{vid}")).Data!["totalCost"]);
        Assert.Equal(403, (await rep.Get($"/vehicles/{vid}/cost-card")).Status);
        var saved = (await owner.Get("/permissions?roleId=sales_rep")).Data!.AsArray();
        Assert.False(saved.Single(p => p!["screenId"].S() == "car-cost-view")!["canView"]!.GetValue<bool>());

        // منحها صراحةً يكشف التكلفة
        var granted = saved.Select(p => Row(p!, p!["screenId"].S() == "car-cost-view" ? true : null)).ToList();
        Assert.Equal(200, (await owner.Post("/permissions", new { roleId = "sales_rep", permissions = granted })).Status);
        Assert.Equal(40000, (await rep.Get($"/vehicles/{vid}")).Data!["totalCost"].D());
        Assert.Equal(200, (await rep.Get($"/vehicles/{vid}/cost-card")).Status);
    }
}
