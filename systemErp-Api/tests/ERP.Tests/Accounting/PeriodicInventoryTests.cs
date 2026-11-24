using ERP.Core.Models.Accounting;
using ERP.Core.Models.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;
using static ERP.Tests.AccountingSettingsTests;

namespace ERP.Tests;

/// <summary>
/// الجرد الدوري: المشتريات على حسابها بسعر المستند، البيع بلا تكلفة، التسويات وإعادة التقييم بلا قيود، والأرصدة الافتتاحية على المخزون أصلاً.
/// اعتماد جرد دوري شامل يرحّل قيداً موحداً بتاريخ الجرد (إلغاء الرصيد الدفتري إلى أول المدة وإثبات الفعلي مقابل آخر المدة) فيبقى المخزون
/// بقيمة الجرد في الميزانية؛ ويُرفض مع حركات لاحقة لتاريخه. لا تحويل للرصيد عند بدء الدوري، ولا احتساب مزدوج مع أكثر من جرد ولا مع
/// قيود النموذج القديم (ومعالجتها لكل منشأة بمعاينة). إقفال السنة يتطلب جرد آخر المدة أو تجاوزاً صريحاً بسبب.
/// </summary>
[Collection("api")]
public class PeriodicInventoryTests : TestBase
{
    public PeriodicInventoryTests(ErpFactory f) : base(f) { }

    private static readonly DateTime Today = SaudiTime.Today;

    private sealed record PeriodicAccounts(string Purchases, string Opening, string Ending);

    private static object Purchase(Guid supplier, Guid product, decimal qty, decimal price, string? inventoryAccountCode = null)
        => new { kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "cash", partyId = supplier, inventoryAccountCode,
            items = new[] { new { itemId = product, quantity = qty, unitPrice = price, vatRate = 15 } } };

    private static async Task<decimal> BalanceAsync(Client api, string code) => (await api.Get($"/accounts/ByCode/{code}")).Data!["balance"].D();

    /// <summary>رصيد من سطور القيود (ميزان المراجعة): يشمل القيود المُدرجة مباشرة في الاختبار.</summary>
    private static async Task<decimal> LinesBalanceAsync(Client api, string code)
        => (await api.Get("/reports/TrialBalance")).Data!.AsArray()
            .Where(r => r!["accountCode"].S() == code).Sum(r => r!["closingDebit"].D() - r!["closingCredit"].D());

    private static async Task<PeriodicAccounts> AccountsOfAsync(Client api)
    {
        var s = (await api.Get("/accounting-settings")).Data!;
        return new(s["purchasesAccountCode"].S(), s["openingInventoryAccountCode"].S(), s["endingInventoryAccountCode"].S());
    }

    /// <summary>منشأة دورية منذ البداية (قبل أول قيد): اختيار الدوري يهيّئ حساباته تلقائياً (بلا أرقام ثابتة).</summary>
    private async Task<(Client Api, PeriodicAccounts A)> PeriodicTenantAsync()
    {
        var api = await NewTenantAsync();
        await SeedSupplierAsync(api); // فاتورة الشراء الضريبية تتطلب مورداً
        var saved = await api.Put("/accounting-settings", new { inventoryMethod = "periodic" });
        Assert.True(saved.Status == 200, saved.Body?.ToJsonString());
        return (api, await AccountsOfAsync(api));
    }

    private static async Task<string> SubmitCountAsync(Client api, object count)
    {
        var created = await api.Post("/inventorycounts", count);
        Assert.True(created.Status == 201, created.Body?.ToJsonString());
        var submitted = await api.Post($"/inventorycounts/{created.Data!["id"].S()}/submit");
        Assert.True(submitted.Status is 200 or 201, $"{submitted.Status}: {submitted.Body?.ToJsonString()}");
        return submitted.Data!["id"].S();
    }

    /// <summary>نجاح مع نص الاستجابة عند الفشل (رسالة السرفر).</summary>
    private static void Ok(Res r) => Assert.True(r.Status == 200, $"{r.Status}: {r.Body?.ToJsonString()}");

