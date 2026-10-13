using System.Diagnostics;
using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>فاتورة شراء السيارات بالدفعات: سطر لكل دفعة بشواصيه، بطاقات مسودة تُدار من الفاتورة، والترحيل والمرتجع الجزئي.</summary>
[Collection("api")]
public class VehicleBatchTests : TestBase
{
    public VehicleBatchTests(ErpFactory f) : base(f) { }

    private static object Chassis(string vin, decimal? price = null, string? color = null, string? engine = null)
        => new { vin, priceOverride = price, colorExterior = color ?? string.Empty, engineNumber = engine };

    private static object Batch(decimal unitPrice, object[] chassis, int generateCount = 0, decimal discount = 0, string model = "كامري", string vatMode = "standard_15") => new
    {
        brandNameAr = "تويوتا", modelNameAr = model, trimNameAr = "GLE", year = 2025, colorExterior = "أبيض", colorInterior = "بيج",
        vatMode, unitPrice, discount, generateCount, chassis,
    };

    private static object Purchase(Guid supplier, string status, params object[] batches) => new
    {
        kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier, status, vehicleBatches = batches,
    };

    private static async Task<decimal> BalanceAsync(Client api, string code) => (await api.Get($"/accounts/ByCode/{code}")).Data!["balance"].D();
    private static async Task<JsonArray> VehiclesAsync(Client api, string? status = null)
        => (await api.Get("/vehicles?pageSize=1000" + (status == null ? "" : $"&status={status}"))).Data!["items"]!.AsArray();
    private static List<JsonNode> ChassisOf(Res invoice, int batch = 0) => invoice.Data!["vehicleBatches"]![batch]!["chassis"]!.AsArray().Select(c => c!).ToList();

    [Fact]
    public async Task Generated_temporary_chassis_numbers_are_unique_and_flagged()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var first = await api.Post("/invoices", Purchase(supplier, "draft", Batch(50000, Array.Empty<object>(), generateCount: 3)));
        Assert.Equal(201, first.Status);
        var batch = first.Data!["vehicleBatches"]![0]!;
        Assert.Equal(3, batch["quantity"].D());                       // الكمية = عدد الشواصي
        Assert.Equal(150000, batch["totalBeforeVat"].D()); Assert.Equal(22500, batch["vatAmount"].D());
        var vins = ChassisOf(first).Select(c => c["vin"].S()).ToList();
        Assert.Equal(3, vins.Distinct().Count());
        Assert.All(ChassisOf(first), c => { Assert.True(c["isTemporaryChassis"]!.GetValue<bool>()); Assert.StartsWith("TMP", c["vin"].S()); Assert.Equal(17, c["vin"].S().Length); });

        // دفعة تجمع المولَّد واليدوي، وفاتورة ثانية لا تكرر أرقام الأولى
        var real = Client.NewVin('1');
        var second = await api.Post("/invoices", Purchase(supplier, "draft", Batch(50000, new[] { Chassis(real) }, generateCount: 2)));
        Assert.Equal(201, second.Status);
        var secondVins = ChassisOf(second).Select(c => c["vin"].S()).ToList();
        Assert.Equal(3, secondVins.Count); Assert.Contains(real, secondVins);
        Assert.Empty(vins.Intersect(secondVins));
        Assert.False(ChassisOf(second).First(c => c["vin"].S() == real)["isTemporaryChassis"]!.GetValue<bool>());

        // بطاقات المسودة لا تظهر في المخزون ولا البيع
        Assert.Empty(await VehiclesAsync(api));
        Assert.Empty((await api.Get("/vehicles/available")).Data!.AsArray());
        Assert.Equal(6, (await VehiclesAsync(api, "draft")).Count);
        Assert.Equal(0, await BalanceAsync(api, "1142")); // المسودة بلا قيد

