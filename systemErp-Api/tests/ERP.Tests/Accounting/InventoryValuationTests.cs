using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// حساب المخزون يساوي قيمة الأصناف (الرصيد × التكلفة) بعد كل عملية: البيع على دفعات، آخر سعر شراء، تغيير طريقة التكلفة،
/// التكلفة المعيارية، وتعديل تسوية بتاريخ سابق. كل فرق قيمة يُرحَّل بقيد، فلا تظهر فروق في مطابقة المخزون.
/// </summary>
[Collection("api")]
public class InventoryValuationTests : TestBase
{
    public InventoryValuationTests(ErpFactory f) : base(f) { }

    private static object Sale(Guid product, decimal qty)
        => new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", items = new[] { new { itemId = product, quantity = qty, unitPrice = 100, vatRate = 15 } } };

    private static object Policy(string method)
        => new { method, recalculateOnNewPurchase = true, includeFreightAndCustoms = false, negativeInventoryPolicy = "prohibit", standardCostVarianceAccountCode = "513" };

    private static async Task<decimal> BalanceAsync(Client api, string code) => (await api.Get($"/accounts/ByCode/{code}")).Data!["balance"].D();

    /// <summary>المخزون في الدفاتر = قيمة الأصناف تماماً (لا هامش تقريب).</summary>
    private static async Task<JsonNode> AssertReconciledAsync(Client api, decimal expectedValue)
    {
        var items = (await api.Get("/reports/InventoryReconciliation")).Data!["rows"]!.AsArray().Single(r => r!["key"].S() == "items")!;
        Assert.True(items["difference"].D() == 0, items.ToJsonString());
        Assert.Equal(expectedValue, items["ledgerValue"].D());
        Assert.Equal(expectedValue, await BalanceAsync(api, "1141"));
        return items;
    }