    private static Task<Res> ApproveAsync(Client api, string approvalId) => api.Post($"/inventorycountapprovals/{approvalId}/approve", new { });

    private static async Task<Res> ApproveCountAsync(Client api, object count) => await ApproveAsync(api, await SubmitCountAsync(api, count));

    private static object ItemCount(Guid product, decimal counted, string countType = "periodic", Guid? warehouseId = null, DateTime? date = null)
        => new { scope = "items", countType, warehouseId, countDate = date ?? Today, lines = new[] { new { itemId = product, countedQuantity = counted } } };

    /// <summary>رصيد افتتاحي بتاريخ محدد مقابل الأرصدة الافتتاحية (33).</summary>
    private static async Task OpeningStockAsync(Client api, Guid product, decimal qty, decimal cost, DateTime date, Guid? warehouseId = null)
    {
        var r = await api.Post("/stockmovements/adjust", new { itemId = product, type = "adjustment_in", quantity = qty, unitCost = cost,
            referenceNumber = "OPEN", counterAccountCode = "33", date, warehouseId });
        Assert.True(r.Status == 200, r.Body?.ToJsonString());
    }

    [Fact]
    public async Task Sales_carry_no_cost_purchases_go_to_purchases_and_opening_balances_stay_an_inventory_asset()
    {
        var (api, a) = await PeriodicTenantAsync();

        var (supplier, _) = await SeedSupplierAsync(api);
        var product = await SeedProductAsync(api, stock: 10, cost: 40); // أرصدة افتتاحية مقابل 33: أصل على المخزون لا مصروف أول المدة
        Assert.Equal(400m, await BalanceAsync(api, MainSiteInventory));
        Assert.Equal(0m, await BalanceAsync(api, a.Opening));

        var sale = await api.PostInvoice(Sale(product, 2));
        Assert.Equal(201, sale.Status);
        var saleLines = await JournalLinesAsync(api, sale.Data!["journalEntryId"].S());
        Assert.Equal(200m, Credit(saleLines, "411"));
        Assert.DoesNotContain(saleLines, l => l!["accountCode"].S() is "511" or MainSiteInventory);

        var purchase = await api.PostInvoice(Purchase(supplier, product, 5, 60));
        Assert.Equal(201, purchase.Status);
        var purchaseLines = await JournalLinesAsync(api, purchase.Data!["journalEntryId"].S());
        Assert.Equal(300m, Debit(purchaseLines, a.Purchases));
        Assert.Equal(45m, Debit(purchaseLines, "1131"));
        Assert.DoesNotContain(purchaseLines, l => l!["accountCode"].S() is MainSiteInventory or "514");

        // بلا حساب مردودات (اختياري، لم يُنشأ): المرتجع بسعر مستنده يخفّض المشتريات نفسها
        Assert.Null((await api.Get("/accounting-settings")).Data!["purchaseReturnsAccountCode"]?.GetValue<string?>());
        var ret = await api.PostReturn(new { originalInvoiceId = purchase.Data["id"].S(), returnReason = "x", lines = new[] { new { itemId = product, quantity = 2 } } });
        Assert.True(ret.Success, ret.Body?.ToJsonString());
        var retLines = await JournalLinesAsync(api, ret.Data!["journalEntryId"].S());
        Assert.Equal(120m, Credit(retLines, a.Purchases));
        Assert.DoesNotContain(retLines, l => l!["accountCode"].S() is MainSiteInventory or "514");
        Assert.Equal(400m, await BalanceAsync(api, MainSiteInventory)); // المشتريات والمرتجعات لا تمس المخزون داخل الفترة
    }

    [Fact]
    public async Task Adjustments_and_revaluation_post_nothing_and_counts_post_no_variance()
    {
        var (api, _) = await PeriodicTenantAsync();
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
        Assert.Equal(400m, await BalanceAsync(api, MainSiteInventory)); // الرصيد الافتتاحي وحده
        Assert.Equal(0m, await BalanceAsync(api, "513"));
    }

