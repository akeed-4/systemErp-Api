using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// فواتير الصرف والاستلام المخزني: مستندان مخزنيان لا بيع ولا شراء. بلا ضريبة ولا عميل ولا مورد؛ الصرف بتكلفة المخزون الفعلية
/// على الحساب المقابل، والاستلام بسعر الشراء الصافي وقواعد الشراء في التكلفة؛ الترحيل مرة واحدة والإلغاء يعكس كل أثر.
/// </summary>
[Collection("api")]
public class InventoryVoucherTests : TestBase
{
    public InventoryVoucherTests(ErpFactory f) : base(f) { }

    /// <summary>صافي كل حساب في القيد: موجب مدين، سالب دائن.</summary>
    private static async Task<Dictionary<string, decimal>> EntryAsync(Client api, string entryId)
        => (await api.Get($"/journalentries/{entryId}")).Data!["lines"]!.AsArray()
            .GroupBy(l => l!["accountCode"].S()).ToDictionary(g => g.Key, g => g.Sum(l => l!["debit"].D() - l!["credit"].D()));

    private static async Task<JsonObject> ProductOfAsync(Client api, Guid item) => (await api.Get($"/products/{item}")).Data!.AsObject();

    private static async Task AssertBooksMatchStockAsync(Client api)
    {
        var items = (await api.Get("/reports/InventoryReconciliation")).Data!["rows"]!.AsArray().Single(r => r!["key"].S() == "items")!;
        Assert.True(items["difference"].D() == 0, items.ToJsonString());
        var (debit, credit) = Totals(await api.Get("/reports/TrialBalance"));
        Assert.Equal(debit, credit);
    }

    private static object Issue(Guid item, decimal qty, bool post = true, string? reason = null)
        => new { reason, post, lines = new[] { new { itemId = item, quantity = qty } } };

    private static object Receipt(Guid item, decimal qty, decimal price, decimal discount = 0, decimal documentDiscount = 0, bool post = true, string? reason = null)
        => new { reason, post, documentDiscount, lines = new[] { new { itemId = item, quantity = qty, unitPrice = price, discount } } };

    /// <summary>حسابا فروقات المخزون للموقع الرئيسي (كالجرد).</summary>
    private static async Task<(string Shortage, string Surplus)> MainSiteVarianceAsync(Client api)
    {
        var main = (await api.Get("/warehouses")).Data!["items"]!.AsArray().First(w => w!["isDefault"]!.GetValue<bool>())!;
        var settings = (await api.Get("/accounting-settings")).Data!["effectiveAccounts"]!;
        return (main["shortageAccountCode"]?.ToString() ?? settings["inventoryShortageAccountCode"].S(),
            main["surplusAccountCode"]?.ToString() ?? settings["inventorySurplusAccountCode"].S());
    }

    private static readonly string[] TaxAndPartyRoots = { "1131", "213", "217" };

