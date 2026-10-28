using ERP.Tests.Infrastructure;
using static ERP.Tests.AccountingSettingsTests;

namespace ERP.Tests;

/// <summary>
/// حساب مخزون لكل مستودع: المستند على حساب مستودعه بعد الحساب اليدوي وقبل حساب الإعدادات، والتحويل بين حسابين مختلفين بقيد
/// بمتوسط التكلفة الموحّد، وإعادة التقييم وبضاعة آخر المدة موزّعتان على المستودعات بكمياتها، والمطابقة تشمل حسابات المستودعات.
/// </summary>
[Collection("api")]
public class WarehouseInventoryAccountTests : TestBase
{
    public WarehouseInventoryAccountTests(ErpFactory f) : base(f) { }

    private static async Task<Guid> WarehouseAsync(Client api, string code, string? inventoryAccountCode)
    {
        var w = await api.Post("/warehouses", new { code, nameAr = "مستودع " + code, nameEn = code, location = "الرياض", status = "active", inventoryAccountCode });
        Assert.Equal(201, w.Status);
        return w.Data!["id"].G();
    }

    private static async Task<Guid> DefaultWarehouseAsync(Client api)
        => (await api.Get("/warehouses")).Data!["items"]!.AsArray().First(w => w!["isDefault"]!.GetValue<bool>())!["id"].G();

    private static object Purchase(Guid supplier, Guid item, decimal qty, decimal price, Guid warehouse, string? inventoryAccountCode = null) => new
    {
        kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "cash", partyId = supplier, warehouseId = warehouse, inventoryAccountCode,
        items = new[] { new { itemId = item, quantity = qty, unitPrice = price, vatRate = 15 } },
    };

    private static object SaleFrom(Guid item, decimal qty, Guid warehouse) => new
    {
        kind = "sales", invoiceType = "simplified", paymentMethod = "cash", warehouseId = warehouse,
        items = new[] { new { itemId = item, quantity = qty, unitPrice = 100, vatRate = 15 } },
    };

    private static async Task<decimal> BalanceAsync(Client api, string code) => (await api.Get($"/accounts/ByCode/{code}")).Data!["balance"].D();

    private static async Task AssertReconciledAsync(Client api)
    {
        var items = (await api.Get("/reports/InventoryReconciliation")).Data!["rows"]!.AsArray().Single(r => r!["key"].S() == "items")!;
        Assert.True(items["difference"].D() == 0, items.ToJsonString());
    }

    /// <summary>منشأة بمستودع افتراضي بلا حساب (حساب الإعدادات 1141) ومستودع فرعي بحساب 1145.</summary>
    private async Task<(Client Api, Guid Main, Guid Branch, Guid Item)> SetupAsync()
    {
        var api = await NewTenantAsync();

        var (supplier, _) = await SeedSupplierAsync(api); // فاتورة الشراء الضريبية تتطلب مورداً
        await AccountAsync(api, "1145", "asset", "114");
        await AccountAsync(api, "1146", "asset", "114");
        var main = await DefaultWarehouseAsync(api);
        var branch = await WarehouseAsync(api, "BR", "1145");
        var item = await SeedProductAsync(api, stock: 0);
        return (api, main, branch, item);
    }

    [Fact]
    public async Task Documents_post_to_their_warehouse_account_after_the_manual_account()
    {
        var (api, main, branch, item) = await SetupAsync();

        var (supplier, _) = await SeedSupplierAsync(api); // فاتورة الشراء الضريبية تتطلب مورداً
        var toBranch = await api.PostInvoice(Purchase(supplier, item, 10, 40, branch));
        Assert.Equal(400m, Debit(await JournalLinesAsync(api, toBranch.Data!["journalEntryId"].S()), "1145"));
        var toMain = await api.PostInvoice(Purchase(supplier, item, 5, 40, main));
        Assert.Equal(200m, Debit(await JournalLinesAsync(api, toMain.Data!["journalEntryId"].S()), "1141"));

        var sale = await api.PostInvoice(SaleFrom(item, 2, branch));
        var saleLines = await JournalLinesAsync(api, sale.Data!["journalEntryId"].S());
        Assert.Equal(80m, Credit(saleLines, "1145"));
        Assert.DoesNotContain(saleLines, l => l!["accountCode"].S() == "1141");

        var manual = await api.PostInvoice(Purchase(supplier, item, 1, 40, branch, inventoryAccountCode: "1146"));
        var manualLines = await JournalLinesAsync(api, manual.Data!["journalEntryId"].S());
        Assert.Equal(40m, Debit(manualLines, "1146"));
        Assert.DoesNotContain(manualLines, l => l!["accountCode"].S() == "1145");
        await AssertReconciledAsync(api);
    }