    [Fact]
    public async Task Periodic_count_posts_one_unified_entry_and_keeps_inventory_at_the_counted_value()
    {
        var (api, a) = await PeriodicTenantAsync();
        var product = await SeedProductAsync(api, stock: 10, cost: 40);
        Assert.Equal(201, (await api.PostInvoice(Sale(product, 2))).Status);

        var approved = await ApproveCountAsync(api, ItemCount(product, 7)); // عجز قطعة: 7 × 40
        Ok(approved);
        var entry = await JournalLinesAsync(api, approved.Data!["journalEntryId"].S());
        Assert.Equal(400m, Credit(entry, MainSiteInventory)); // X: الرصيد الدفتري
        Assert.Equal(400m, Debit(entry, a.Opening));
        Assert.Equal(280m, Debit(entry, MainSiteInventory));  // Y: الفعلي
        Assert.Equal(280m, Credit(entry, a.Ending));
        Assert.Equal(280m, await BalanceAsync(api, MainSiteInventory)); // لا يُصفَّر المخزون بعد الجرد
        Assert.Equal(400m, await BalanceAsync(api, a.Opening));
        Assert.Equal(-280m, await BalanceAsync(api, a.Ending)); // دائن على حساب مصروف (طبيعته مدينة)
        Assert.Equal(0, (await api.Get("/journalentries")).Data!["items"]!.AsArray()
            .Count(e => e!["sourceType"]?.ToString() == "inventory_period_opening")); // لا عكس في اليوم التالي

        Assert.Equal(409, (await api.PostInvoice(Sale(product, 1))).Status);                       // ما قبل القيمة المثبتة مقفل
        Assert.Equal(409, (await api.Post("/stockmovements/adjust", new { itemId = product, type = "adjustment_in", quantity = 1, unitCost = 40,
            referenceNumber = "LATE", date = Today })).Status);
        Assert.Equal(409, (await ApproveCountAsync(api, ItemCount(product, 6))).Status);           // قيد آخر مدة ثانٍ لليوم نفسه
    }

    [Fact]
    public async Task Unified_entry_reverses_and_restates_each_warehouse_inventory_account()
    {
        var (api, a) = await PeriodicTenantAsync();
        await AccountAsync(api, "1149", "asset", "114");
        var jed = await api.Post("/warehouses", new { code = "JED", nameAr = "معرض جدة", nameEn = "Jeddah", location = "جدة", status = "active", inventoryAccountCode = "1149" });
        Assert.Equal(201, jed.Status);
        var product = await SeedProductAsync(api, stock: 0);
        await OpeningStockAsync(api, product, 10, 40, Today);
        await OpeningStockAsync(api, product, 5, 40, Today, jed.Data!["id"].G());
        Assert.Equal(400m, await BalanceAsync(api, MainSiteInventory));
        Assert.Equal(200m, await BalanceAsync(api, "1149"));
        Assert.Equal(201, (await api.PostInvoice(Sale(product, 2))).Status); // من الموقع الرئيسي

        var approved = await ApproveCountAsync(api, ItemCount(product, 13)); // بلا فروق: 8 في الرئيسي و5 في جدة
        Ok(approved);
        var entry = await JournalLinesAsync(api, approved.Data!["journalEntryId"].S());
        Assert.Equal(400m, Credit(entry, MainSiteInventory));
        Assert.Equal(200m, Credit(entry, "1149"));
        Assert.Equal(600m, Debit(entry, a.Opening));
        Assert.Equal(320m, Debit(entry, MainSiteInventory));
        Assert.Equal(200m, Debit(entry, "1149"));
        Assert.Equal(520m, Credit(entry, a.Ending));
        Assert.Equal(320m, await BalanceAsync(api, MainSiteInventory));
        Assert.Equal(200m, await BalanceAsync(api, "1149"));
    }

