using ERP.Core.Models.Accounting;
using ERP.Core.Models.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ERP.Tests.Infrastructure;
using static ERP.Tests.AccountingSettingsTests;

namespace ERP.Tests;

/// <summary>
/// الجرد الدوري: المشتريات على حسابها بسعر المستند، البيع بلا تكلفة، التسويات وإعادة التقييم بلا قيود، بضاعة آخر المدة من جرد دوري شامل
/// مع عكسها إلى أول المدة، تحويل رصيد المخزون مرة واحدة عند بدء الدوري، رفض الحركات السابقة لقيمة مثبتة، ومنع تكرار قيد المستند.
/// </summary>
[Collection("api")]
public class PeriodicInventoryTests : TestBase
{
    public PeriodicInventoryTests(ErpFactory f) : base(f) { }

    private static readonly DateTime Today = DateTime.UtcNow.Date;

    private static object Purchase(Guid supplier, Guid product, decimal qty, decimal price, string? inventoryAccountCode = null)
        => new { kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "cash", partyId = supplier, inventoryAccountCode,
            items = new[] { new { itemId = product, quantity = qty, unitPrice = price, vatRate = 15 } } };

    private static async Task<decimal> BalanceAsync(Client api, string code) => (await api.Get($"/accounts/ByCode/{code}")).Data!["balance"].D();

    private static async Task PeriodicAccountsAsync(Client api)
    {
        foreach (var code in new[] { "516", "517", "518", "519" }) await AccountAsync(api, code, "expense", "51");
    }

    private static object PeriodicSettings(DateTime? from = null) => new
    {
        inventoryMethod = "periodic", purchasesAccountCode = "516", openingInventoryAccountCode = "517", endingInventoryAccountCode = "518",
        purchaseReturnsAccountCode = "519", inventoryMethodEffectiveFrom = from,
    };

    /// <summary>منشأة دورية منذ البداية (قبل أول قيد).</summary>
    private async Task<Client> PeriodicTenantAsync()
    {
        var api = await NewTenantAsync();

        var (supplier, _) = await SeedSupplierAsync(api); // فاتورة الشراء الضريبية تتطلب مورداً
        await PeriodicAccountsAsync(api);
        Assert.Equal(200, (await api.Put("/accounting-settings", PeriodicSettings())).Status);
        return api;
    }

    private static async Task<Res> ApproveCountAsync(Client api, object count)
    {
        var created = await api.Post("/inventorycounts", count);
        Assert.Equal(201, created.Status);
        var approval = await api.Post($"/inventorycounts/{created.Data!["id"].S()}/submit");
        return await api.Post($"/inventorycountapprovals/{approval.Data!["id"].S()}/approve", new { });
    }

    private static object ItemCount(Guid product, decimal counted, string countType = "periodic", Guid? warehouseId = null)
        => new { scope = "items", countType, warehouseId, countDate = DateTime.UtcNow, lines = new[] { new { itemId = product, countedQuantity = counted } } };

