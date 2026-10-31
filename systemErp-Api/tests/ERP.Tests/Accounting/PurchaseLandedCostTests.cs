using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// التكاليف الإضافية للمشتريات العامة: تحميل الشحن والجمارك والتخليص على تكلفة الأصناف بضريبة مقطوعة، وتوزيعها،
/// وقيدها (المخزون بتغيّر قيمته والباقي تكلفة مبيعات/فرق سعر)، وإلغاؤها، وعزلها عن مشتريات السيارات.
/// </summary>
[Collection("api")]
public class PurchaseLandedCostTests : TestBase
{
    public PurchaseLandedCostTests(ErpFactory f) : base(f) { }

    private static async Task<Guid> ProductAsync(Client api, string sku, decimal? weightKg = null)
    {
        await api.Post("/productcategories", new { code = "C1", nameAr = "تصنيف" });
        await api.Post("/unitsofmeasure", new { code = "PCS", nameAr = "قطعة", isBaseUnit = true, conversionFactor = 1 });
        var p = await api.Post("/products", new { sku, nameAr = "صنف " + sku, category = "C1", unit = "PCS", sellingPrice = 100, vatRate = 15, weightKg });
        Assert.Equal(201, p.Status);
        return p.Data!["id"].G();
    }

    private static async Task<(string Id, Res Res)> PurchaseAsync(Client api, Guid supplier, params (Guid Item, decimal Qty, decimal Price)[] lines)
    {
        var res = await api.Post("/purchase-invoices", new
        {
            kind = "purchase", invoiceType = "simplified", paymentMethod = "credit", partyId = supplier,
            items = lines.Select(l => new { itemId = l.Item, quantity = l.Qty, unitPrice = l.Price, vatRate = 15 }).ToArray(),
        });
        Assert.Equal(201, res.Status);
        return (res.Data!["id"].S(), res);
    }

    private static async Task<Dictionary<string, Guid>> TypesAsync(Client api)
        => (await api.Get("/purchase-expense-types")).Data!.AsArray().ToDictionary(t => t!["code"].S(), t => t!["id"].G());

    /// <summary>صافي كل حساب في القيد: موجب مدين، سالب دائن.</summary>
    private static async Task<Dictionary<string, decimal>> EntryAsync(Client api, string entryId)
        => (await api.Get($"/journalentries/{entryId}")).Data!["lines"]!.AsArray()
            .GroupBy(l => l!["accountCode"].S()).ToDictionary(g => g.Key, g => g.Sum(l => l!["debit"].D() - l!["credit"].D()));

    private static async Task<decimal> AverageCostAsync(Client api, Guid product) => (await api.Get($"/products/{product}")).Data!["averageCost"].D();

    private static object Line(Guid type, decimal amount, decimal vat = 0, string? method = null, object[]? manual = null)
        => new { expenseTypeId = type, amount, vatAmount = vat, allocationMethod = method, manual };

    [Fact]
    public async Task Clearance_with_a_fixed_tax_is_capitalized_and_its_tax_goes_to_input_vat()
    {
        var api = await NewTenantAsync();
        var item = await ProductAsync(api, "A");
        var (supplier, supplierAccount) = await SeedSupplierAsync(api, "مورد البضاعة");
        var (agent, agentAccount) = await SeedSupplierAsync(api, "مخلّص جمركي");
        var (invoice, purchase) = await PurchaseAsync(api, supplier, (item, 10, 10));

        // قيد الشراء كما هو: مخزون 100 + ضريبة 15 / المورد 115
        var buy = await EntryAsync(api, purchase.Data!["journalEntryId"].S());
        Assert.Equal(100, buy["1141"]); Assert.Equal(15, buy["1131"]); Assert.Equal(-115, buy[supplierAccount]);

        var types = await TypesAsync(api);
        // ضريبة مقطوعة 1 على 10 (ليست 15%)
        var created = await api.Post("/purchase-landed-costs", new
        {
            invoiceIds = new[] { invoice }, payee = "supplier", supplierId = agent, supplierInvoiceNumber = "CLR-77",
            lines = new[] { Line(types["CUSTOMS_CLEARANCE"], 10, 1) },
        });
        Assert.Equal(201, created.Status);
        Assert.Equal("draft", created.Data!["status"].S());
        Assert.Equal(11, created.Data["grandTotal"].D());
        Assert.Equal(11, created.Data["items"]![0]!["unitCostAfter"].D());
        Assert.Equal(10, await AverageCostAsync(api, item)); // المسودة لا تمس التكلفة

        var approved = await api.Post($"/purchase-landed-costs/{created.Data["id"].S()}/approve");
        Assert.Equal(200, approved.Status);
        Assert.Equal("approved", approved.Data!["status"].S());
        var entry = await EntryAsync(api, approved.Data["journalEntryId"].S());
        Assert.Equal(10, entry["1141"]);
        Assert.Equal(1, entry["1131"]);
        Assert.Equal(-11, entry[agentAccount]);
        Assert.Equal(3, entry.Count);
        Assert.Equal(11, await AverageCostAsync(api, item));

        // فاتورة عليها سند معتمد لا تُعدَّل
        Assert.Equal(409, (await api.Put($"/purchase-invoices/{invoice}", new
        {
            kind = "purchase", invoiceType = "simplified", paymentMethod = "credit", partyId = supplier,
            items = new[] { new { itemId = item, quantity = 10, unitPrice = 12, vatRate = 15 } },
        })).Status);
        var (debit, credit) = Totals(await api.Get("/reports/TrialBalance"));
        Assert.Equal(debit, credit);
    }