    [Fact]
    public async Task Two_counts_in_a_year_do_not_double_count_and_statements_show_cost_of_sales()
    {
        var (api, a) = await PeriodicTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var product = await SeedProductAsync(api, stock: 0);
        var first = Today.AddDays(-10);
        await OpeningStockAsync(api, product, 10, 40, first.AddDays(-1));
        Ok(await ApproveCountAsync(api, ItemCount(product, 10, date: first))); // Y1 = 400 = X

        Assert.Equal(201, (await api.PostInvoice(Purchase(supplier, product, 5, 60))).Status);
        Assert.Equal(201, (await api.PostInvoice(Sale(product, 3))).Status);
        Ok(await ApproveCountAsync(api, ItemCount(product, 12)));

        var y2 = await BalanceAsync(api, MainSiteInventory);
        var stock = (await api.Get($"/products/{product}")).Data!;
        Assert.Equal(Math.Round(stock["currentStock"].D() * stock["averageCost"].D(), 2), y2); // الميزانية = قيمة الجرد الثاني
        Assert.Equal(800m, await BalanceAsync(api, a.Opening));            // 400 الافتتاحي + 400 من الجرد الأول
        Assert.Equal(-(400m + y2), await BalanceAsync(api, a.Ending));
        // تكلفة المبيعات = أول المدة + المشتريات − آخر المدة = الافتتاحي + المشتريات − الجرد الأخير
        var cogs = await BalanceAsync(api, a.Opening) + await BalanceAsync(api, a.Purchases) + await BalanceAsync(api, a.Ending);
        Assert.Equal(400m + 300m - y2, cogs);

        var income = (await api.Get($"/financialstatements/IncomeStatement?from={first.AddDays(-1):yyyy-MM-dd}&to={Today:yyyy-MM-dd}")).Data!;
        static IEnumerable<string> Codes(JsonNode section) => section["lines"]!.AsArray().Select(l => l!["accountCode"].S());
        Assert.Contains(a.Purchases, Codes(income["costOfSales"]!));
        Assert.Contains(a.Opening, Codes(income["costOfSales"]!));
        Assert.Contains(a.Ending, Codes(income["costOfSales"]!));
        Assert.DoesNotContain(Codes(income["operatingExpenses"]!), c => c == a.Purchases || c == a.Opening || c == a.Ending);
        Assert.DoesNotContain(a.Ending, Codes(income["revenue"]!));
        Assert.Equal(cogs, income["costOfSales"]!["total"].D());

        var position = (await api.Get($"/financialstatements/FinancialPosition?asOf={Today:yyyy-MM-dd}")).Data!;
        Assert.Equal(y2, position["currentAssets"]!["lines"]!.AsArray().Single(l => l!["accountCode"].S() == MainSiteInventory)!["amount"].D());
        Assert.True(position["isBalanced"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Count_dated_earlier_posts_on_its_date_and_later_movements_block_approval()
    {
        // جرد بتاريخ سابق يُعتمد لاحقاً: القيد بتاريخ الجرد لا الاعتماد
        var (api, a) = await PeriodicTenantAsync();
        var product = await SeedProductAsync(api, stock: 0);
        var countDate = Today.AddDays(-5);
        await OpeningStockAsync(api, product, 10, 40, countDate.AddDays(-1));
        var approved = await ApproveCountAsync(api, ItemCount(product, 10, date: countDate));
        Ok(approved);
        var entry = (await api.Get($"/journalentries/{approved.Data!["journalEntryId"].S()}")).Data!;
        Assert.Equal(countDate, entry["date"]!.GetValue<DateTime>().Date);

        // حركة بعد تاريخ الجرد: يُرفض الاعتماد بمراجعها وعددها
        var (api2, _) = await PeriodicTenantAsync();
        var product2 = await SeedProductAsync(api2, stock: 0);
        await OpeningStockAsync(api2, product2, 10, 40, countDate.AddDays(-1));
        var pending = await SubmitCountAsync(api2, ItemCount(product2, 10, date: countDate));
        Assert.Equal(201, (await api2.PostInvoice(Sale(product2, 1))).Status); // اليوم > تاريخ الجرد
        var blocked = await ApproveAsync(api2, pending);
        Assert.Equal(409, blocked.Status);
        Assert.Contains("1", blocked.Body!.ToJsonString());

        // حركة سُجّلت بعد الإرسال بتاريخ سابق للجرد: تغيّر الكميات التي يمثلها
        var (api3, _) = await PeriodicTenantAsync();
        var product3 = await SeedProductAsync(api3, stock: 0);
        await OpeningStockAsync(api3, product3, 10, 40, countDate.AddDays(-2));
        var pending3 = await SubmitCountAsync(api3, ItemCount(product3, 10, date: countDate));
        await OpeningStockAsync(api3, product3, 1, 40, countDate.AddDays(-1));
        Assert.Equal(409, (await ApproveAsync(api3, pending3)).Status);
        Assert.Equal(0m, await BalanceAsync(api3, (await AccountsOfAsync(api3)).Ending)); // لا قيد
    }

    [Fact]
    public async Task Periodic_count_must_cover_all_warehouses()
    {
        var (api, _) = await PeriodicTenantAsync();
        var product = await SeedProductAsync(api);
        var warehouse = (await api.Get("/warehouses")).Data!["items"]!.AsArray().First()!["id"].G();
        // يُرفض عند الإرسال (EnsureSubmittableAsync) قبل الاعتماد
        var created = await api.Post("/inventorycounts", ItemCount(product, 9, warehouseId: warehouse));
        Assert.Equal(201, created.Status);
        Assert.Equal(400, (await api.Post($"/inventorycounts/{created.Data!["id"].S()}/submit")).Status);
    }

    [Fact]
    public async Task Switching_from_perpetual_keeps_the_inventory_asset_until_the_first_count_and_closes_the_previous_method()
    {
        var api = await NewTenantAsync();
        var thisYear = new DateTime(Today.Year, 1, 1);
        var product = await SeedProductAsync(api, stock: 0);
        await OpeningStockAsync(api, product, 10, 40, thisYear.AddMonths(-6)); // مستمر: على المخزون
        Assert.Equal(400m, await BalanceAsync(api, MainSiteInventory));
        var switched = await api.Put("/accounting-settings", new { inventoryMethod = "periodic", inventoryMethodEffectiveFrom = thisYear });
        Assert.True(switched.Status == 200, switched.Body?.ToJsonString());
        var a = await AccountsOfAsync(api);

        var sale = await api.PostInvoice(Sale(product, 2));
        Assert.Equal(201, sale.Status);
        Assert.DoesNotContain(await JournalLinesAsync(api, sale.Data!["journalEntryId"].S()), l => l!["accountCode"].S() is "511" or MainSiteInventory);
        Assert.Equal(400m, await BalanceAsync(api, MainSiteInventory));   // لا تحويل إلى أول المدة: يبقى أصلاً
        Assert.Equal(0m, await BalanceAsync(api, a.Opening));
        Assert.Equal(409, (await api.Post("/stockmovements/adjust", new { itemId = product, type = "adjustment_in", quantity = 1, unitCost = 40,
            referenceNumber = "LATE", date = thisYear.AddDays(-1) })).Status); // ما قبل بدء الدوري أُقفل

        Ok(await ApproveCountAsync(api, ItemCount(product, 8)));
        Assert.Equal(400m, await BalanceAsync(api, a.Opening));          // الجرد الأول يحمّل الرصيد الدفتري مرة واحدة
        Assert.Equal(320m, await BalanceAsync(api, MainSiteInventory));
    }

    [Fact]
    public async Task Perpetual_inventory_is_unaffected()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 10, cost: 40);
        var sale = await api.PostInvoice(Sale(product, 2));
        var lines = await JournalLinesAsync(api, sale.Data!["journalEntryId"].S());
        Assert.Equal(80m, Debit(lines, "511"));
        Assert.Equal(80m, Credit(lines, MainSiteInventory));
        var count = await ApproveCountAsync(api, ItemCount(product, 7, countType: "full")); // عجز قطعة بتاريخ الاعتماد
        Assert.Equal(200, count.Status);
        Assert.Equal(40m, Debit(await JournalLinesAsync(api, count.Data!["journalEntryId"].S()), "513"));
        Assert.Equal(280m, await BalanceAsync(api, MainSiteInventory));
        // قواعد الدوري لا تُطبَّق على حفظ إعدادات المستمر
        Assert.Equal(200, (await api.Put("/accounting-settings", new { purchasesAccountCode = "524", endingInventoryAccountCode = "421" })).Status);
        Assert.Equal(0, (await api.Get("/accounting-settings/periodic-legacy")).Data!["entries"]!.AsArray().Count);
    }

    [Fact]
    public async Task Manual_inventory_account_is_rejected_on_periodic_stock_documents()
    {
        var (api, _) = await PeriodicTenantAsync();

        var (supplier, _) = await SeedSupplierAsync(api); // فاتورة الشراء الضريبية تتطلب مورداً
        await AccountAsync(api, "1146", "asset", "114");
        var product = await SeedProductAsync(api, stock: 0);
        Assert.Equal(400, (await api.PostInvoice(Purchase(supplier, product, 1, 50, inventoryAccountCode: "1146"))).Status);
    }

    // ---------------- إقفال السنة ----------------

    [Fact]
    public async Task Closing_a_periodic_year_requires_a_closing_count_or_an_explicit_override_with_reason()
    {
        var lastYear = Today.Year - 1;
        var yearEnd = new DateTime(lastYear, 12, 31);

        // بلا جرد آخر المدة: رفض، والتجاوز يتطلب سبباً
        var (api, _) = await PeriodicTenantAsync();
        var product = await SeedProductAsync(api, stock: 0);
        await OpeningStockAsync(api, product, 10, 40, yearEnd.AddDays(-30));
        var check = (await api.Get($"/fiscalperiods/close-year-check?year={lastYear}")).Data!;
        Assert.True(check["periodic"]!.GetValue<bool>());
        Assert.False(check["yearEndCounted"]!.GetValue<bool>());
        Assert.True(check["canOverride"]!.GetValue<bool>()); // المالك
        Assert.Equal(409, (await api.Post("/fiscalperiods/close-year", new { year = lastYear })).Status);
        Assert.Equal(400, (await api.Post("/fiscalperiods/close-year", new { year = lastYear, overrideMissingPeriodEnd = true, overrideReason = " " })).Status);
        var overridden = await api.Post("/fiscalperiods/close-year", new { year = lastYear, overrideMissingPeriodEnd = true, overrideReason = "جرد متأخر بموافقة الإدارة" });
        Assert.True(overridden.Status == 200, overridden.Body?.ToJsonString());
        Assert.True(overridden.Data!["overriddenMissingPeriodEnd"]!.GetValue<bool>());

        // بجرد آخر المدة بتاريخ 31/12 يُعتمد لاحقاً: القيد بتاريخه والإقفال يمر بلا تجاوز
        var (api2, a2) = await PeriodicTenantAsync();
        var product2 = await SeedProductAsync(api2, stock: 0);
        await OpeningStockAsync(api2, product2, 10, 40, yearEnd.AddDays(-30));
        var approved = await ApproveCountAsync(api2, ItemCount(product2, 9, date: yearEnd));
        Ok(approved);
        Assert.Equal(yearEnd, (await api2.Get($"/journalentries/{approved.Data!["journalEntryId"].S()}")).Data!["date"]!.GetValue<DateTime>().Date);
        Assert.Equal(360m, await BalanceAsync(api2, MainSiteInventory));
        var closed = await api2.Post("/fiscalperiods/close-year", new { year = lastYear });
        Assert.True(closed.Status == 200, closed.Body?.ToJsonString());
        Assert.False(closed.Data!["overriddenMissingPeriodEnd"]!.GetValue<bool>());
        Assert.Equal(360m, await BalanceAsync(api2, MainSiteInventory)); // المخزون أصل يبقى بعد الإقفال
        Assert.Equal(0m, await BalanceAsync(api2, a2.Opening));           // حسابات الدخل أُقفلت
    }

    /// <summary>
    /// الموقع الإضافي يُنشأ له بضاعة أول المدة وآخرها باسمه بجوار حسابي الإعدادات، وقيد الجرد يقابل حساب مخزونه بهما (رصيده الدفتري قيد مُدرج
    /// بلا كميات)، والرئيسي على حسابي الإعدادات.
    /// </summary>
    [Fact]
    public async Task Each_added_site_has_its_own_opening_and_closing_inventory_and_the_count_entry_uses_them()
    {
        var (api, a) = await PeriodicTenantAsync();
        var site = await api.Post("/warehouses", new { code = "JD2", nameAr = "معرض جدة", nameEn = "Jeddah", location = "جدة", status = "active" });
        Assert.True(site.Status == 201, site.Body?.ToJsonString());
        var siteInventory = site.Data!["inventoryAccountCode"].S();
        var siteOpening = site.Data["openingInventoryAccountCode"].S();
        var siteEnding = site.Data["endingInventoryAccountCode"].S();
        foreach (var (own, general) in new[] { (siteOpening, a.Opening), (siteEnding, a.Ending) })
        {
            Assert.NotEqual(general, own);
            var account = (await api.Get($"/accounts/ByCode/{own}")).Data!;
            Assert.Contains("معرض جدة", account["nameAr"].S());
            Assert.Equal("expense", account["type"].S().ToLowerInvariant());
            Assert.Equal((await api.Get($"/accounts/ByCode/{general}")).Data!["parentCode"].S(), account["parentCode"].S());
        }

        var product = await SeedProductAsync(api, stock: 0);
        await OpeningStockAsync(api, product, 10, 40, Today);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>();
            var entry = new JournalEntry
            {
                TenantId = api.TenantId, EntryNumber = "SITE-" + Guid.NewGuid().ToString("N")[..6], Date = Today, Description = "رصيد مخزون الموقع",
                SourceType = "test_site_balance", Status = JournalEntryStatus.Posted, TotalDebit = 200, TotalCredit = 200,
            };
            entry.Lines.Add(new JournalEntryLine { TenantId = api.TenantId, AccountCode = siteInventory, AccountName = "مخزون جدة", Debit = 200 });
            entry.Lines.Add(new JournalEntryLine { TenantId = api.TenantId, AccountCode = "33", AccountName = "أرصدة افتتاحية", Credit = 200 });
            db.Add(entry);
            await db.SaveChangesAsync();
        }

        var approved = await ApproveCountAsync(api, ItemCount(product, 10));
        Ok(approved);
        var lines = await JournalLinesAsync(api, approved.Data!["journalEntryId"].S());
        Assert.Equal(200m, Credit(lines, siteInventory));
        Assert.Equal(200m, Debit(lines, siteOpening));
        Assert.Equal(400m, Credit(lines, MainSiteInventory));
        Assert.Equal(400m, Debit(lines, a.Opening));
        Assert.Equal(400m, Debit(lines, MainSiteInventory));
        Assert.Equal(400m, Credit(lines, a.Ending));
    }