    [Fact]
    public async Task Sales_carry_no_cost_and_purchases_go_to_purchases_at_document_price()
    {
        var api = await PeriodicTenantAsync();

        var (supplier, _) = await SeedSupplierAsync(api); // فاتورة الشراء الضريبية تتطلب مورداً
        var product = await SeedProductAsync(api, stock: 10, cost: 40); // أرصدة افتتاحية مقابل 33: على بضاعة أول المدة
        Assert.Equal(400m, await BalanceAsync(api, "517"));
        Assert.Equal(0m, await BalanceAsync(api, "1141"));

        var sale = await api.PostInvoice(Sale(product, 2));
        Assert.Equal(201, sale.Status);
        var saleLines = await JournalLinesAsync(api, sale.Data!["journalEntryId"].S());
        Assert.Equal(200m, Credit(saleLines, "411"));
        Assert.DoesNotContain(saleLines, l => l!["accountCode"].S() is "511" or "1141");

        var purchase = await api.PostInvoice(Purchase(supplier, product, 5, 60));
        Assert.Equal(201, purchase.Status);
        var purchaseLines = await JournalLinesAsync(api, purchase.Data!["journalEntryId"].S());
        Assert.Equal(300m, Debit(purchaseLines, "516"));
        Assert.Equal(45m, Debit(purchaseLines, "1131"));
        Assert.DoesNotContain(purchaseLines, l => l!["accountCode"].S() is "1141" or "514");

        // المرتجع بسعر مستنده (لا بالتكلفة الحالية) على حساب مردودات المشتريات
        var ret = await api.PostReturn(new { originalInvoiceId = purchase.Data["id"].S(), returnReason = "x", lines = new[] { new { itemId = product, quantity = 2 } } });
        Assert.True(ret.Success, ret.Body?.ToJsonString());
        var retLines = await JournalLinesAsync(api, ret.Data!["journalEntryId"].S());
        Assert.Equal(120m, Credit(retLines, "519"));
        Assert.DoesNotContain(retLines, l => l!["accountCode"].S() is "1141" or "514");
        Assert.Equal(0m, await BalanceAsync(api, "1141"));
    }

    [Fact]
    public async Task Adjustments_and_revaluation_post_nothing_and_counts_post_no_variance()
    {
        var api = await PeriodicTenantAsync();
        var product = await SeedProductAsync(api, stock: 10, cost: 40);
        var entries = (await api.Get("/journalentries")).Data!["totalCount"].D();

        var adjust = await api.Post("/stockmovements/adjust", new { itemId = product, type = "adjustment_in", quantity = 3, unitCost = 40, referenceNumber = "A" });
        Assert.Equal(200, adjust.Status);
        Assert.Null(adjust.Data!["journalEntryId"]);
        Assert.Equal(200, (await api.Put("/costing/policy", new { method = "last_purchase", recalculateOnNewPurchase = true, includeFreightAndCustoms = false,
            negativeInventoryPolicy = "prohibit", standardCostVarianceAccountCode = "513" })).Status);
        Assert.Equal(200, (await api.Post("/stockmovements/adjust", new { itemId = product, type = "adjustment_in", quantity = 1, unitCost = 70, referenceNumber = "B" })).Status);

        var count = await ApproveCountAsync(api, ItemCount(product, 12, countType: "full"));
        Assert.Equal(200, count.Status);
        Assert.Null(count.Data!["journalEntryId"]);
        Assert.Equal(entries, (await api.Get("/journalentries")).Data!["totalCount"].D());
        Assert.Equal(0m, await BalanceAsync(api, "1141"));
        Assert.Equal(0m, await BalanceAsync(api, "513"));
    }

    [Fact]
    public async Task Periodic_count_records_ending_inventory_once_reverses_it_tomorrow_and_closes_earlier_dates()
    {
        var api = await PeriodicTenantAsync();
        var product = await SeedProductAsync(api, stock: 10, cost: 40);
        Assert.Equal(201, (await api.PostInvoice(Sale(product, 2))).Status);

        var approved = await ApproveCountAsync(api, ItemCount(product, 7)); // عجز قطعة: 7 × 40
        Assert.Equal(200, approved.Status);
        var ending = await JournalLinesAsync(api, approved.Data!["journalEntryId"].S());
        Assert.Equal(280m, Debit(ending, "1141"));
        Assert.Equal(280m, Credit(ending, "518"));
        Assert.Equal(0m, await BalanceAsync(api, "1141"));       // عُكس غداً إلى أول المدة
        Assert.Equal(400m + 280m, await BalanceAsync(api, "517"));
        Assert.Equal(-280m, await BalanceAsync(api, "518")); // دائن على حساب مصروف (طبيعته مدينة)

        Assert.Equal(409, (await api.PostInvoice(Sale(product, 1))).Status);                       // ما قبل القيمة المثبتة مقفل
        Assert.Equal(409, (await api.Post("/stockmovements/adjust", new { itemId = product, type = "adjustment_in", quantity = 1, unitCost = 40,
            referenceNumber = "LATE", date = Today })).Status);
        Assert.Equal(409, (await ApproveCountAsync(api, ItemCount(product, 6))).Status);           // قيد آخر مدة ثانٍ لليوم نفسه
    }