    [Fact]
    public async Task Selling_stock_piece_by_piece_leaves_no_rounding_residue()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 3, cost: 3.3333m); // قيمة 10.00 (3 × 3.3333 = 9.9999)
        await AssertReconciledAsync(api, 10m);

        var costs = new List<decimal>();
        for (var i = 0; i < 3; i++)
        {
            var sale = await api.Post("/invoices", Sale(product, 1));
            Assert.Equal(201, sale.Status);
            costs.Add(sale.Data!["totalCost"].D());
        }
        Assert.Equal(10m, costs.Sum()); // آخر قطعة تحمل الباقي (3.33 + 3.33 + 3.34)
        await AssertReconciledAsync(api, 0m);
    }

    [Fact]
    public async Task Last_purchase_costing_books_the_repricing_of_existing_stock()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 10, cost: 40);
        Assert.Equal(200, (await api.Put("/costing/policy", Policy("last_purchase"))).Status);
        Assert.Equal(200, (await api.Post("/stockmovements/adjust", new { itemId = product, type = "adjustment_in", quantity = 5, unitCost = 70, referenceNumber = "X" })).Status);
        await AssertReconciledAsync(api, 15 * 70m); // الرصيد القديم أُعيد تسعيره بآخر سعر ودخل فرقه في القيد

        var (supplier, _) = await SeedSupplierAsync(api);
        var purchase = await api.Post("/invoices", new { kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier,
            items = new[] { new { itemId = product, quantity = 5, unitPrice = 30, vatRate = 15 } } });
        Assert.Equal(201, purchase.Status); // شراء بأقل من التكلفة: الرصيد كله ينخفض إلى 30، والفرق إلى فروق الأسعار
        await AssertReconciledAsync(api, 20 * 30m);
        Assert.Equal(1050m + 150m - 600m, await BalanceAsync(api, "514"));
    }

    [Fact]
    public async Task Switching_costing_method_revalues_stock_with_an_entry()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 10, cost: 40);
        Assert.Equal(200, (await api.Post("/stockmovements/adjust", new { itemId = product, type = "adjustment_in", quantity = 10, unitCost = 60, referenceNumber = "L2" })).Status);
        Assert.Equal(600m, (await api.Post("/invoices", Sale(product, 12))).Data!["totalCost"].D()); // متوسط 50
        await AssertReconciledAsync(api, 400m);

        // FIFO: الباقي 8 من الدفعة الثانية بـ 60 = 480، والفرق 80 بقيد إعادة تقييم
        Assert.Equal(200, (await api.Put("/costing/policy", Policy("fifo"))).Status);
        Assert.Equal(60m, (await api.Get($"/products/{product}")).Data!["averageCost"].D());
        await AssertReconciledAsync(api, 480m);

        var again = await api.Post("/costing/RecalculateAll");
        Assert.Equal(0m, again.Data!["revaluationAmount"].D()); // لا فرق جديد
        Assert.Null(again.Data["revaluationEntryNumber"]);
    }

    [Fact]
    public async Task Standard_costing_books_purchases_at_standard_and_revalues_when_the_standard_changes()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 0);
        var current = (await api.Get($"/products/{product}")).Data!.AsObject();
        object Update(decimal standard) => new
        {
            sku = current["sku"].S(), nameAr = current["nameAr"].S(), category = "C1", unit = "PCS", sellingPrice = 100, vatRate = 15, standardCost = standard,
        };
        Assert.Equal(200, (await api.Put($"/products/{product}", Update(45))).Status);
        Assert.Equal(200, (await api.Put("/costing/policy", Policy("standard"))).Status);

        var (supplier, _) = await SeedSupplierAsync(api);
        Assert.Equal(201, (await api.Post("/invoices", new { kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier,
            items = new[] { new { itemId = product, quantity = 10, unitPrice = 50, vatRate = 15 } } })).Status);
        await AssertReconciledAsync(api, 450m);
        Assert.Equal(50m, await BalanceAsync(api, "513")); // فرق التكلفة المعيارية على حسابه في السياسة

        Assert.Equal(200, (await api.Put($"/products/{product}", Update(48))).Status);
        await AssertReconciledAsync(api, 480m);
        Assert.Equal(20m, await BalanceAsync(api, "513")); // 50 − 30 إعادة تقييم

        Assert.Equal(192m, (await api.Post("/invoices", Sale(product, 4))).Data!["totalCost"].D());
        await AssertReconciledAsync(api, 288m);
    }

    [Fact]
    public async Task Editing_a_back_dated_adjustment_rebooks_its_effect_on_the_remaining_stock()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 0);
        var adjust = await api.Post("/stockmovements/adjust", new { itemId = product, type = "adjustment_in", quantity = 10, unitCost = 40,
            referenceNumber = "OPEN", date = DateTime.UtcNow.Date.AddDays(-2) });
        Assert.Equal(200, adjust.Status);
        Assert.Equal(200m, (await api.Post("/invoices", Sale(product, 5))).Data!["totalCost"].D());
        await AssertReconciledAsync(api, 200m);

        // تكلفة الإضافة تُصحَّح إلى 60: الباقي 5 × 60 = 300، وتكلفة المبيعات المرحّلة لا تتغير
        var id = adjust.Data!["id"].S();
        Assert.Equal(200, (await api.Put($"/stockmovements/{id}", new { itemId = product, type = "adjustment_in", quantity = 10, unitCost = 60,
            referenceNumber = "OPEN", date = DateTime.UtcNow.Date.AddDays(-2) })).Status);
        await AssertReconciledAsync(api, 300m);

        // حذفها غير ممكن (يجعل الرصيد سالباً)، وإضافة ثم حذف تسوية أخرى يعيد القيمة كما كانت
        var extra = await api.Post("/stockmovements/adjust", new { itemId = product, type = "adjustment_in", quantity = 5, unitCost = 80, referenceNumber = "X" });
        await AssertReconciledAsync(api, 700m); // 10 × 70
        Assert.Equal(200, (await api.Delete($"/stockmovements/{extra.Data!["id"].S()}")).Status);
        await AssertReconciledAsync(api, 300m);
    }
}