    // ---------------- معالجة قيود النموذج القديم ----------------

    /// <summary>قيد قديم كما رحّله النموذج السابق (تحويل رصيد المخزون إلى أول المدة)، مُدرج مباشرة لأن مساره أُزيل.</summary>
    private async Task<Guid> InsertLegacyAsync(Client api, string sourceType, DateTime date, string opening, decimal amount)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>();
        var entry = new JournalEntry
        {
            TenantId = api.TenantId, EntryNumber = "LEG-" + Guid.NewGuid().ToString("N")[..6], Date = date, Description = "قيد جرد دوري قديم",
            SourceType = sourceType, Status = JournalEntryStatus.Posted, TotalDebit = amount, TotalCredit = amount,
        };
        entry.Lines.Add(new JournalEntryLine { TenantId = api.TenantId, AccountCode = opening, AccountName = "بضاعة أول المدة", Debit = amount });
        entry.Lines.Add(new JournalEntryLine { TenantId = api.TenantId, AccountCode = MainSiteInventory, AccountName = "مخزون", Credit = amount });
        db.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    [Fact]
    public async Task Legacy_entries_are_previewed_remediated_once_per_company_and_not_double_counted_by_the_next_count()
    {
        var (api, a) = await PeriodicTenantAsync();
        var (other, _) = await PeriodicTenantAsync();
        var product = await SeedProductAsync(api, stock: 10, cost: 40);
        await InsertLegacyAsync(api, "inventory_method_opening", Today, a.Opening, 400);
        Assert.Equal(0m, await LinesBalanceAsync(api, MainSiteInventory)); // النموذج القديم صفّر المخزون

        var preview = (await api.Get("/accounting-settings/periodic-legacy")).Data!;
        Assert.Single(preview["entries"]!.AsArray());
        Assert.Equal("ready", preview["entries"]![0]!["status"].S());
        Assert.Equal(400m, preview["readyAmount"].D());
        Assert.NotEmpty((await api.Get($"/financialstatements/FinancialPosition?asOf={Today:yyyy-MM-dd}")).Data!["warnings"]!.AsArray());
        Assert.Empty((await other.Get("/accounting-settings/periodic-legacy")).Data!["entries"]!.AsArray()); // منشأة أخرى لا تتأثر

        Assert.Equal(409, (await api.Post("/accounting-settings/periodic-legacy", new { fingerprint = "stale" })).Status);
        var applied = await api.Post("/accounting-settings/periodic-legacy", new { fingerprint = preview["fingerprint"].S() });
        Assert.True(applied.Status == 200, applied.Body?.ToJsonString());
        Assert.Single(applied.Data!["posted"]!.AsArray());
        Assert.Empty(applied.Data!["remaining"]!["entries"]!.AsArray());
        Assert.Equal(400m, await LinesBalanceAsync(api, MainSiteInventory));
        Assert.Equal(0m, await LinesBalanceAsync(api, a.Opening));
        Assert.Empty((await api.Get($"/financialstatements/FinancialPosition?asOf={Today:yyyy-MM-dd}")).Data!["warnings"]!.AsArray());
        var again = (await api.Get("/accounting-settings/periodic-legacy")).Data!;
        Assert.Equal(400, (await api.Post("/accounting-settings/periodic-legacy", new { fingerprint = again["fingerprint"].S() })).Status); // لا تكرار

        // الجرد التالي يحمّل الرصيد مرة واحدة: أول المدة 400 لا 800
        Ok(await ApproveCountAsync(api, ItemCount(product, 10)));
        Assert.Equal(400m, await LinesBalanceAsync(api, a.Opening));
        Assert.Equal(400m, await LinesBalanceAsync(api, MainSiteInventory));
    }

