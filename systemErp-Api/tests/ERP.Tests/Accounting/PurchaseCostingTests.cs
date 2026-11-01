using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// تكلفة الصنف من مستندات الشراء بكل طرق التكلفة: صافي السطر بعد خصمه وحصته من خصم الفاتورة وبلا ضريبة، وبالعملة الأساسية،
/// ثم المتوسط المتحرك وFIFO وآخر شراء والمعيارية، والمرتجع وتعديل الفاتورة. بعد كل عملية: حساب المخزون = قيمة الأصناف،
/// وبطاقة الصنف (ItemLedger) ترصد الكمية والقيمة نفسها.
/// </summary>
[Collection("api")]
public class PurchaseCostingTests : TestBase
{
    public PurchaseCostingTests(ErpFactory f) : base(f) { }

    private static object Policy(string method)
        => new { method, recalculateOnNewPurchase = true, includeFreightAndCustoms = true, negativeInventoryPolicy = "prohibit", standardCostVarianceAccountCode = "513" };

    private sealed record Line(Guid ItemId, decimal Quantity, decimal UnitPrice, decimal Discount = 0);

    private static object Purchase(Guid supplier, IEnumerable<Line> lines, decimal invoiceDiscount = 0, string currency = "SAR", decimal rate = 1)
        => new
        {
            kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier, invoiceDiscount,
            currencyCode = currency, exchangeRate = rate,
            items = lines.Select(l => new { itemId = l.ItemId, quantity = l.Quantity, unitPrice = l.UnitPrice, discount = l.Discount, vatRate = 15 }).ToArray(),
        };

    private static async Task<JsonNode> BuyAsync(Client api, Guid supplier, Guid item, decimal qty, decimal price)
    {
        var r = await api.PostInvoice(Purchase(supplier, new[] { new Line(item, qty, price) }));
        Assert.True(r.Status == 201, r.Body?.ToJsonString());
        return r.Data!;
    }