    [Fact]
    public async Task Issue_takes_items_out_at_their_actual_cost_to_the_counter_account_without_tax_or_revenue()
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api, "ISS", stock: 10, cost: 40); // سعر البيع 100 وضريبته 15%: لا يظهران في الصرف

        var issued = await api.Post("/inventory-issues", Issue(item, 4));
        Assert.True(issued.Status == 200, issued.Body?.ToJsonString());
        var doc = issued.Data!;
        Assert.Equal("posted", doc["status"].S());
        Assert.StartsWith("IIS-", doc["voucherNumber"].S());
        Assert.Equal("532", doc["counterAccountCode"].S()); // الافتراضي: مصروفات الصرف المخزني
        Assert.Equal(160, doc["inventoryValue"].D());
        Assert.Equal(40, doc["lines"]![0]!["unitCost"].D());

        var entry = await EntryAsync(api, doc["journalEntryId"].S());
        Assert.Equal(160, entry["532"]);
        Assert.Equal(-160, entry[MainSiteInventory]);
        Assert.Equal(2, entry.Count); // لا إيراد ولا تكلفة مبيعات ولا ضريبة ولا عميل
        Assert.DoesNotContain(entry.Keys, k => TaxAndPartyRoots.Contains(k) || k.StartsWith("41") || k.StartsWith("112"));

        var product = await ProductOfAsync(api, item);
        Assert.Equal(6, product["currentStock"].D());
        Assert.Equal(40, product["averageCost"].D());
        await AssertBooksMatchStockAsync(api);

        // لا ترحيل مكرر: المرحّل لا يُرحَّل ولا يُعدَّل ولا يُحذف
        var id = doc["id"].S();
        Assert.Equal(409, (await api.Post($"/inventory-issues/{id}/post")).Status);
        Assert.Equal(409, (await api.Put($"/inventory-issues/{id}", Issue(item, 1))).Status);
        Assert.Equal(409, (await api.Delete($"/inventory-issues/{id}")).Status);
        Assert.Equal(6, (await ProductOfAsync(api, item))["currentStock"].D());

        // النوع جزء من المعرّف: لا يُقرأ من شاشة الاستلام
        Assert.Equal(404, (await api.Get($"/inventory-receipts/{id}")).Status);

        // الإلغاء بسبب إلزامي يعكس الحركة والقيد
        Assert.Equal(400, (await api.Post($"/inventory-issues/{id}/cancel", new { reason = "" })).Status);
        var cancelled = await api.Post($"/inventory-issues/{id}/cancel", new { reason = "صرف بالخطأ" });
        Assert.True(cancelled.Status == 200, cancelled.Body?.ToJsonString());
        Assert.Equal("cancelled", cancelled.Data!["status"].S());
        product = await ProductOfAsync(api, item);
        Assert.Equal(10, product["currentStock"].D());
        Assert.Equal(40, product["averageCost"].D());
        Assert.Equal(409, (await api.Post($"/inventory-issues/{id}/cancel", new { reason = "مرة ثانية" })).Status);
        await AssertBooksMatchStockAsync(api);
    }

    [Fact]
    public async Task Issue_beyond_available_stock_is_rejected_and_leaves_nothing_behind()
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api, "SHORT", stock: 3, cost: 10);

        Assert.Equal(409, (await api.Post("/inventory-issues", Issue(item, 5))).Status);
        Assert.Equal(0, (await api.Get("/inventory-issues")).Data!["totalCount"].D()); // المعاملة كلها أُلغيت
        Assert.Equal(3, (await ProductOfAsync(api, item))["currentStock"].D());

        // المسودة تُحفظ ويظهر فيها المتاح، والترحيل يرفضه
        var draft = await api.Post("/inventory-issues", Issue(item, 5, post: false));
        Assert.Equal(200, draft.Status);
        var loaded = await api.Get($"/inventory-issues/{draft.Data!["id"].S()}");
        Assert.Equal(3, loaded.Data!["lines"]![0]!["available"].D());
        Assert.Equal(409, (await api.Post($"/inventory-issues/{draft.Data["id"].S()}/post")).Status);
        Assert.Equal("draft", (await api.Get($"/inventory-issues/{draft.Data["id"].S()}")).Data!["status"].S());
    }

    [Fact]
    public async Task Receipt_enters_items_at_the_net_purchase_price_with_purchase_cost_rules_and_no_supplier_or_tax()
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api, "RCV", stock: 10, cost: 40);

        // 10 × 60 = 600 − خصم السطر 50 − خصم المستند 50 = 500 → 50 للوحدة (الضريبة 15% على الصنف لا تُحسب)
        var received = await api.Post("/inventory-receipts", Receipt(item, 10, 60, discount: 50, documentDiscount: 50));
        Assert.True(received.Status == 200, received.Body?.ToJsonString());
        var doc = received.Data!;
        Assert.StartsWith("IRC-", doc["voucherNumber"].S());
        Assert.Equal("424", doc["counterAccountCode"].S()); // الافتراضي: واردات مخزنية بلا فاتورة
        Assert.Equal(600, doc["subtotal"].D());
        Assert.Equal(100, doc["discountTotal"].D());
        Assert.Equal(500, doc["netTotal"].D());
        Assert.Equal(500, doc["inventoryValue"].D());

        var entry = await EntryAsync(api, doc["journalEntryId"].S());
        Assert.Equal(500, entry[MainSiteInventory]);
        Assert.Equal(-500, entry["424"]);
        Assert.Equal(2, entry.Count); // لا ضريبة مدخلات ولا مورد
        Assert.DoesNotContain(entry.Keys, k => TaxAndPartyRoots.Contains(k) || k.StartsWith("211"));

        // المتوسط المتحرك كالشراء: (10×40 + 10×50) ÷ 20، وآخر سعر شراء يُحدَّث
        var product = await ProductOfAsync(api, item);
        Assert.Equal(20, product["currentStock"].D());
        Assert.Equal(45, product["averageCost"].D());
        Assert.Equal(50, product["lastPurchaseCost"].D());
        await AssertBooksMatchStockAsync(api);

        // الإلغاء يخرج الكمية بتكلفة دخولها فيعود المتوسط
        var cancelled = await api.Post($"/inventory-receipts/{doc["id"].S()}/cancel", new { reason = "استلام مكرر" });
        Assert.True(cancelled.Status == 200, cancelled.Body?.ToJsonString());
        product = await ProductOfAsync(api, item);
        Assert.Equal(10, product["currentStock"].D());
        Assert.Equal(40, product["averageCost"].D());
        await AssertBooksMatchStockAsync(api);
    }

    [Fact]
    public async Task Receipt_with_cost_recalculation_off_keeps_the_item_cost_and_posts_the_difference_as_a_variance()
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api, "KEEP", stock: 10, cost: 40);
        Assert.Equal(200, (await api.Put("/costing/policy", new { method = "moving_average", recalculateOnNewPurchase = false, includeFreightAndCustoms = true,
            negativeInventoryPolicy = "prohibit", standardCostVarianceAccountCode = "513" })).Status);

        var received = await api.Post("/inventory-receipts", Receipt(item, 10, 50));
        Assert.True(received.Status == 200, received.Body?.ToJsonString());
        var entry = await EntryAsync(api, received.Data!["journalEntryId"].S());
        Assert.Equal(400, entry[MainSiteInventory]); // يدخل بالتكلفة الحالية 40
        Assert.Equal(100, entry["513"]);             // فرق السعر على حساب الانحرافات في السياسة
        Assert.Equal(-500, entry["424"]);
        Assert.Equal(40, (await ProductOfAsync(api, item))["averageCost"].D());
        await AssertBooksMatchStockAsync(api);
    }

    [Theory]
    [InlineData("internal_use", "532")]  // صرف داخلي: مصروفات الصرف المخزني
    [InlineData("project", "517")]       // صرف لمشروع: تكلفة مواد المشاريع
    [InlineData("shortage", null)]       // تسوية نقص: عجز المخزون للموقع
    public async Task Each_issue_reason_posts_to_its_mapped_account(string reason, string? expected)
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api, "R-" + reason, stock: 10, cost: 25);
        expected ??= (await MainSiteVarianceAsync(api)).Shortage;

        var issued = await api.Post("/inventory-issues", Issue(item, 2, reason: reason));
        Assert.True(issued.Status == 200, issued.Body?.ToJsonString());
        Assert.Equal(reason, issued.Data!["reason"].S());
        Assert.Equal(expected, issued.Data["counterAccountCode"].S());
        Assert.Equal(expected, issued.Data["lines"]![0]!["counterAccountCode"].S());
        var entry = await EntryAsync(api, issued.Data["journalEntryId"].S());
        Assert.Equal(50, entry[expected]);
        Assert.Equal(-50, entry[MainSiteInventory]);
        Assert.Equal(2, entry.Count);
        await AssertBooksMatchStockAsync(api);
    }

    [Theory]
    [InlineData("opening_balance", "33")]  // رصيد افتتاحي: الأرصدة الافتتاحية
    [InlineData("other_receipt", "424")]   // استلام بلا فاتورة
    [InlineData("surplus", null)]          // تسوية زيادة: زيادة المخزون للموقع
    public async Task Each_receipt_reason_posts_to_its_mapped_account(string reason, string? expected)
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api, "R-" + reason, stock: 0);
        expected ??= (await MainSiteVarianceAsync(api)).Surplus;

        var received = await api.Post("/inventory-receipts", Receipt(item, 4, 25, reason: reason));
        Assert.True(received.Status == 200, received.Body?.ToJsonString());
        Assert.Equal(expected, received.Data!["counterAccountCode"].S());
        var entry = await EntryAsync(api, received.Data["journalEntryId"].S());
        Assert.Equal(100, entry[MainSiteInventory]);
        Assert.Equal(-100, entry[expected]);
        Assert.Equal(2, entry.Count);
        await AssertBooksMatchStockAsync(api);
    }

    [Fact]
    public async Task Reason_must_belong_to_the_document_type_and_the_user_never_sends_the_account()
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api, "RSN", stock: 5, cost: 10);
        Assert.Equal(400, (await api.Post("/inventory-issues", Issue(item, 1, reason: "surplus"))).Status);
        Assert.Equal(400, (await api.Post("/inventory-receipts", Receipt(item, 1, 10, reason: "project"))).Status);
        Assert.Equal(400, (await api.Post("/inventory-issues", Issue(item, 1, reason: "nope"))).Status);

        // حساب مرسل من الواجهة يُتجاهل: الحساب من السبب وحده
        var sent = await api.Post("/inventory-issues", new { reason = "internal_use", counterAccountCode = "33", post = true, lines = new[] { new { itemId = item, quantity = 1 } } });
        Assert.True(sent.Status == 200, sent.Body?.ToJsonString());
        Assert.Equal("532", sent.Data!["counterAccountCode"].S());
    }

    [Fact]
    public async Task Changing_the_reason_mapping_affects_new_postings_only()
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api, "MAP", stock: 10, cost: 10);
        var before = await api.Post("/inventory-issues", Issue(item, 1));
        var draft = await api.Post("/inventory-issues", Issue(item, 1, post: false));
        Assert.Equal("532", before.Data!["counterAccountCode"].S());

        // المحاسب يربط الصرف الداخلي بمصروف آخر
        var saved = await api.Put("/accounting-settings", new { inventoryIssueAccountCode = "521" });
        Assert.True(saved.Status == 200, saved.Body?.ToJsonString());
        Assert.Equal("521", saved.Data!["effectiveAccounts"]!["inventoryIssueAccountCode"].S());

        // المرحّل قبله يبقى بحسابه وقيده
        var old = await api.Get($"/inventory-issues/{before.Data["id"].S()}");
        Assert.Equal("532", old.Data!["counterAccountCode"].S());
        Assert.Equal(10, (await EntryAsync(api, before.Data["journalEntryId"].S()))["532"]);
        // المسودة المحفوظة قبل التعديل تُرحَّل بالحساب الحالي
        var posted = await api.Post($"/inventory-issues/{draft.Data!["id"].S()}/post");
        Assert.Equal("521", posted.Data!["counterAccountCode"].S());
        Assert.Equal(10, (await EntryAsync(api, posted.Data["journalEntryId"].S()))["521"]);

        // إلغاء المرحّل القديم يعكس قيده نفسه (532) لا الحساب الجديد
        Assert.Equal(200, (await api.Post($"/inventory-issues/{before.Data["id"].S()}/cancel", new { reason = "تصحيح" })).Status);
        Assert.Equal(0m, (await api.Get("/accounts/ByCode/532")).Data!["balance"].D());
        await AssertBooksMatchStockAsync(api);
    }

    [Fact]
    public async Task Accounts_are_hidden_from_operational_users_and_shown_to_the_authorized_accountant()
    {
        var owner = await NewTenantAsync();
        var item = await SeedProductAsync(owner, "HID", stock: 5, cost: 10);
        var issued = await owner.Post("/inventory-issues", Issue(item, 1));
        var id = issued.Data!["id"].S();

        var email = $"u{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await owner.Post("/users", new { name = "store", email, phone = "1", password = "Passw0rd!", role = "sales_rep" })).Status);
        var clerk = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");

        var seen = await clerk.Get($"/inventory-issues/{id}");
        Assert.True(seen.Status == 200, seen.Body?.ToJsonString());
        Assert.Equal("internal_use", seen.Data!["reason"].S());
        Assert.Null(seen.Data["counterAccountCode"]);
        Assert.Null(seen.Data["lines"]![0]!["counterAccountCode"]);
        var defaults = (await clerk.Get("/inventory-issues/defaults")).Data!;
        Assert.False(defaults["canViewAccounts"]!.GetValue<bool>());
        Assert.All(defaults["reasons"]!.AsArray(), r => Assert.Null(r!["accountCode"]));

        var accountant = (await owner.Get("/inventory-issues/defaults")).Data!;
        Assert.True(accountant["canViewAccounts"]!.GetValue<bool>());
        Assert.Equal("532", accountant["reasons"]!.AsArray().Single(r => r!["reason"].S() == "internal_use")!["accountCode"].S());
        Assert.Equal("532", (await owner.Get($"/inventory-issues/{id}")).Data!["counterAccountCode"].S());
    }

    [Fact]
    public async Task Periodic_inventory_posts_through_the_purchases_account_and_leaves_inventory_untouched()
    {
        var api = await NewTenantAsync();
        var saved = await api.Put("/accounting-settings", new { inventoryMethod = "periodic" });
        Assert.True(saved.Status == 200, saved.Body?.ToJsonString());
        var purchases = saved.Data!["purchasesAccountCode"].S(); // هيّأه اختيار الدوري
        var item = await SeedProductAsync(api, "PER", stock: 10, cost: 40);

        var issued = await api.Post("/inventory-issues", Issue(item, 2));
        Assert.True(issued.Status == 200, issued.Body?.ToJsonString());
        var issueEntry = await EntryAsync(api, issued.Data!["journalEntryId"].S());
        Assert.Equal(80, issueEntry["532"]);
        Assert.Equal(-80, issueEntry[purchases]);
        Assert.False(issueEntry.ContainsKey(MainSiteInventory));

        var received = await api.Post("/inventory-receipts", Receipt(item, 5, 30));
        Assert.True(received.Status == 200, received.Body?.ToJsonString());
        var receiptEntry = await EntryAsync(api, received.Data!["journalEntryId"].S());
        Assert.Equal(150, receiptEntry[purchases]);
        Assert.Equal(-150, receiptEntry["424"]);
        Assert.False(receiptEntry.ContainsKey(MainSiteInventory));
        Assert.Equal(13, (await ProductOfAsync(api, item))["currentStock"].D());
    }

    [Fact]
    public async Task Landed_costs_can_be_capitalized_on_a_receipt_which_then_cannot_be_cancelled_until_the_voucher_is()
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api, "LND", stock: 0);
        var received = await api.Post("/inventory-receipts", Receipt(item, 10, 10));
        Assert.True(received.Status == 200, received.Body?.ToJsonString());
        var receiptId = received.Data!["id"].S();
        Assert.Equal(10, (await ProductOfAsync(api, item))["averageCost"].D());

        var types = (await api.Get("/purchase-expense-types")).Data!.AsArray().ToDictionary(t => t!["code"].S(), t => t!["id"].G());
        var (agent, _) = await SeedSupplierAsync(api, "شركة شحن");
        var voucher = await api.Post("/purchase-landed-costs", new
        {
            inventoryReceiptIds = new[] { receiptId }, payee = "supplier", supplierId = agent,
            lines = new[] { new { expenseTypeId = types["CUSTOMS_CLEARANCE"], amount = 20, vatAmount = 0 } },
        });
        Assert.True(voucher.Status == 201, voucher.Body?.ToJsonString());
        Assert.Equal("inventory_receipt", voucher.Data!["invoices"]![0]!["sourceType"].S());
        Assert.Equal(12, voucher.Data["items"]![0]!["unitCostAfter"].D());

        var approved = await api.Post($"/purchase-landed-costs/{voucher.Data["id"].S()}/approve");
        Assert.True(approved.Status == 200, approved.Body?.ToJsonString());
        Assert.Equal(12, (await ProductOfAsync(api, item))["averageCost"].D());
        await AssertBooksMatchStockAsync(api);

        // السند المعتمد رفع تكلفة حركة الاستلام: الإلغاء بعد إلغاء السند
        Assert.True((await api.Get($"/inventory-receipts/{receiptId}")).Data!["hasLandedCosts"]!.GetValue<bool>());
        Assert.Equal(409, (await api.Post($"/inventory-receipts/{receiptId}/cancel", new { reason = "خطأ" })).Status);
        Assert.Equal(200, (await api.Post($"/purchase-landed-costs/{voucher.Data["id"].S()}/cancel")).Status);
        Assert.Equal(10, (await ProductOfAsync(api, item))["averageCost"].D());
        Assert.Equal(200, (await api.Post($"/inventory-receipts/{receiptId}/cancel", new { reason = "خطأ" })).Status);
        Assert.Equal(0, (await ProductOfAsync(api, item))["currentStock"].D());
        await AssertBooksMatchStockAsync(api);
    }
}
