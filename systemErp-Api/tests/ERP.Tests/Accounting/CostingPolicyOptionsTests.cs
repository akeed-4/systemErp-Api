using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// خيارات سياسة التكلفة: إيقاف تحديث التكلفة عند الشراء (الفرق لحساب الانحرافات)، وإيقاف تضمين التكاليف الإضافية
/// في التكلفة (السند يُحمَّل مصروفاً)، وإعادة الاحتساب على دفعات.
/// </summary>
[Collection("api")]
public class CostingPolicyOptionsTests : TestBase
{
    public CostingPolicyOptionsTests(ErpFactory f) : base(f) { }

    private static object Policy(bool recalculateOnPurchase = true, bool includeFreight = true, string method = "moving_average")
        => new { method, recalculateOnNewPurchase = recalculateOnPurchase, includeFreightAndCustoms = includeFreight, negativeInventoryPolicy = "prohibit", standardCostVarianceAccountCode = "513" };

    private static async Task<Guid> ProductAsync(Client api, string sku)
    {
        await api.Post("/productcategories", new { code = "C1", nameAr = "تصنيف" });
        await api.Post("/unitsofmeasure", new { code = "PCS", nameAr = "قطعة", isBaseUnit = true, conversionFactor = 1 });
        var p = await api.Post("/products", new { sku, nameAr = "صنف " + sku, category = "C1", unit = "PCS", sellingPrice = 100, vatRate = 15 });
        Assert.Equal(201, p.Status);
        return p.Data!["id"].G();
    }

    private static async Task<(string Id, string EntryId)> PurchaseAsync(Client api, Guid supplier, Guid item, decimal qty, decimal price)
    {
        var res = await api.Post("/purchase-invoices", new
        {
            kind = "purchase", invoiceType = "simplified", paymentMethod = "credit", partyId = supplier,
            items = new[] { new { itemId = item, quantity = qty, unitPrice = price, vatRate = 15 } },
        });
        Assert.Equal(201, res.Status);
        return (res.Data!["id"].S(), res.Data["journalEntryId"].S());
    }

    private static async Task<Dictionary<string, decimal>> EntryAsync(Client api, string entryId)
        => (await api.Get($"/journalentries/{entryId}")).Data!["lines"]!.AsArray()
            .GroupBy(l => l!["accountCode"].S()).ToDictionary(g => g.Key, g => g.Sum(l => l!["debit"].D() - l!["credit"].D()));

    private static async Task<decimal> AverageCostAsync(Client api, Guid product) => (await api.Get($"/products/{product}")).Data!["averageCost"].D();

    [Fact]
    public async Task New_tenant_includes_freight_in_cost_by_default()
    {
        var api = await NewTenantAsync();
        var policy = (await api.Get("/costing/policy")).Data!;
        Assert.True((bool)policy["includeFreightAndCustoms"]!);
        Assert.True((bool)policy["recalculateOnNewPurchase"]!);
    }

    [Fact]
    public async Task Purchase_keeps_item_cost_when_auto_update_is_off_and_posts_the_difference_to_the_variance_account()
    {
        var api = await NewTenantAsync();
        var item = await ProductAsync(api, "A");
        var (supplier, supplierAccount) = await SeedSupplierAsync(api);
        await PurchaseAsync(api, supplier, item, 10, 10);
        Assert.Equal(10, await AverageCostAsync(api, item));

        Assert.Equal(200, (await api.Put("/costing/policy", Policy(recalculateOnPurchase: false))).Status);
        var (_, entryId) = await PurchaseAsync(api, supplier, item, 10, 14);
        Assert.Equal(10, await AverageCostAsync(api, item));

        // المخزون بتكلفة الصنف الحالية 100، والفرق 40 لحساب الانحرافات المحدد، والمورد بالفاتورة كاملة
        var entry = await EntryAsync(api, entryId);
        Assert.Equal(100, entry[MainSiteInventory]); Assert.Equal(40, entry["513"]); Assert.Equal(-161, entry[supplierAccount]);

        // إعادة الاحتساب تحترم الخيار: لا تغيير ولا قيد إعادة تقييم
        var re = (await api.Post("/costing/RecalculateAll")).Data!;
        Assert.Equal(0, re["revaluationAmount"].D());
        Assert.Equal(10, await AverageCostAsync(api, item));

        // تشغيله من جديد يعيد الشراء إلى المتوسط المتحرك
        Assert.Equal(200, (await api.Put("/costing/policy", Policy())).Status);
        await PurchaseAsync(api, supplier, item, 20, 13);
        Assert.Equal(11.5m, await AverageCostAsync(api, item)); // (20×10 + 20×13) ÷ 40
    }

    [Fact]
    public async Task Landed_costs_are_expensed_without_touching_item_cost_when_freight_is_excluded()
    {
        var api = await NewTenantAsync();
        var item = await ProductAsync(api, "A");
        var (supplier, _) = await SeedSupplierAsync(api, "مورد البضاعة");
        var (agent, agentAccount) = await SeedSupplierAsync(api, "مخلّص جمركي");
        var (invoice, _) = await PurchaseAsync(api, supplier, item, 10, 10);
        Assert.Equal(200, (await api.Put("/costing/policy", Policy(includeFreight: false))).Status);

        var types = (await api.Get("/purchase-expense-types")).Data!.AsArray().ToDictionary(t => t!["code"].S(), t => t!["id"].G());
        var created = await api.Post("/purchase-landed-costs", new
        {
            invoiceIds = new[] { invoice }, payee = "supplier", supplierId = agent, supplierInvoiceNumber = "CLR-1",
            lines = new[] { new { expenseTypeId = types["CUSTOMS_CLEARANCE"], amount = 10m, vatAmount = 1m, allocationMethod = (string?)null, manual = (object[]?)null } },
        });
        Assert.Equal(201, created.Status);
        var id = created.Data!["id"].S();

        var approved = await api.Post($"/purchase-landed-costs/{id}/approve");
        Assert.Equal(200, approved.Status);
        Assert.Equal(10, await AverageCostAsync(api, item));
        var entry = await EntryAsync(api, approved.Data!["journalEntryId"].S());
        Assert.False(entry.ContainsKey(MainSiteInventory));
        Assert.Equal(10, entry["511"]); Assert.Equal(1, entry["1131"]); Assert.Equal(-11, entry[agentAccount]);

        // الإلغاء يعكس المصروف ولا يمس التكلفة
        var cancelled = await api.Post($"/purchase-landed-costs/{id}/cancel");
        Assert.Equal(200, cancelled.Status);
        var reverse = await EntryAsync(api, cancelled.Data!["cancelJournalEntryId"].S());
        Assert.Equal(-10, reverse["511"]); Assert.Equal(11, reverse[agentAccount]);
        Assert.Equal(10, await AverageCostAsync(api, item));
    }

    [Fact]
    public async Task Recalculation_updates_every_item()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var items = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var item = await ProductAsync(api, "R" + i);
            await PurchaseAsync(api, supplier, item, 2, 10 + i);
            await PurchaseAsync(api, supplier, item, 2, 20 + i);
            items.Add(item);
        }
        Assert.Equal(200, (await api.Put("/costing/policy", Policy(method: "last_purchase"))).Status); // يعيد الاحتساب
        for (var i = 0; i < 3; i++) Assert.Equal(20 + i, await AverageCostAsync(api, items[i]));
        var re = (await api.Post("/costing/RecalculateAll")).Data!;
        Assert.Equal(3, (int)re["productsUpdated"]!);
    }
}