    [Fact]
    public async Task Share_of_already_sold_goods_goes_to_cogs_and_cancelling_mirrors_it()
    {
        var api = await NewTenantAsync();
        var item = await ProductAsync(api, "A");
        var (supplier, _) = await SeedSupplierAsync(api);
        var (invoice, _) = await PurchaseAsync(api, supplier, (item, 10, 10));
        Assert.Equal(201, (await api.Post("/sales-invoices", new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash",
            items = new[] { new { itemId = item, quantity = 4, unitPrice = 100, vatRate = 15 } },
        })).Status);

        var types = await TypesAsync(api);
        var created = await api.Post("/purchase-landed-costs", new
        {
            invoiceIds = new[] { invoice }, payee = "payment_account", paymentAccountCode = "1112", lines = new[] { Line(types["FREIGHT"], 10) },
        });
        Assert.Equal(201, created.Status);
        var id = created.Data!["id"].S();
        var approved = (await api.Post($"/purchase-landed-costs/{id}/approve")).Data!;
        var entry = await EntryAsync(api, approved["journalEntryId"].S());
        Assert.Equal(6, entry["1141"]); // الباقي في المخزون 6 وحدات
        Assert.Equal(4, entry["511"]);  // حصة المبيع على تكلفة المبيعات
        Assert.Equal(-10, entry["1112"]); // دائن البنك
        Assert.Equal(6, approved["inventoryAmount"].D());
        Assert.Equal(4, approved["expensedAmount"].D());
        Assert.Equal(11, await AverageCostAsync(api, item));