    [Fact]
    public async Task Transfers_move_value_between_different_accounts_at_average_cost_only()
    {
        var (api, main, branch, item) = await SetupAsync();

        var (supplier, _) = await SeedSupplierAsync(api); // فاتورة الشراء الضريبية تتطلب مورداً
        var other = await WarehouseAsync(api, "OT", null); // بلا حساب: حساب الإعدادات كالرئيسي
        Assert.Equal(201, (await api.PostInvoice(Purchase(supplier, item, 10, 40, main))).Status);

        Assert.Equal(200, (await api.Post("/stocktransfers", new { fromWarehouseId = main, toWarehouseId = branch, items = new[] { new { itemId = item, quantity = 4 } } })).Status);
        Assert.Equal(240m, await BalanceAsync(api, "1141"));
        Assert.Equal(160m, await BalanceAsync(api, "1145"));

        var entries = (await api.Get("/journalentries")).Data!["totalCount"].D();
        Assert.Equal(200, (await api.Post("/stocktransfers", new { fromWarehouseId = main, toWarehouseId = other, items = new[] { new { itemId = item, quantity = 1 } } })).Status);
        Assert.Equal(entries, (await api.Get("/journalentries")).Data!["totalCount"].D()); // الحساب نفسه: لا قيد
        Assert.Equal(240m, await BalanceAsync(api, "1141"));
        await AssertReconciledAsync(api);
    }

    [Fact]
    public async Task Revaluation_is_split_by_the_quantities_that_were_repriced()
    {
        var (api, main, branch, item) = await SetupAsync();

        var (supplier, _) = await SeedSupplierAsync(api); // فاتورة الشراء الضريبية تتطلب مورداً
        Assert.Equal(201, (await api.PostInvoice(Purchase(supplier, item, 6, 40, main))).Status);
        Assert.Equal(201, (await api.PostInvoice(Purchase(supplier, item, 4, 40, branch))).Status);
        Assert.Equal(200, (await api.Put("/costing/policy", new { method = "last_purchase", recalculateOnNewPurchase = true, includeFreightAndCustoms = false,
            negativeInventoryPolicy = "prohibit", standardCostVarianceAccountCode = "513" })).Status);

        // إضافة قطعة بـ 70 للرئيسي تعيد تسعير العشر السابقة (+30 لكل قطعة): 6 في الرئيسي و4 في الفرع
        Assert.Equal(200, (await api.Post("/stockmovements/adjust", new { itemId = item, warehouseId = main, type = "adjustment_in", quantity = 1, unitCost = 70, referenceNumber = "X" })).Status);
        Assert.Equal(7 * 70m, await BalanceAsync(api, "1141"));
        Assert.Equal(4 * 70m, await BalanceAsync(api, "1145"));
        await AssertReconciledAsync(api);
    }

    [Fact]
    public async Task Count_differences_post_to_the_counted_warehouse_account()
    {
        var (api, _, branch, item) = await SetupAsync();

        var (supplier, _) = await SeedSupplierAsync(api); // فاتورة الشراء الضريبية تتطلب مورداً
        Assert.Equal(201, (await api.PostInvoice(Purchase(supplier, item, 10, 40, branch))).Status);
        var created = await api.Post("/inventorycounts", new { scope = "items", countType = "full", warehouseId = branch, countDate = DateTime.UtcNow,
            lines = new[] { new { itemId = item, countedQuantity = 8m } } });
        var approval = await api.Post($"/inventorycounts/{created.Data!["id"].S()}/submit");
        var approved = await api.Post($"/inventorycountapprovals/{approval.Data!["id"].S()}/approve", new { });
        var lines = await JournalLinesAsync(api, approved.Data!["journalEntryId"].S());
        Assert.Equal(80m, Credit(lines, "1145"));
        Assert.DoesNotContain(lines, l => l!["accountCode"].S() == "1141");
    }