        Assert.Equal(400, (await api.Post("/invoices", Purchase(supplier, "draft", Batch(50000, Array.Empty<object>())))).Status); // دفعة بلا شواصي
    }

    [Fact]
    public async Task Chassis_are_validated_in_bulk_within_the_list_the_invoice_and_the_company()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var inStock = Client.NewVin('2');
        Assert.Equal(201, (await api.Post("/vehicles", new { chassisNumber = inStock, brandNameAr = "تويوتا", modelNameAr = "يارس", year = 2024, sellingPrice = 1 })).Status);
        var inDraft = Client.NewVin('3');
        var draft = await api.Post("/invoices", Purchase(supplier, "draft", Batch(1000, new[] { Chassis(inDraft) })));
        var good = Client.NewVin('4');

        // صفوف استيراد: سليم، صيغة خاطئة، مكرر في الملف، موجود في المخزون، موجود في مسودة أخرى، فارغ
        var check = await api.Post("/vehicles/ValidateChassis", new { vins = new[] { good, "SHORT", good.ToLowerInvariant(), inStock, inDraft, " " } });
        Assert.Equal(200, check.Status);
        var issues = check.Data!.AsArray().ToDictionary(i => (int)i!["index"].D(), i => i!["code"].S());
        Assert.False(issues.ContainsKey(0));
        Assert.Equal("format", issues[1]); Assert.Equal("duplicate", issues[2]); Assert.Equal("exists", issues[3]); Assert.Equal("exists", issues[4]); Assert.Equal("required", issues[5]);
        Assert.Contains(draft.Data!["invoiceNumber"].S(), check.Data.AsArray().First(i => i!["index"].D() == 4)!["message"].S()); // تُذكر المسودة التي تحجزه
        // شواصي المسودة نفسها ليست تكراراً عند تعديلها
        Assert.Empty((await api.Post("/vehicles/ValidateChassis", new { vins = new[] { inDraft }, invoiceId = draft.Data["id"].S() })).Data!.AsArray());

        // الحفظ يطبّق التحقق نفسه: داخل الدفعة، بين دفعات الفاتورة، ومع سيارات المنشأة
        Assert.Equal(400, (await api.Post("/invoices", Purchase(supplier, "draft", Batch(1000, new[] { Chassis(good), Chassis(good) })))).Status);
        Assert.Equal(400, (await api.Post("/invoices", Purchase(supplier, "draft", Batch(1000, new[] { Chassis(good) }), Batch(2000, new[] { Chassis(good) }, model: "كورولا")))).Status);
        Assert.Equal(400, (await api.Post("/invoices", Purchase(supplier, "draft", Batch(1000, new[] { Chassis("BAD") })))).Status);
        Assert.Equal(409, (await api.Post("/invoices", Purchase(supplier, "draft", Batch(1000, new[] { Chassis(inStock) })))).Status);
        Assert.Equal(409, (await api.Post("/invoices", Purchase(supplier, "posted", Batch(1000, new[] { Chassis(inDraft) })))).Status);
        Assert.Single(await VehiclesAsync(api, "draft")); // لم يُحفظ شيء من المرفوض
    }

    [Fact]
    public async Task Draft_cards_follow_the_invoice_through_edit_delete_and_posting()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var a = Client.NewVin('1'); var b = Client.NewVin('2'); var c = Client.NewVin('3'); var d = Client.NewVin('4');

        var draft = await api.Post("/invoices", Purchase(supplier, "draft", Batch(40000, new[] { Chassis(a), Chassis(b), Chassis(c) })));
        Assert.Equal(201, draft.Status);
        var id = draft.Data!["id"].S();
        var cards = (await VehiclesAsync(api, "draft")).ToDictionary(v => v!["chassisNumber"].S(), v => v!["id"].S());
        Assert.Equal(3, cards.Count);
        Assert.All(ChassisOf(draft), ch => Assert.Equal(cards[ch["vin"].S()], ch["vehicleId"].S())); // كل شاسيه مرتبط ببطاقته
        Assert.Equal(409, (await api.Put($"/vehicles/{cards[a]}", new { chassisNumber = a, brandNameAr = "تويوتا", modelNameAr = "كامري", year = 2025, sellingPrice = 1 })).Status); // تُدار من الفاتورة

        // تعديل المسودة: حذف شاسيه، إضافة آخر، وتغيير لون وسعر: البطاقات تتبع
        var edited = await api.Put($"/invoices/{id}", Purchase(supplier, "draft", Batch(41000, new[] { Chassis(a, color: "أسود", engine: "ENG-1"), Chassis(b), Chassis(d) })));
        Assert.Equal(200, edited.Status);
        var after = (await VehiclesAsync(api, "draft")).ToDictionary(v => v!["chassisNumber"].S(), v => v!);
        Assert.Equal(new[] { a, b, d }.OrderBy(x => x), after.Keys.OrderBy(x => x));
        Assert.Equal(cards[a], after[a]["id"].S());                 // الباقي احتفظ ببطاقته
        Assert.Equal("أسود", after[a]["colorExterior"].S()); Assert.Equal("ENG-1", after[a]["engineNumber"].S());
        Assert.Equal(41000, after[a]["purchasePrice"].D());

        // حذف المسودة يحذف بطاقاتها ويحرر شواصيها
        Assert.Equal(200, (await api.Delete($"/invoices/{id}")).Status);
        Assert.Empty(await VehiclesAsync(api, "draft"));

        // مسودة جديدة بالشواصي نفسها ثم ترحيلها: البطاقات تدخل المخزون والقيد بإجمالي السطر
        var again = await api.Post("/invoices", Purchase(supplier, "draft", Batch(40000, new[] { Chassis(a), Chassis(b), Chassis(c) })));
        Assert.Equal(201, again.Status);
        var posted = await api.Post($"/invoices/{again.Data!["id"].S()}/post", new { });
        Assert.Equal(200, posted.Status);
        Assert.Equal("posted", posted.Data!["status"].S());
        Assert.Single(posted.Data["items"]!.AsArray());                                  // بند واحد للدفعة لا بند لكل سيارة
        Assert.Equal(3, posted.Data["items"]![0]!["quantity"].D());
        Assert.Equal(120000, posted.Data["subtotal"].D()); Assert.Equal(138000, posted.Data["grandTotal"].D());
        var stock = await VehiclesAsync(api);
        Assert.Equal(3, stock.Count);
        Assert.All(stock, v => { Assert.Equal("available", v!["status"].S()); Assert.Equal(40000, v["totalCost"].D()); });
        Assert.Equal(120000, await BalanceAsync(api, "1142"));
        Assert.Equal(138000, (await api.Get($"/suppliers/{supplier}")).Data!["currentBalance"].D());

        // بعد الترحيل: لا تعديل ولا حذف لشاسيه، فقط مرتجع
        Assert.Equal(409, (await api.Put($"/invoices/{again.Data["id"].S()}", Purchase(supplier, "draft", Batch(40000, new[] { Chassis(a) })))).Status);
        Assert.Equal(409, (await api.Delete($"/invoices/{again.Data["id"].S()}")).Status);
        Assert.Equal(409, (await api.Delete($"/vehicles/{stock[0]!["id"].S()}")).Status);
        var (dr, cr) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(dr, cr);
    }

    [Fact]
    public async Task Special_chassis_price_drives_the_line_total_and_partial_return_reduces_the_batch()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var a = Client.NewVin('5'); var b = Client.NewVin('6'); var special = Client.NewVin('7');

        // سعر الوحدة 50000 بخصم 1000، وشاسيه بسعر خاص 52000: الإجمالي = 49000 × 2 + 51000
        var inv = await api.Post("/invoices", Purchase(supplier, "posted", Batch(50000, new[] { Chassis(a), Chassis(b), Chassis(special, price: 52000) }, discount: 1000)));
        Assert.Equal(201, inv.Status);
        var batch = inv.Data!["vehicleBatches"]![0]!;
        Assert.Equal(3, batch["quantity"].D());
        Assert.Equal(149000, batch["totalBeforeVat"].D()); Assert.Equal(22350, batch["vatAmount"].D()); Assert.Equal(171350, batch["totalAfterVat"].D());
        Assert.Equal(149000, inv.Data["subtotal"].D()); Assert.Equal(22350, inv.Data["vatTotal"].D()); Assert.Equal(171350, inv.Data["grandTotal"].D());
        Assert.Equal(2, inv.Data["items"]!.AsArray().Count); // بند بسعر الدفعة وبند للسعر الخاص
        var cars = (await VehiclesAsync(api)).ToDictionary(v => v!["chassisNumber"].S(), v => v!);
        Assert.Equal(49000, cars[a]["purchasePrice"].D()); Assert.Equal(51000, cars[special]["purchasePrice"].D());
        Assert.Equal(149000, await BalanceAsync(api, "1142"));
        Assert.Equal(400, (await api.Post("/invoices", Purchase(supplier, "draft", Batch(50000, new[] { Chassis(Client.NewVin('8'), price: 500) }, discount: 1000)))).Status); // سعر خاص أقل من الخصم

        // مرتجع جزئي: الشاسيه ذو السعر الخاص وشاسيه عادي
        var ret = await api.Post("/invoices/returns", new { originalInvoiceId = inv.Data["id"].S(), returnReason = "عيب مصنعي", vehicleIds = new[] { cars[special]["id"].S(), cars[a]["id"].S() } });
        Assert.Equal(200, ret.Status);
        Assert.Equal(100000, ret.Data!["subtotal"].D());           // 51000 + 49000
        Assert.Equal(115000, ret.Data["grandTotal"].D());
        var after = await api.Get($"/invoices/{inv.Data["id"].S()}");
        Assert.Equal(2, after.Data!["vehicleBatches"]![0]!["returnedQuantity"].D());              // الكمية والإجمالي ينقصان بقدر المرتجع
        Assert.Equal(100000, after.Data["vehicleBatches"]![0]!["returnedTotal"].D());
        Assert.Equal(2, ChassisOf(after).Count(ch => ch["isReturned"]!.GetValue<bool>()));
        Assert.Equal(171350 - 115000, (await api.Get($"/suppliers/{supplier}")).Data!["currentBalance"].D());
        Assert.Single(await VehiclesAsync(api), v => v!["status"].S() == "available");

        // لا يُرجَع الشاسيه مرتين، والباقي يُرجَع
        Assert.Equal(409, (await api.Post("/invoices/returns", new { originalInvoiceId = inv.Data["id"].S(), returnReason = "x", vehicleIds = new[] { cars[a]["id"].S() } })).Status);
        Assert.Equal(200, (await api.Post("/invoices/returns", new { originalInvoiceId = inv.Data["id"].S(), returnReason = "x" })).Status);
        Assert.Equal(0, (await api.Get($"/suppliers/{supplier}")).Data!["currentBalance"].D());
        var (dr, cr) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(dr, cr);
    }

    [Fact]
    public async Task Temporary_chassis_is_posted_but_not_sold_until_replaced_by_the_real_number()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var inv = await api.Post("/invoices", Purchase(supplier, "posted", Batch(30000, Array.Empty<object>(), generateCount: 2)));
        Assert.Equal(201, inv.Status);
        var cars = await VehiclesAsync(api);
        Assert.Equal(2, cars.Count);
        Assert.All(cars, v => { Assert.Equal("available", v!["status"].S()); Assert.True(v["isTemporaryChassis"]!.GetValue<bool>()); });
        var first = cars[0]!["id"].S(); var second = cars[1]!["id"].S();

        // الشاسيه المؤقت لا يُباع
        object Sale(string vehicleId) => new { cycleType = "individual", buyerName = "مشتري", buyerNationalIdOrCr = "1010101010", buyerPhone = "0550000000", vehicleId, sellingPrice = 40000, vatMode = "standard_15", paymentMethod = "cash", condition = "new" };
        Assert.Equal(409, (await api.Post("/carsalescontracts", Sale(first))).Status);
        Assert.Equal(400, (await api.Post("/invoices", new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "posted", vehicleLines = new[] { new { vehicleId = first, unitPrice = 40000, discount = 0 } } })).Status);

        // الاستبدال: الرقم الحقيقي فقط، غير مكرر، ولشاسيه مؤقت فقط
        var real = Client.NewVin('9');
        Assert.Equal(400, (await api.Post("/vehicles/ReplaceChassis", new[] { new { vehicleId = first, chassisNumber = "BAD" } })).Status);
        Assert.Equal(400, (await api.Post("/vehicles/ReplaceChassis", new[] { new { vehicleId = first, chassisNumber = cars[1]!["chassisNumber"].S() } })).Status); // رقم مؤقت
        Assert.Equal(400, (await api.Post("/vehicles/ReplaceChassis", new[] { new { vehicleId = first, chassisNumber = real }, new { vehicleId = second, chassisNumber = real } })).Status);
        var done = await api.Post("/vehicles/ReplaceChassis", new[] { new { vehicleId = first, chassisNumber = real } });
        Assert.Equal(200, done.Status); Assert.Equal(1, done.Data!["replaced"].D());
        var card = (await api.Get($"/vehicles/{first}")).Data!;
        Assert.Equal(real, card["chassisNumber"].S()); Assert.False(card["isTemporaryChassis"]!.GetValue<bool>());
        // الفاتورة المرحّلة تعرض الرقم الحقيقي
        var line = ChassisOf(await api.Get($"/invoices/{inv.Data!["id"].S()}")).First(c => c["vehicleId"].S() == first);
        Assert.Equal(real, line["vin"].S()); Assert.False(line["isTemporaryChassis"]!.GetValue<bool>());
        Assert.Equal(409, (await api.Post("/vehicles/ReplaceChassis", new[] { new { vehicleId = first, chassisNumber = Client.NewVin('8') } })).Status); // لم يعد مؤقتاً
        Assert.Equal(400, (await api.Post("/vehicles/ReplaceChassis", new[] { new { vehicleId = second, chassisNumber = real } })).Status);             // موجود

        Assert.Equal(201, (await api.Post("/carsalescontracts", Sale(first))).Status);
    }

    [Fact]
    public async Task Additional_charges_are_allocated_across_the_vehicles_of_a_batch()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var inv = await api.Post("/invoices", Purchase(supplier, "posted", Batch(30000, new[] { Chassis(Client.NewVin('1')), Chassis(Client.NewVin('2')), Chassis(Client.NewVin('3'), price: 60000) })));
        Assert.Equal(201, inv.Status);
        var ids = ChassisOf(inv).Select(c => c["vehicleId"].S()).ToArray();
        var freight = (await api.Get("/expensetypes?pageSize=200")).Data!["items"]!.AsArray().First(t => t!["code"].S() == "FREIGHT")!["id"].S();

        // شحن 1200 حسب القيمة على سيارات الدفعة (30000، 30000، 60000)
        var voucher = await api.Post("/landedcostvouchers", new { supplierId = supplier, vehicleIds = ids, lines = new[] { new { expenseTypeId = freight, amount = 1200, allocationMethod = "by_value" } } });
        Assert.Equal(201, voucher.Status);
        Assert.Equal(200, (await api.Post($"/landedcostvouchers/{voucher.Data!["id"].S()}/approve")).Status);
        var costs = (await VehiclesAsync(api)).ToDictionary(v => v!["id"].S(), v => v!["totalCost"].D());
        Assert.Equal(30300, costs[ids[0]]); Assert.Equal(30300, costs[ids[1]]); Assert.Equal(60600, costs[ids[2]]);
    }

    [Fact]
    public async Task A_batch_of_500_vehicles_is_saved_and_posted_in_bulk()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        // 300 شاسيه حقيقي + 200 مؤقت يولّدها السرفر
        var real = Enumerable.Range(0, 300).Select(i => Chassis("1HGCM82633B" + i.ToString("D6"))).ToArray();
        var watch = Stopwatch.StartNew();
        var inv = await api.Post("/invoices", Purchase(supplier, "posted", Batch(20000, real, generateCount: 200)));
        watch.Stop();
        Assert.Equal(201, inv.Status);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"حفظ وترحيل 500 سيارة استغرق {watch.Elapsed.TotalSeconds:0.0} ثانية");
        Assert.Equal(500, inv.Data!["vehicleBatches"]![0]!["quantity"].D());
        Assert.Single(inv.Data["items"]!.AsArray());
        Assert.Equal(10_000_000, inv.Data["subtotal"].D());
        Assert.Equal(500, (await api.Get("/vehicles?status=available")).Data!["totalCount"].D());
        Assert.Equal(10_000_000, await BalanceAsync(api, "1142"));
    }

    [Fact]
    public async Task Purchase_order_vehicles_carry_temporary_chassis_until_received()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var order = await api.Post("/carprocurementorders", new { supplierId = supplier, paymentType = "credit", creditDays = 30, currency = "SAR", exchangeRate = 1, date = DateTime.UtcNow,
            items = new[] { new { brandName = "تويوتا", modelName = "كامري", year = 2025, quantity = 2, unitPrice = 80000 } } });
        var id = order.Data!["id"].S();
        foreach (var s in new[] { "requisition_approved", "rfq", "rfq_approved", "purchase_order" }) await api.Post($"/carprocurementorders/{id}/AdvanceStage", new { targetStage = s });
        var transit = await VehiclesAsync(api, "in_transit");
        Assert.Equal(2, transit.Count);
        Assert.All(transit, v => Assert.True(v!["isTemporaryChassis"]!.GetValue<bool>()));

        var rec = await api.Post($"/carprocurementorders/{id}/AdvanceStage", new { targetStage = "vin_received", pdiInspectionPassed = true, vins = new[] { new { vin = Client.NewVin('1') }, new { vin = Client.NewVin('2') } } });
        Assert.Equal(200, rec.Status);
        Assert.All(await VehiclesAsync(api), v => { Assert.Equal("available", v!["status"].S()); Assert.False(v["isTemporaryChassis"]!.GetValue<bool>()); });
    }
}