    private static async Task<decimal> SellAsync(Client api, Guid item, decimal qty)
    {
        var r = await api.PostInvoice(new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash",
            items = new[] { new { itemId = item, quantity = qty, unitPrice = 100, vatRate = 15 } } });
        Assert.True(r.Status == 201, r.Body?.ToJsonString());
        return r.Data!["totalCost"].D();
    }

    private static async Task<JsonObject> ProductAsync(Client api, Guid id) => (await api.Get($"/products/{id}")).Data!.AsObject();
    private static async Task<decimal> CostAsync(Client api, Guid id) => (await ProductAsync(api, id))["averageCost"].D();
    private static async Task<decimal> BalanceAsync(Client api, string code) => (await api.Get($"/accounts/ByCode/{code}")).Data!["balance"].D();

    private static async Task<JsonArray> LedgerAsync(Client api, Guid item)
    {
        var r = await api.Get($"/reports/ItemLedger?itemId={item}");
        Assert.True(r.Status == 200, r.Body?.ToJsonString());
        return r.Data!.AsArray();
    }

    /// <summary>حساب المخزون = الرصيد × التكلفة لكل صنف، بلا فرق.</summary>
    private static async Task AssertBooksMatchAsync(Client api, decimal expected)
    {
        var items = (await api.Get("/reports/InventoryReconciliation")).Data!["rows"]!.AsArray().Single(r => r!["key"].S() == "items")!;
        Assert.True(items["difference"].D() == 0, items.ToJsonString());
        Assert.Equal(expected, items["ledgerValue"].D());
        Assert.Equal(expected, await BalanceAsync(api, MainSiteInventory));
    }

    /// <summary>آخر سطر في بطاقة الصنف يطابق رصيد الصنف وقيمته الدفترية.</summary>
    private static async Task AssertLedgerMatchesItemAsync(Client api, Guid item)
    {
        var p = await ProductAsync(api, item);
        var last = (await LedgerAsync(api, item)).Last()!;
        Assert.Equal(p["currentStock"].D(), last["runningStockBalance"].D());
        Assert.True(Math.Round(p["currentStock"].D() * p["averageCost"].D(), 2) == last["runningStockValue"].D(),
            $"بطاقة الصنف {last["runningStockValue"].D()} ≠ القيمة الدفترية {p["currentStock"].D()} × {p["averageCost"].D()}");
    }

    [Fact]
    public async Task Unit_cost_is_the_line_net_after_line_and_invoice_discounts_without_vat()
    {
        var api = await NewTenantAsync();
        var a = await SeedProductAsync(api, "A", stock: 0);
        var b = await SeedProductAsync(api, "B", stock: 0);
        var (supplier, supplierAccount) = await SeedSupplierAsync(api);

        // A: 10 × 20 − 20 = 180، B: 5 × 40 = 200، خصم الفاتورة 38 يتوزع 18 / 20 بنسبة الصافي
        var r = await api.PostInvoice(Purchase(supplier, new[] { new Line(a, 10, 20, 20), new Line(b, 5, 40) }, invoiceDiscount: 38));
        Assert.True(r.Status == 201, r.Body?.ToJsonString());
        Assert.Equal(16.2m, await CostAsync(api, a)); // 162 ÷ 10
        Assert.Equal(36m, await CostAsync(api, b));   // 180 ÷ 5
        var lines = r.Data!["items"]!.AsArray();
        Assert.Equal(new[] { 16.2m, 36m }, lines.Select(l => l!["unitCost"].D()));

        await AssertBooksMatchAsync(api, 342m);                   // ضريبة المدخلات خارج التكلفة
        Assert.Equal(51.3m, await BalanceAsync(api, "1131"));     // 15% من 342
        Assert.Equal(393.3m, await BalanceAsync(api, supplierAccount)); // رصيد المورد يُعرض موجباً (دائن)
        await AssertLedgerMatchesItemAsync(api, a);
    }

    [Fact]
    public async Task Foreign_currency_purchase_is_costed_in_base_currency()
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api, stock: 0);
        var (supplier, _) = await SeedSupplierAsync(api);
        var r = await api.PostInvoice(Purchase(supplier, new[] { new Line(item, 4, 10) }, currency: "USD", rate: 3.75m));
        Assert.True(r.Status == 201, r.Body?.ToJsonString());
        Assert.Equal(37.5m, await CostAsync(api, item));
        await AssertBooksMatchAsync(api, 150m);
    }

    [Fact]
    public async Task Moving_average_blends_purchases_and_sales_go_out_at_the_average()
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api, stock: 0);
        var (supplier, _) = await SeedSupplierAsync(api);

        await BuyAsync(api, supplier, item, 10, 10);
        await BuyAsync(api, supplier, item, 10, 16);
        Assert.Equal(13m, await CostAsync(api, item));        // (100 + 160) ÷ 20
        Assert.Equal(65m, await SellAsync(api, item, 5));      // 5 × 13
        await BuyAsync(api, supplier, item, 5, 20);
        Assert.Equal(14.75m, await CostAsync(api, item));     // (15 × 13 + 100) ÷ 20
        await AssertBooksMatchAsync(api, 295m);

        var ledger = await LedgerAsync(api, item);
        Assert.Equal(new[] { "purchase", "purchase", "sales", "purchase" }, ledger.Select(l => l!["docType"].S()));
        Assert.Equal(new[] { 10m, 16m, 13m, 20m }, ledger.Select(l => l!["unitCost"].D()));
        Assert.Equal(new[] { 10m, 20m, 15m, 20m }, ledger.Select(l => l!["runningStockBalance"].D()));
        Assert.Equal(new[] { 100m, 260m, 195m, 295m }, ledger.Select(l => l!["runningStockValue"].D()));
        await AssertLedgerMatchesItemAsync(api, item);
    }

    [Fact]
    public async Task Fifo_sales_consume_the_oldest_purchase_layers()
    {
        var api = await NewTenantAsync();
        Assert.Equal(200, (await api.Put("/costing/policy", Policy("fifo"))).Status);
        var item = await SeedProductAsync(api, stock: 0);
        var (supplier, _) = await SeedSupplierAsync(api);

        await BuyAsync(api, supplier, item, 10, 10);
        await BuyAsync(api, supplier, item, 10, 20);
        Assert.Equal(200m, await SellAsync(api, item, 15));   // 10 × 10 + 5 × 20
        Assert.Equal(20m, await CostAsync(api, item));        // الباقي من الدفعة الثانية
        await AssertBooksMatchAsync(api, 100m);
        await AssertLedgerMatchesItemAsync(api, item);
        Assert.Equal(100m, await SellAsync(api, item, 5));
        await AssertBooksMatchAsync(api, 0m);
    }

    [Fact]
    public async Task Last_purchase_reprices_the_whole_balance_and_the_item_card_follows()
    {
        var api = await NewTenantAsync();
        Assert.Equal(200, (await api.Put("/costing/policy", Policy("last_purchase"))).Status);
        var item = await SeedProductAsync(api, stock: 0);
        var (supplier, _) = await SeedSupplierAsync(api);

        await BuyAsync(api, supplier, item, 10, 10);
        await BuyAsync(api, supplier, item, 10, 14);
        Assert.Equal(14m, await CostAsync(api, item));
        Assert.Equal(14m, (await ProductAsync(api, item))["lastPurchaseCost"].D());
        await AssertBooksMatchAsync(api, 280m);
        Assert.Equal(-40m, await BalanceAsync(api, "514"));   // إعادة تسعير أول 10 من 10 إلى 14
        await AssertLedgerMatchesItemAsync(api, item);
    }

    [Fact]
    public async Task Standard_cost_books_at_standard_and_the_item_card_follows()
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api, stock: 0);
        var current = await ProductAsync(api, item);
        Assert.Equal(200, (await api.Put($"/products/{item}", new
        {
            sku = current["sku"].S(), nameAr = current["nameAr"].S(), category = "C1", unit = "PCS", sellingPrice = 100, vatRate = 15, standardCost = 12,
        })).Status);
        Assert.Equal(200, (await api.Put("/costing/policy", Policy("standard"))).Status);
        var (supplier, _) = await SeedSupplierAsync(api);

        await BuyAsync(api, supplier, item, 10, 15);
        Assert.Equal(12m, await CostAsync(api, item));
        await AssertBooksMatchAsync(api, 120m);
        Assert.Equal(30m, await BalanceAsync(api, "513"));
        await AssertLedgerMatchesItemAsync(api, item);
    }

    [Fact]
    public async Task Purchase_return_leaves_at_the_running_average_and_the_price_gap_goes_to_variance()
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api, stock: 0);
        var (supplier, supplierAccount) = await SeedSupplierAsync(api);
        await BuyAsync(api, supplier, item, 10, 10);
        var second = await BuyAsync(api, supplier, item, 10, 20);
        Assert.Equal(15m, await CostAsync(api, item));

        var ret = await api.PostReturn(new { originalInvoiceId = second["id"].S(), returnReason = "تالف", lines = new[] { new { itemId = item, quantity = 4 } } });
        Assert.True(ret.Status is 200 or 201, ret.Body?.ToJsonString());
        Assert.Equal(16m, (await ProductAsync(api, item))["currentStock"].D());
        Assert.Equal(15m, await CostAsync(api, item));
        await AssertBooksMatchAsync(api, 240m);                // خرج 4 × 15
        Assert.Equal(-20m, await BalanceAsync(api, "514"));    // المورد دائن بـ 4 × 20، والفرق 20
        Assert.Equal(115m + 230m - 92m, await BalanceAsync(api, supplierAccount));
        var last = (await LedgerAsync(api, item)).Last()!;
        Assert.Equal("purchase_return", last["docType"].S());
        Assert.Equal(4m, last["quantityOut"].D());
        await AssertLedgerMatchesItemAsync(api, item);
    }

    [Fact]
    public async Task Editing_a_posted_purchase_reverses_its_cost_and_rebooks_the_new_price()
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api, stock: 0);
        var (supplier, _) = await SeedSupplierAsync(api);
        await BuyAsync(api, supplier, item, 10, 10);
        var second = await BuyAsync(api, supplier, item, 10, 20);
        Assert.Equal(15m, await CostAsync(api, item));

        var body = Purchase(supplier, new[] { new Line(item, 10, 30) });
        var upd = await api.Put(await api.InvoicePath(second["id"].S()), body);
        Assert.True(upd.Status == 200, upd.Body?.ToJsonString());
        Assert.Equal(20m, await CostAsync(api, item));        // (100 + 300) ÷ 20
        Assert.Equal(20m, (await ProductAsync(api, item))["currentStock"].D());
        await AssertBooksMatchAsync(api, 400m);
        await AssertLedgerMatchesItemAsync(api, item);
    }

    [Fact]
    public async Task Purchase_onto_negative_stock_takes_the_purchase_cost()
    {
        var api = await NewTenantAsync();
        Assert.Equal(200, (await api.Put("/costing/policy", new
        {
            method = "moving_average", recalculateOnNewPurchase = true, includeFreightAndCustoms = true, negativeInventoryPolicy = "allow_with_last_cost", standardCostVarianceAccountCode = "513",
        })).Status);
        var item = await SeedProductAsync(api, stock: 2, cost: 10);
        await SellAsync(api, item, 5);                         // الرصيد −3
        var (supplier, _) = await SeedSupplierAsync(api);
        await BuyAsync(api, supplier, item, 10, 12);
        Assert.Equal(7m, (await ProductAsync(api, item))["currentStock"].D());
        Assert.Equal(12m, await CostAsync(api, item));        // الرصيد السالب لا يدخل المتوسط
        await AssertBooksMatchAsync(api, 84m);
    }

    /// <summary>
    /// كل ما يغيّر قيمة الصنف بلا كمية يظهر في بطاقته سطر إعادة تقييم: التكاليف الإضافية واعتمادها وإلغاؤها، وتغيير طريقة التكلفة،
    /// وتعديل تسوية بتاريخ سابق وحذف أخرى. بعد كل خطوة: آخر قيمة في البطاقة = الرصيد × التكلفة = حساب المخزون.
    /// </summary>
    [Fact]
    public async Task Every_value_change_without_quantity_appears_in_the_item_card()
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api, stock: 0);
        var (supplier, _) = await SeedSupplierAsync(api);
        var (agent, _) = await SeedSupplierAsync(api, "مخلّص جمركي");
        var adjust = await api.Post("/stockmovements/adjust", new { itemId = item, type = "adjustment_in", quantity = 10, unitCost = 8,
            referenceNumber = "OPEN", date = DateTime.UtcNow.Date.AddDays(-2) });
        Assert.Equal(200, adjust.Status);
        var invoice = await BuyAsync(api, supplier, item, 10, 12);
        await SellAsync(api, item, 5);                         // متوسط 10، يبقى 15
        await AssertLedgerMatchesItemAsync(api, item);

        // سند تكاليف إضافية 20 على الفاتورة: نصيب ما بيع منها لتكلفة المبيعات، والباقي يرفع قيمة الصنف
        var types = (await api.Get("/purchase-expense-types")).Data!.AsArray().ToDictionary(t => t!["code"].S(), t => t!["id"].G());
        var voucher = await api.Post("/purchase-landed-costs", new
        {
            invoiceIds = new[] { invoice["id"].S() }, payee = "supplier", supplierId = agent, supplierInvoiceNumber = "CLR-9",
            lines = new[] { new { expenseTypeId = types["CUSTOMS_CLEARANCE"], amount = 20m, vatAmount = 0m, allocationMethod = (string?)null, manual = (object[]?)null } },
        });
        Assert.Equal(201, voucher.Status);
        Assert.Equal(200, (await api.Post($"/purchase-landed-costs/{voucher.Data!["id"].S()}/approve")).Status);
        await AssertLedgerMatchesItemAsync(api, item);
        Assert.Contains(await LedgerAsync(api, item), l => l!["docType"].S() == "revaluation" && l["quantityIn"].D() == 0 && l["quantityOut"].D() == 0);
        Assert.Equal(200, (await api.Post($"/purchase-landed-costs/{voucher.Data["id"].S()}/cancel")).Status);
        await AssertLedgerMatchesItemAsync(api, item);

        // تغيير الطريقة يعيد الاحتساب ويعيد تقييم الرصيد
        Assert.Equal(200, (await api.Put("/costing/policy", Policy("fifo"))).Status);
        await AssertLedgerMatchesItemAsync(api, item);
        Assert.Equal(200, (await api.Put("/costing/policy", Policy("moving_average"))).Status);
        await AssertLedgerMatchesItemAsync(api, item);

        // تعديل التسوية بتاريخ سابق يعيد تسعير الرصيد كله، وحذف تسوية لاحقة يعيده
        Assert.Equal(200, (await api.Put($"/stockmovements/{adjust.Data!["id"].S()}", new { itemId = item, type = "adjustment_in", quantity = 10, unitCost = 14,
            referenceNumber = "OPEN", date = DateTime.UtcNow.Date.AddDays(-2) })).Status);
        await AssertLedgerMatchesItemAsync(api, item);
        var extra = await api.Post("/stockmovements/adjust", new { itemId = item, type = "adjustment_in", quantity = 5, unitCost = 30, referenceNumber = "X" });
        Assert.Equal(200, extra.Status);
        Assert.Equal(200, (await api.Delete($"/stockmovements/{extra.Data!["id"].S()}")).Status);
        await AssertLedgerMatchesItemAsync(api, item);

        var p = await ProductAsync(api, item);
        await AssertBooksMatchAsync(api, Math.Round(p["currentStock"].D() * p["averageCost"].D(), 2));
    }

    [Fact]
    public async Task Changing_the_standard_cost_appears_in_the_item_card()
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api, stock: 10, cost: 40);
        var current = await ProductAsync(api, item);
        object Update(decimal standard) => new
        {
            sku = current["sku"].S(), nameAr = current["nameAr"].S(), category = "C1", unit = "PCS", sellingPrice = 100, vatRate = 15, standardCost = standard,
        };
        Assert.Equal(200, (await api.Put("/costing/policy", Policy("standard"))).Status);
        Assert.Equal(200, (await api.Put($"/products/{item}", Update(45))).Status);
        Assert.Equal(45m, await CostAsync(api, item));
        await AssertLedgerMatchesItemAsync(api, item);
        Assert.Equal(450m, (await LedgerAsync(api, item)).Last()!["runningStockValue"].D());
        await AssertBooksMatchAsync(api, 450m);
    }
}