    [Fact]
    public async Task Periodic_ending_inventory_is_split_by_warehouse_account()
    {
        var api = await NewTenantAsync();

        var (supplier, _) = await SeedSupplierAsync(api); // فاتورة الشراء الضريبية تتطلب مورداً
        await AccountAsync(api, "1145", "asset", "114");
        foreach (var code in new[] { "516", "517", "518" }) await AccountAsync(api, code, "expense", "51");
        Assert.Equal(200, (await api.Put("/accounting-settings", new { inventoryMethod = "periodic", purchasesAccountCode = "516",
            openingInventoryAccountCode = "517", endingInventoryAccountCode = "518" })).Status);
        var main = await DefaultWarehouseAsync(api);
        var branch = await WarehouseAsync(api, "BR", "1145");
        var item = await SeedProductAsync(api, stock: 0);
        Assert.Equal(201, (await api.PostInvoice(Purchase(supplier, item, 6, 40, main))).Status);
        Assert.Equal(201, (await api.PostInvoice(Purchase(supplier, item, 4, 40, branch))).Status);

        var created = await api.Post("/inventorycounts", new { scope = "items", countType = "periodic", countDate = DateTime.UtcNow,
            lines = new[] { new { itemId = item, countedQuantity = 6m } } }); // الجرد بلا مستودع يعدّ المستودع الافتراضي: بلا فرق
        var approval = await api.Post($"/inventorycounts/{created.Data!["id"].S()}/submit");
        var approved = await api.Post($"/inventorycountapprovals/{approval.Data!["id"].S()}/approve", new { });
        Assert.Equal(200, approved.Status);
        var ending = await JournalLinesAsync(api, approved.Data!["journalEntryId"].S());
        Assert.Equal(240m, Debit(ending, "1141"));
        Assert.Equal(160m, Debit(ending, "1145"));
        Assert.Equal(400m, Credit(ending, "518"));
    }

    [Fact]
    public async Task Warehouse_account_is_validated_kept_when_omitted_and_locked_after_movements()
    {
        var (api, main, branch, item) = await SetupAsync();

        var (supplier, _) = await SeedSupplierAsync(api); // فاتورة الشراء الضريبية تتطلب مورداً
        Assert.Equal(400, (await api.Post("/warehouses", new { code = "C1", nameAr = "ص", nameEn = "C1", location = "x", status = "active", inventoryAccountCode = "1111" })).Status);
        Assert.Equal(400, (await api.Post("/warehouses", new { code = "C2", nameAr = "ص", nameEn = "C2", location = "x", status = "active", inventoryAccountCode = "1131" })).Status);
        Assert.Equal(400, (await api.Post("/warehouses", new { code = "C3", nameAr = "ص", nameEn = "C3", location = "x", status = "active", inventoryAccountCode = "1142" })).Status);
        Assert.Equal(400, (await api.Put("/accounting-settings", new { inputVatAccountCode = "1145" })).Status); // حساب مستودع

        // تعديل المستودع دون الحقل لا يمسح حسابه
        var renamed = await api.Put($"/warehouses/{branch}", new { code = "BR", nameAr = "الفرع", nameEn = "Branch", location = "جدة", status = "active" });
        Assert.Equal(200, renamed.Status);
        Assert.Equal("1145", renamed.Data!["inventoryAccountCode"].S());

        Assert.Equal(201, (await api.PostInvoice(Purchase(supplier, item, 1, 40, branch))).Status);
        Assert.Equal(409, (await api.Put($"/warehouses/{branch}", new { code = "BR", nameAr = "الفرع", nameEn = "Branch", location = "جدة", status = "active",
            inventoryAccountCode = "1146" })).Status);
        Assert.Equal(409, (await api.Put($"/warehouses/{branch}", new { code = "BR", nameAr = "الفرع", nameEn = "Branch", location = "جدة", status = "active",
            inventoryAccountCode = "" })).Status);
    }
}