    [Fact]
    public async Task Legacy_entries_in_a_locked_period_need_an_explicit_first_open_date()
    {
        if (Today is { Month: 1, Day: 1 }) return; // أمس في سنة أخرى: لا تاريخ بديل
        var (api, a) = await PeriodicTenantAsync();
        await SeedProductAsync(api, stock: 10, cost: 40);
        await InsertLegacyAsync(api, "inventory_period_opening", Today.AddDays(-1), a.Opening, 400);
        Assert.Equal(200, (await api.Put("/fiscalperiods/lock", new { lockedThrough = Today.AddDays(-1) })).Status);

        var preview = (await api.Get("/accounting-settings/periodic-legacy")).Data!;
        Assert.Equal("locked_period", preview["entries"]![0]!["status"].S());
        Assert.Equal(Today, preview["entries"]![0]!["fallbackDate"]!.GetValue<DateTime>().Date);
        Assert.Equal(400, (await api.Post("/accounting-settings/periodic-legacy", new { fingerprint = preview["fingerprint"].S() })).Status);

        var applied = await api.Post("/accounting-settings/periodic-legacy", new { fingerprint = preview["fingerprint"].S(), useFirstOpenDateForLocked = true });
        Assert.True(applied.Status == 200, applied.Body?.ToJsonString());
        Assert.Equal(Today, applied.Data!["posted"]![0]!["date"]!.GetValue<DateTime>().Date);
        Assert.Equal(400m, await LinesBalanceAsync(api, MainSiteInventory));
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
