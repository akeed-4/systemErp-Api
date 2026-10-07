using ERP.Tests.Infrastructure;

namespace ERP.Tests;

[Collection("api")]
public class AgreementDeliveryContractTests : TestBase
{
    public AgreementDeliveryContractTests(ErpFactory f) : base(f) { }

    private static object Order(Guid agreement, Guid product, decimal qty, string status, string type = "sales_order")
        => new { type, partyName = "عميل", orderDate = DateTime.UtcNow, expectedDeliveryDate = DateTime.UtcNow.AddDays(3), status, agreementId = agreement,
            items = new[] { new { itemId = product, itemName = "منتج", sku = "P1", unit = "PCS", quantity = qty, unitPrice = 100, vatRate = 15 } } };

    private static object Agreement(Guid product, decimal agreedQty, Guid? itemId = null)
        => new { type = "sales", partyName = "عميل", startDate = DateTime.UtcNow.AddDays(-1), status = "active",
            items = new[] { new { id = itemId ?? Guid.Empty, itemId = product, itemName = "منتج", quantity = agreedQty, unitPrice = 100, minQuantity = 2, maxQuantity = 6 } } };

    [Fact]
    public async Task Agreement_limits_orders_and_usage_follows_confirmed_orders_only()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        var agr = await api.Post("/agreements", Agreement(product, 10));
        Assert.Equal(201, agr.Status);
        var id = Guid.Parse(agr.Data!["id"].S()); var line = Guid.Parse(agr.Data["items"]![0]!["id"].S());
        async Task<decimal> Used() => (await api.Get($"/agreements/{id}")).Data!["items"]![0]!["usedQuantity"].D();

        Assert.Equal(400, (await api.Post("/commercialorders", Order(id, product, 1, "confirmed"))).Status); // تحت الحد الأدنى
        Assert.Equal(400, (await api.Post("/commercialorders", Order(id, product, 7, "confirmed"))).Status); // فوق الحد الأقصى
        Assert.Equal(400, (await api.Post("/commercialorders", Order(id, product, 3, "confirmed", "purchase_order"))).Status); // نوع مخالف
        var first = await api.Post("/commercialorders", Order(id, product, 6, "confirmed"));
        Assert.Equal(201, first.Status); Assert.Equal(6, await Used());
        Assert.Equal(400, (await api.Post("/commercialorders", Order(id, product, 5, "confirmed"))).Status); // المتبقي 4

        var second = await api.Post("/commercialorders", Order(id, product, 5, "draft")); // المسودة لا تستهلك
        Assert.Equal(201, second.Status); Assert.Equal(6, await Used());
        var secondId = second.Data!["id"].S();
        Assert.Equal(400, (await api.Put($"/commercialorders/{secondId}", Order(id, product, 5, "confirmed"))).Status);

        Assert.Equal(200, (await api.Put($"/commercialorders/{first.Data!["id"].S()}", Order(id, product, 6, "cancelled"))).Status);
        Assert.Equal(0, await Used());
        Assert.Equal(200, (await api.Put($"/commercialorders/{secondId}", Order(id, product, 5, "confirmed"))).Status);
        Assert.Equal(5, await Used());