        var cancelled = await api.Post($"/purchase-landed-costs/{id}/cancel");
        Assert.Equal(200, cancelled.Status);
        Assert.Equal("cancelled", cancelled.Data!["status"].S());
        var reverse = await EntryAsync(api, cancelled.Data["cancelJournalEntryId"].S());
        Assert.Equal(-6, reverse["1141"]); Assert.Equal(-4, reverse["511"]); Assert.Equal(10, reverse["1112"]);
        Assert.Equal(10, await AverageCostAsync(api, item));
        Assert.Equal(409, (await api.Post($"/purchase-landed-costs/{id}/cancel")).Status);
        Assert.Equal(409, (await api.Delete($"/purchase-landed-costs/{id}")).Status); // الملغى بعد اعتماده يبقى في السجل
    }

    [Fact]
    public async Task Costs_are_allocated_by_value_quantity_weight_or_manually()
    {
        var api = await NewTenantAsync();
        var heavy = await ProductAsync(api, "H", weightKg: 5);
        var light = await ProductAsync(api, "L", weightKg: 1);
        var bare = await ProductAsync(api, "N");
        var (supplier, _) = await SeedSupplierAsync(api);
        // القيمة 300 لكل صنف، والكمية 2 مقابل 6، والوزن 10 كجم مقابل 6
        var (invoice, purchase) = await PurchaseAsync(api, supplier, (heavy, 2, 150), (light, 6, 50));
        var types = await TypesAsync(api);

        async Task<Dictionary<Guid, decimal>> SharesAsync(string method, object[]? manual = null)
        {
            var preview = await api.Post("/purchase-landed-costs/preview", new
            {
                invoiceIds = new[] { invoice }, payee = "accrued", lines = new[] { Line(types["FREIGHT"], 80, 0, method, manual) },
            });
            Assert.True(preview.Status == 200, preview.Body?.ToJsonString());
            return preview.Data!["items"]!.AsArray().ToDictionary(i => i!["itemId"].G(), i => i!["capitalized"].D());
        }

        var byValue = await SharesAsync("by_value");
        Assert.Equal(40, byValue[heavy]); Assert.Equal(40, byValue[light]);
        var byQuantity = await SharesAsync("by_quantity");
        Assert.Equal(20, byQuantity[heavy]); Assert.Equal(60, byQuantity[light]);
        var byWeight = await SharesAsync("by_weight");
        Assert.Equal(50, byWeight[heavy]); Assert.Equal(30, byWeight[light]);

        var lines = purchase.Data!["items"]!.AsArray();
        var heavyLine = lines.First(l => l!["itemId"].G() == heavy)!["id"].G();
        var lightLine = lines.First(l => l!["itemId"].G() == light)!["id"].G();
        var manual = await SharesAsync("manual", new object[] { new { invoiceItemId = heavyLine, amount = 70 }, new { invoiceItemId = lightLine, amount = 10 } });
        Assert.Equal(70, manual[heavy]); Assert.Equal(10, manual[light]);
        Assert.Equal(400, (await api.Post("/purchase-landed-costs/preview", new
        {
            invoiceIds = new[] { invoice }, payee = "accrued",
            lines = new[] { Line(types["FREIGHT"], 80, 0, "manual", new object[] { new { invoiceItemId = heavyLine, amount = 70 } }) },
        })).Status); // المجموع لا يساوي المبلغ

        // صنف بلا وزن: التوزيع بالوزن مرفوض
        var (other, _) = await PurchaseAsync(api, supplier, (heavy, 1, 10), (bare, 1, 10));
        Assert.Equal(400, (await api.Post("/purchase-landed-costs/preview", new
        {
            invoiceIds = new[] { other }, payee = "accrued", lines = new[] { Line(types["FREIGHT"], 10, 0, "by_weight") },
        })).Status);
    }

    [Fact]
    public async Task Returned_quantity_carries_no_share_of_the_cost()
    {
        var api = await NewTenantAsync();
        var item = await ProductAsync(api, "A");
        var (supplier, _) = await SeedSupplierAsync(api);
        var (invoice, _) = await PurchaseAsync(api, supplier, (item, 10, 10));
        Assert.Equal(200, (await api.Post("/purchase-returns/from-invoice", new
        {
            originalInvoiceId = invoice, returnReason = "تالف", lines = new[] { new { itemId = item, quantity = 2 } },
        })).Status);

        var types = await TypesAsync(api);
        var created = await api.Post("/purchase-landed-costs", new
        {
            invoiceIds = new[] { invoice }, payee = "accrued", lines = new[] { Line(types["FREIGHT"], 8, 0, "by_quantity") },
        });
        Assert.Equal(8, created.Data!["items"]![0]!["netQuantity"].D());
        Assert.Equal(11, created.Data["items"]![0]!["unitCostAfter"].D());
        var approved = (await api.Post($"/purchase-landed-costs/{created.Data["id"].S()}/approve")).Data!;
        var entry = await EntryAsync(api, approved["journalEntryId"].S());
        Assert.Equal(8, entry["1141"]);   // الصافي 8 وحدات بزيادة 1 للوحدة
        Assert.Equal(-8, entry["212"]);   // مستحقات حتى تصل فاتورة الشاحن
        Assert.Equal(11, await AverageCostAsync(api, item));
    }

    [Fact]
    public async Task Customs_duty_takes_no_tax_and_import_vat_goes_wholly_to_input_vat()
    {
        var api = await NewTenantAsync();
        var item = await ProductAsync(api, "A");
        var (supplier, _) = await SeedSupplierAsync(api);
        var (invoice, _) = await PurchaseAsync(api, supplier, (item, 10, 10));
        var types = await TypesAsync(api);

        // الرسوم الجمركية خارج نطاق الضريبة، وضريبة الاستيراد مبلغها كله ضريبة: لا تُسجَّل على أي منهما ضريبة
        Assert.Equal(400, (await api.Post("/purchase-landed-costs/preview", new
        {
            invoiceIds = new[] { invoice }, payee = "accrued", lines = new[] { Line(types["CUSTOMS_DUTY"], 20, 3) },
        })).Status);
        Assert.Equal(400, (await api.Post("/purchase-landed-costs/preview", new
        {
            invoiceIds = new[] { invoice }, payee = "accrued", lines = new[] { Line(types["IMPORT_VAT"], 15, 1) },
        })).Status);

        var created = await api.Post("/purchase-landed-costs", new
        {
            invoiceIds = new[] { invoice }, payee = "payment_account", paymentAccountCode = "1112",
            lines = new[] { Line(types["CUSTOMS_DUTY"], 20), Line(types["IMPORT_VAT"], 15) },
        });
        Assert.Equal(201, created.Status);
        Assert.Equal(20, created.Data!["baseCostTotal"].D());
        Assert.Equal(15, created.Data["baseInputVat"].D());
        var approved = (await api.Post($"/purchase-landed-costs/{created.Data["id"].S()}/approve")).Data!;
        var entry = await EntryAsync(api, approved["journalEntryId"].S());
        Assert.Equal(20, entry["1141"]); Assert.Equal(15, entry["1131"]); Assert.Equal(-35, entry["1112"]);
        Assert.Equal(12, await AverageCostAsync(api, item)); // 10 + 20/10
    }

    [Fact]
    public async Task Under_last_purchase_cost_an_older_invoice_cost_goes_to_price_variance()
    {
        var api = await NewTenantAsync();
        Assert.Equal(200, (await api.Put("/costing/policy", new
        {
            method = "last_purchase", recalculateOnNewPurchase = true, includeFreightAndCustoms = false, negativeInventoryPolicy = "prohibit", standardCostVarianceAccountCode = "513",
        })).Status);
        var item = await ProductAsync(api, "A");
        var (supplier, _) = await SeedSupplierAsync(api);
        var (older, _) = await PurchaseAsync(api, supplier, (item, 10, 10));
        await PurchaseAsync(api, supplier, (item, 5, 12));
        Assert.Equal(12, await AverageCostAsync(api, item));

        var types = await TypesAsync(api);
        var created = await api.Post("/purchase-landed-costs", new
        {
            invoiceIds = new[] { older }, payee = "accrued", lines = new[] { Line(types["FREIGHT"], 10) },
        });
        var approved = (await api.Post($"/purchase-landed-costs/{created.Data!["id"].S()}/approve")).Data!;
        var entry = await EntryAsync(api, approved["journalEntryId"].S());
        Assert.Equal(10, entry["514"]); // لا تتغيّر تكلفة آخر شراء، فالتكلفة كلها فرق سعر
        Assert.False(entry.ContainsKey("1141"));
        Assert.Equal("514", approved["expensedAccountCode"].S());
        Assert.Equal(12, await AverageCostAsync(api, item));
    }

    [Fact]
    public async Task Only_posted_general_purchase_invoices_can_be_linked()
    {
        var api = await NewTenantAsync();
        var item = await ProductAsync(api, "A");
        var (supplier, _) = await SeedSupplierAsync(api);
        var types = await TypesAsync(api);
        var draft = await api.Post("/purchase-invoices", new
        {
            kind = "purchase", invoiceType = "simplified", paymentMethod = "credit", partyId = supplier, status = "draft",
            items = new[] { new { itemId = item, quantity = 1, unitPrice = 10, vatRate = 15 } },
        });
        await PurchaseAsync(api, supplier, (item, 1, 10));
        var sale = await api.Post("/sales-invoices", new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash", items = new[] { new { itemId = item, quantity = 1, unitPrice = 10, vatRate = 15 } },
        });
        Assert.Equal(201, sale.Status);
        foreach (var id in new[] { draft.Data!["id"].S(), sale.Data!["id"].S(), Guid.NewGuid().ToString() })
            Assert.Equal(400, (await api.Post("/purchase-landed-costs/preview", new
            {
                invoiceIds = new[] { id }, payee = "accrued", lines = new[] { Line(types["FREIGHT"], 10) },
            })).Status);
        Assert.Equal(400, (await api.Post("/purchase-landed-costs/preview", new
        {
            invoiceIds = Array.Empty<string>(), payee = "accrued", lines = new[] { Line(types["FREIGHT"], 10) },
        })).Status);
    }
}