    [Fact]
    public async Task Periodic_count_must_cover_all_warehouses()
    {
        var api = await PeriodicTenantAsync();
        var product = await SeedProductAsync(api);
        var warehouse = (await api.Get("/warehouses")).Data!["items"]!.AsArray().First()!["id"].G();
        Assert.Equal(400, (await ApproveCountAsync(api, ItemCount(product, 9, warehouseId: warehouse))).Status);
    }

    [Fact]
    public async Task Switching_from_perpetual_transfers_the_inventory_balance_once_and_closes_the_previous_method()
    {
        var api = await NewTenantAsync();
        await PeriodicAccountsAsync(api);
        var thisYear = new DateTime(Today.Year, 1, 1);
        var product = await SeedProductAsync(api, stock: 0);
        Assert.Equal(200, (await api.Post("/stockmovements/adjust", new { itemId = product, type = "adjustment_in", quantity = 10, unitCost = 40,
            referenceNumber = "OPEN", counterAccountCode = "33", date = thisYear.AddMonths(-6) })).Status); // مستمر: على 1141
        Assert.Equal(400m, await BalanceAsync(api, "1141"));
        Assert.Equal(200, (await api.Put("/accounting-settings", PeriodicSettings(thisYear))).Status);

        var sale = await api.PostInvoice(Sale(product, 2));
        Assert.Equal(201, sale.Status);
        Assert.DoesNotContain(await JournalLinesAsync(api, sale.Data!["journalEntryId"].S()), l => l!["accountCode"].S() is "511" or "1141");
        Assert.Equal(0m, await BalanceAsync(api, "1141"));   // نُقل الرصيد إلى بضاعة أول المدة
        Assert.Equal(400m, await BalanceAsync(api, "517"));

        Assert.Equal(201, (await api.PostInvoice(Sale(product, 1))).Status);
        Assert.Equal(400m, await BalanceAsync(api, "517"));  // لا تحويل ثانٍ
        Assert.Equal(409, (await api.Post("/stockmovements/adjust", new { itemId = product, type = "adjustment_in", quantity = 1, unitCost = 40,
            referenceNumber = "LATE", date = thisYear.AddDays(-1) })).Status); // ما قبل بدء الدوري أُقفل
    }

    [Fact]
    public async Task Manual_inventory_account_is_rejected_on_periodic_stock_documents()
    {
        var api = await PeriodicTenantAsync();

        var (supplier, _) = await SeedSupplierAsync(api); // فاتورة الشراء الضريبية تتطلب مورداً
        await AccountAsync(api, "1146", "asset", "114");
        var product = await SeedProductAsync(api, stock: 0);
        Assert.Equal(400, (await api.PostInvoice(Purchase(supplier, product, 1, 50, inventoryAccountCode: "1146"))).Status);
    }

    [Fact]
    public async Task Only_one_active_entry_is_allowed_per_source_document()
    {
        var api = await NewTenantAsync();
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>();
        var source = Guid.NewGuid();
        JournalEntry Entry(string number, JournalEntryStatus status) => new()
        {
            TenantId = api.TenantId, EntryNumber = number, Date = Today, Description = "x", SourceType = "sales", SourceReferenceId = source, Status = status,
        };

        db.AddRange(Entry("DUP-1", JournalEntryStatus.Reversed), Entry("DUP-2", JournalEntryStatus.Posted)); // معكوس + فعّال: مسموح
        await db.SaveChangesAsync();
        db.Add(Entry("DUP-3", JournalEntryStatus.Posted));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