        Assert.Equal(400, (await api.Put($"/agreements/{id}", Agreement(product, 4, line))).Status); // أقل من المستهلك
        var raised = await api.Put($"/agreements/{id}", Agreement(product, 12, line));
        Assert.Equal(200, raised.Status); Assert.Equal(5, raised.Data!["items"]![0]!["usedQuantity"].D()); // لا يُقبل من العميل
        Assert.Equal(409, (await api.Delete($"/agreements/{id}")).Status);
    }

    [Theory]
    [InlineData("sales")]
    [InlineData("purchase")]
    public async Task Agreement_payment_schedule_must_total_100_percent_and_keeps_the_sent_order(string type)
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        object Body(params object[] payments) => new
        {
            type, partyName = "طرف", startDate = DateTime.UtcNow.AddDays(-1), status = "active",
            items = new[] { new { itemId = product, itemName = "منتج", quantity = 10, unitPrice = 100 } }, payments,
        };
        static object Pay(decimal percentage, string? description = null, Guid? id = null, string? dueDate = null)
            => new { id = id ?? Guid.Empty, percentage, description, dueDate };

        Assert.Equal(400, (await api.Post("/agreements", Body(Pay(60), Pay(30)))).Status);  // 90%
        Assert.Equal(400, (await api.Post("/agreements", Body(Pay(60), Pay(50)))).Status);  // 110%
        Assert.Equal(400, (await api.Post("/agreements", Body(Pay(100), Pay(0)))).Status);  // دفعة بلا نسبة
        api.Language = "en";
        Assert.Equal("The agreement payment percentages must total 100% (current total 90%).", (await api.Post("/agreements", Body(Pay(60), Pay(30)))).Body!["message"].S());
        api.Language = null;

        var created = await api.Post("/agreements", Body(Pay(30, "مقدَّم", dueDate: "2026-11-01"), Pay(50), Pay(20, "عند التسليم")));
        Assert.Equal(201, created.Status);
        var id = created.Data!["id"].S();
        var payments = created.Data["payments"]!.AsArray();
        Assert.Equal(new[] { 1, 2, 3 }, payments.Select(p => p!["sequence"]!.GetValue<int>()));
        Assert.Equal(new[] { "مقدَّم", "دفعة 2", "عند التسليم" }, payments.Select(p => p!["description"].S()));
        Assert.Equal(new[] { 30m, 50m, 20m }, payments.Select(p => p!["percentage"].D()));
        Assert.StartsWith("2026-11-01", payments[0]!["dueDate"].S());

        // تعديل: حذف الثانية، تقديم الثالثة، وإضافة دفعة جديدة — الترتيب كما أُرسل والمعرّفات محفوظة
        Guid first = payments[0]!["id"].G(), third = payments[2]!["id"].G();
        Assert.Equal(400, (await api.Put($"/agreements/{id}", Body(Pay(25, "عند التسليم", third), Pay(30, "مقدَّم", first)))).Status);
        var updated = await api.Put($"/agreements/{id}", Body(Pay(25, "عند التسليم", third), Pay(30, "مقدَّم", first), Pay(45, "ختامية")));
        Assert.Equal(200, updated.Status);
        var after = (await api.Get($"/agreements/{id}")).Data!["payments"]!.AsArray();
        Assert.Equal(new[] { "عند التسليم", "مقدَّم", "ختامية" }, after.Select(p => p!["description"].S()));
        Assert.Equal(new[] { 1, 2, 3 }, after.Select(p => p!["sequence"]!.GetValue<int>()));
        Assert.Equal(third, after[0]!["id"].G()); Assert.Equal(first, after[1]!["id"].G());

        // جدول السداد اختياري
        Assert.Empty((await api.Put($"/agreements/{id}", Body())).Data!["payments"]!.AsArray());
    }

    [Fact]
    public async Task Delivery_note_is_priced_on_the_server_and_returns_use_the_original_price()
    {
        var api = await NewTenantAsync();
        var note = await api.Post("/deliverynotes", new { type = "sales_delivery", partyName = "عميل", date = DateTime.UtcNow,
            driverName = "سائق", vehiclePlate = "أ ب ج 123", warehouseLocation = "المستودع الرئيسي", notes = "تسليم جزئي",
            subtotal = 1, grandTotal = 1, // تتجاهلها الخادم
            items = new[] { new { itemId = Guid.NewGuid(), itemName = "بند", unit = "PCS", contractQty = 10, deliveredQty = 4, unitPrice = 50, vatRate = 15 } } });
        Assert.Equal(200, note.Status);
        Assert.Equal(200, note.Data!["subtotal"].D()); Assert.Equal(30, note.Data["vatTotal"].D()); Assert.Equal(230, note.Data["grandTotal"].D());
        Assert.Equal(230, note.Data["items"]![0]!["totalAfterVat"].D());
        Assert.Equal("سائق", note.Data["driverName"].S()); Assert.Equal("المستودع الرئيسي", note.Data["warehouseLocation"].S());

        var item = note.Data["items"]![0]!["itemId"].S();
        var ret = await api.Post("/deliverynotes/returns", new { type = "sales_delivery_return", deliveryNoteId = note.Data["id"].S(), returnReason = "تالف",
            items = new[] { new { itemId = item, quantity = 2, unitPrice = 999 } } });
        Assert.Equal(200, ret.Status);
        Assert.Equal(115, ret.Data!["grandTotal"].D()); Assert.Equal(50, ret.Data["items"]![0]!["unitPrice"].D());
        Assert.Equal("عميل", ret.Data["partyName"].S()); Assert.Equal("تالف", ret.Data["returnReason"].S());
    }

    [Fact]
    public async Task Delivery_note_invoices_its_net_quantities_once()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        var (customer, _) = await SeedCustomerAsync(api);
        var note = await api.Post("/deliverynotes", new { type = "sales_delivery", partyName = "عميل", partyId = customer, date = DateTime.UtcNow,
            items = new object[]
            {
                new { itemId = product, itemName = "منتج", unit = "PCS", contractQty = 4, deliveredQty = 4, unitPrice = 50, vatRate = 15 },
                new { itemId = Guid.NewGuid(), itemName = "تركيب", unit = "خدمة", contractQty = 1, deliveredQty = 1, unitPrice = 20, vatRate = 15 },
            } });
        var id = note.Data!["id"].S();
        await api.Post("/deliverynotes/returns", new { type = "sales_delivery_return", deliveryNoteId = id, items = new[] { new { itemId = product, quantity = 1 } } });

        var inv = await api.Post($"/deliverynotes/{id}/Invoice");
        Assert.Equal(200, inv.Status);
        Assert.Equal(195.5m, inv.Data!["grandTotal"].D()); // (3×50 + 20) × 1.15
        Assert.Equal("posted", inv.Data["status"].S());
        Assert.Equal(409, (await api.Post($"/deliverynotes/{id}/Invoice")).Status);
        var after = (await api.Get($"/deliverynotes/{id}")).Data!;
        Assert.Equal("invoiced", after["status"].S()); Assert.Equal(inv.Data["invoiceNumber"].S(), after["invoiceNumber"].S());
        Assert.Equal(7, (await api.Get($"/products/{product}")).Data!["currentStock"].D()); // 10 - 3 صافي
    }

    [Fact]
    public async Task Fixed_asset_without_accounts_uses_the_default_asset_and_depreciation_accounts()
    {
        var api = await NewTenantAsync();
        var center = (await api.Post("/costcenters", new { code = "CC-1", nameAr = "الإدارة" })).Data!["id"].S();
        var fa = await api.Post("/fixedassets", new { assetCode = "FA-1", nameAr = "خادم", nameEn = "Server", purchaseDate = DateTime.UtcNow, purchaseCost = 1000, currentBookValue = 1000, depreciationRate = 20, costCenterId = center });
        Assert.Equal(201, fa.Status);
        var asset = (await api.Get("/accounts/ByCode/121")).Data!; var dep = (await api.Get("/accounts/ByCode/122")).Data!;
        Assert.Equal(asset["id"].S(), fa.Data!["assetAccountId"].S()); Assert.Equal(dep["id"].S(), fa.Data["accumulatedDepreciationAccountId"].S());
        Assert.False(dep["isDebitNature"]!.GetValue<bool>()); // حساب مقابل للأصل
    }

    [Fact]
    public async Task Contract_keeps_terms_and_items_and_computes_retention()
    {
        var api = await NewTenantAsync();
        var body = new { title = "عقد توريد", contractType = "supply", partyName = "عميل", contractValue = 1000, vatRate = 15, retentionPercent = 5m,
            durationMonths = 12, scopeOfWork = "توريد وتركيب", latePenaltyPerDay = 100, maxPenaltyPercent = 10,
            items = new[] { new { description = "جهاز", unit = "PCS", quantity = 2, unitPrice = 100, vatRate = 15 } },
            milestones = new[] { new { title = "دفعة", percentage = 100, retentionDeductionPercent = 10 } }, clauses = Array.Empty<object>() };
        var ct = await api.Post("/commercialcontracts", body);
        Assert.Equal(201, ct.Status);
        Assert.Equal(50, ct.Data!["retentionAmount"].D()); Assert.Equal("توريد وتركيب", ct.Data["scopeOfWork"].S());
        Assert.Equal(230, ct.Data["items"]![0]!["totalWithVat"].D());
        Assert.Equal(1050, ct.Data["milestones"]![0]!["netPayableAmount"].D()); // 1150 - 10% من 1000
        Assert.Equal(400, (await api.Post("/commercialcontracts", new { title = "عقد", partyName = "عميل", contractValue = 10, retentionPercent = 150m, items = Array.Empty<object>(), milestones = Array.Empty<object>(), clauses = Array.Empty<object>() })).Status);
    }
}