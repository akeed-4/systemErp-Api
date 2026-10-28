using ERP.Core.Models.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// إعدادات الحسابات المحاسبية: الافتراضي عند الفراغ، التحقق من الحساب قبل الحفظ، ترحيل المستندات على الحسابات المحددة
/// مع تقدم حساب المستند، منع تغيير حساب يحمل رصيداً أو حركات، وقواعد تغيير طريقة الجرد.
/// </summary>
[Collection("api")]
public class AccountingSettingsTests : TestBase
{
    public AccountingSettingsTests(ErpFactory f) : base(f) { }

    internal static object Sale(Guid product, decimal qty, string? revenueAccountCode = null)
        => new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", items = new[] { new { itemId = product, quantity = qty, unitPrice = 100, vatRate = 15, revenueAccountCode } } };

    internal static async Task AccountAsync(Client api, string code, string type, string parent)
        => Assert.Equal(201, (await api.Post("/accounts", new { code, nameAr = "حساب " + code, nameEn = "Account " + code, type, parentCode = parent,
            isDebitNature = type is "asset" or "expense" })).Status);

    internal static async Task<JsonArray> JournalLinesAsync(Client api, string entryId)
        => (await api.Get($"/journalentries/{entryId}")).Data!["lines"]!.AsArray();

    internal static decimal Debit(JsonArray lines, string code) => lines.Where(l => l!["accountCode"].S() == code).Sum(l => l!["debit"].D());
    internal static decimal Credit(JsonArray lines, string code) => lines.Where(l => l!["accountCode"].S() == code).Sum(l => l!["credit"].D());

    [Fact]
    public async Task Empty_settings_resolve_to_the_default_chart_accounts()
    {
        var api = await NewTenantAsync();
        var settings = (await api.Get("/accounting-settings")).Data!;
        Assert.Equal("perpetual", settings["inventoryMethod"].S());
        var effective = settings["effectiveAccounts"]!;
        Assert.Equal("1141", effective["inventoryAccountCode"].S());
        Assert.Equal("511", effective["cogsAccountCode"].S());
        Assert.Equal("513", effective["inventoryShortageAccountCode"].S());
        Assert.Equal("411", effective["salesRevenueAccountCode"].S());
        Assert.Equal("213", effective["outputVatAccountCode"].S());
        Assert.Equal("1131", effective["inputVatAccountCode"].S());
    }

    [Fact]
    public async Task Accounts_are_validated_before_saving()
    {
        var api = await NewTenantAsync();
        var (_, customerAccount) = await SeedCustomerAsync(api);
        Assert.Equal(400, (await api.Put("/accounting-settings", new { inventoryAccountCode = "9999" })).Status);        // غير موجود
        Assert.Equal(400, (await api.Put("/accounting-settings", new { inventoryAccountCode = "114" })).Status);         // حساب رئيسي
        Assert.Equal(400, (await api.Put("/accounting-settings", new { inventoryAccountCode = "411" })).Status);         // نوع لا يناسب
        Assert.Equal(400, (await api.Put("/accounting-settings", new { salesRevenueAccountCode = customerAccount })).Status); // حساب طرف
        Assert.Equal(400, (await api.Put("/accounting-settings", new { inventoryMethod = "periodic" })).Status);         // حسابات الدوري ناقصة
        Assert.Equal("perpetual", (await api.Get("/accounting-settings")).Data!["inventoryMethod"].S());
    }

    [Fact]
    public async Task Documents_post_to_configured_accounts_and_the_line_account_wins()
    {
        var api = await NewTenantAsync();
        await AccountAsync(api, "1145", "asset", "114");
        await AccountAsync(api, "516", "expense", "51");
        await AccountAsync(api, "414", "revenue", "41");
        await AccountAsync(api, "415", "revenue", "41");
        await AccountAsync(api, "219", "liability", "21");
        Assert.Equal(200, (await api.Put("/accounting-settings", new
        {
            inventoryAccountCode = "1145", cogsAccountCode = "516", salesRevenueAccountCode = "414", salesReturnsAccountCode = "415", outputVatAccountCode = "219",
        })).Status);

        var product = await SeedProductAsync(api, stock: 10, cost: 40);
        var sale = await api.PostInvoice(Sale(product, 2));
        Assert.Equal(201, sale.Status);
        var lines = await JournalLinesAsync(api, sale.Data!["journalEntryId"].S());
        Assert.Equal(200m, Credit(lines, "414"));
        Assert.Equal(30m, Credit(lines, "219"));
        Assert.Equal(80m, Debit(lines, "516"));
        Assert.Equal(80m, Credit(lines, "1145"));
        Assert.DoesNotContain(lines, l => l!["accountCode"].S() is "411" or "213" or "511" or "1141");

        var ret = await api.PostReturn(new { originalInvoiceId = sale.Data["id"].S(), returnReason = "x" });
        Assert.True(ret.Success, ret.Body?.ToJsonString());
        var retLines = await JournalLinesAsync(api, ret.Data!["journalEntryId"].S());
        Assert.Equal(200m, Debit(retLines, "415"));
        Assert.Equal(80m, Debit(retLines, "1145"));

        // حساب السطر المحدد يدوياً يتقدم على الإعداد
        var manual = await api.PostInvoice(Sale(product, 1, revenueAccountCode: "411"));
        Assert.Equal(100m, Credit(await JournalLinesAsync(api, manual.Data!["journalEntryId"].S()), "411"));
    }

    [Fact]
    public async Task Inventory_count_posts_shortage_and_surplus_to_their_accounts()
    {
        var api = await NewTenantAsync();
        await AccountAsync(api, "531", "expense", "52");
        await AccountAsync(api, "424", "revenue", "42");
        Assert.Equal(200, (await api.Put("/accounting-settings", new { inventoryShortageAccountCode = "531", inventorySurplusAccountCode = "424" })).Status);
        var shortItem = await SeedProductAsync(api, "S1", stock: 10, cost: 40);
        var extraItem = await SeedProductAsync(api, "S2", stock: 10, cost: 30);

        var count = await api.Post("/inventorycounts", new
        {
            scope = "items", countType = "full", countDate = DateTime.UtcNow,
            lines = new[] { new { itemId = shortItem, countedQuantity = 8m }, new { itemId = extraItem, countedQuantity = 11m } },
        });
        Assert.Equal(201, count.Status);
        var approval = await api.Post($"/inventorycounts/{count.Data!["id"].S()}/submit");
        var approved = await api.Post($"/inventorycountapprovals/{approval.Data!["id"].S()}/approve", new { });
        Assert.Equal(200, approved.Status);

        var lines = await JournalLinesAsync(api, approved.Data!["journalEntryId"].S());
        Assert.Equal(80m, Debit(lines, "531"));
        Assert.Equal(30m, Credit(lines, "424"));
        Assert.DoesNotContain(lines, l => l!["accountCode"].S() == "513");
    }

    [Fact]
    public async Task Vehicle_count_differences_keep_the_inventory_adjustment_account()
    {
        var api = await NewTenantAsync();
        await AccountAsync(api, "531", "expense", "52");
        Assert.Equal(200, (await api.Put("/accounting-settings", new { inventoryShortageAccountCode = "531" })).Status);
        var vin = Client.NewVin('4');
        Assert.Equal(201, (await api.Post("/vehicles", new { chassisNumber = vin, brandNameAr = "تويوتا", modelNameAr = "كامري", year = 2025,
            purchasePrice = 60000, sellingPrice = 70000, vatMode = "standard_15", location = "المعرض" })).Status);

        var count = await api.Post("/inventorycounts", new { scope = "vehicles", countDate = DateTime.UtcNow, location = "المعرض",
            lines = new[] { new { chassisNumber = vin, countedQuantity = 0 } } });
        var approval = (await api.Post($"/inventorycounts/{count.Data!["id"].S()}/submit")).Data!;
        var approved = await api.Post($"/inventorycountapprovals/{approval["id"].S()}/approve", new { });
        var lines = await JournalLinesAsync(api, approved.Data!["journalEntryId"].S());
        Assert.Equal(60000m, Debit(lines, "513"));
        Assert.DoesNotContain(lines, l => l!["accountCode"].S() == "531");
    }

    [Fact]
    public async Task Inventory_and_vat_accounts_are_locked_after_any_movement_even_at_zero_balance()
    {
        var api = await NewTenantAsync();
        await AccountAsync(api, "1145", "asset", "114");
        await AccountAsync(api, "219", "liability", "21");
        var product = await SeedProductAsync(api, stock: 1, cost: 40);
        Assert.Equal(201, (await api.PostInvoice(Sale(product, 1))).Status);
        Assert.Equal(0m, (await api.Get("/accounts/ByCode/1141")).Data!["balance"].D()); // بيع كل المخزون: الرصيد صفر والحركات باقية

        Assert.Equal(409, (await api.Put("/accounting-settings", new { inventoryAccountCode = "1145" })).Status);
        Assert.Equal(409, (await api.Put("/accounting-settings", new { outputVatAccountCode = "219" })).Status);
        Assert.Equal(200, (await api.Put("/accounting-settings", new { cogsAccountCode = "511" })).Status); // حساب نتيجة يُغيَّر بحرية
    }

    [Fact]
    public async Task Inventory_method_changes_before_the_first_entry_or_at_a_clean_financial_year_start()
    {
        var periodic = new { inventoryMethod = "periodic", purchasesAccountCode = "516", openingInventoryAccountCode = "517", endingInventoryAccountCode = "518" };

        var fresh = await NewTenantAsync();
        foreach (var code in new[] { "516", "517", "518" }) await AccountAsync(fresh, code, "expense", "51");
        var saved = await fresh.Put("/accounting-settings", periodic);
        Assert.Equal(200, saved.Status);
        Assert.Equal("periodic", saved.Data!["inventoryMethod"].S());
        Assert.Null(saved.Data["inventoryMethodEffectiveFrom"]); // قبل أول قيد: منذ البداية

        var api = await NewTenantAsync();
        foreach (var code in new[] { "516", "517", "518" }) await AccountAsync(api, code, "expense", "51");
        await SeedProductAsync(api); // قيد وحركة مخزون اليوم
        var thisYear = new DateTime(DateTime.UtcNow.Year, 1, 1);
        Assert.Equal(400, (await api.Put("/accounting-settings", periodic)).Status); // تاريخ السريان مطلوب
        Assert.Equal(400, (await api.Put("/accounting-settings", new { periodic.inventoryMethod, periodic.purchasesAccountCode, periodic.openingInventoryAccountCode,
            periodic.endingInventoryAccountCode, inventoryMethodEffectiveFrom = thisYear.AddMonths(1) })).Status); // ليس بداية سنة
        Assert.Equal(409, (await api.Put("/accounting-settings", new { periodic.inventoryMethod, periodic.purchasesAccountCode, periodic.openingInventoryAccountCode,
            periodic.endingInventoryAccountCode, inventoryMethodEffectiveFrom = thisYear })).Status); // حركات مخزون في السنة

        var nextYear = await api.Put("/accounting-settings", new { periodic.inventoryMethod, periodic.purchasesAccountCode, periodic.openingInventoryAccountCode,
            periodic.endingInventoryAccountCode, inventoryMethodEffectiveFrom = thisYear.AddYears(1) });
        Assert.Equal(200, nextYear.Status);
        Assert.Equal("perpetual", nextYear.Data!["previousInventoryMethod"].S());
        Assert.Equal(thisYear.AddYears(1), DateTime.Parse(nextYear.Data["inventoryMethodEffectiveFrom"].S()).Date);
    }

    private async Task<Client> PeriodicReadyTenantAsync()
    {
        var api = await NewTenantAsync();
        foreach (var code in new[] { "516", "517", "518" }) await AccountAsync(api, code, "expense", "51");
        return api;
    }

    private static object Periodic(DateTime? from) => new
    {
        inventoryMethod = "periodic", purchasesAccountCode = "516", openingInventoryAccountCode = "517", endingInventoryAccountCode = "518",
        inventoryMethodEffectiveFrom = from,
    };

    private async Task SetFinancialYearStartAsync(Client api, string monthDay)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>();
        var tenant = await db.Set<Tenant>().IgnoreQueryFilters().FirstAsync(t => t.Id == api.TenantId);
        tenant.FinancialYearStart = monthDay;
        await db.SaveChangesAsync();
    }

    // ---------- التحديث الجزئي ----------

    [Fact]
    public async Task Omitted_fields_keep_their_values_and_an_empty_code_restores_the_default()
    {
        var api = await PeriodicReadyTenantAsync();
        await AccountAsync(api, "414", "revenue", "41");
        Assert.Equal(200, (await api.Put("/accounting-settings", Periodic(null))).Status);
        Assert.Equal(200, (await api.Put("/accounting-settings", new { cogsAccountCode = "516" })).Status);

        var partial = (await api.Put("/accounting-settings", new { salesRevenueAccountCode = "414" })).Data!;
        Assert.Equal("periodic", partial["inventoryMethod"].S());   // الطريقة غير المرسلة لا تعود للمستمر
        Assert.Equal("516", partial["cogsAccountCode"].S());        // ولا يُمسح حساب لم يُرسل
        Assert.Equal("517", partial["openingInventoryAccountCode"].S());
        Assert.Equal("414", partial["salesRevenueAccountCode"].S());

        var cleared = (await api.Put("/accounting-settings", new { cogsAccountCode = "" })).Data!;
        Assert.Null(cleared["cogsAccountCode"]);
        Assert.Equal("511", cleared["effectiveAccounts"]!["cogsAccountCode"].S());
        Assert.Equal(400, (await api.Put("/accounting-settings", new { purchasesAccountCode = "" })).Status); // إلزامي في الدوري
    }

    // ---------- تعارض الأدوار ----------

    [Fact]
    public async Task Conflicting_cash_showroom_and_shared_balance_sheet_accounts_are_rejected()
    {
        var api = await NewTenantAsync();
        await AccountAsync(api, "1132", "asset", "113");
        Assert.Equal(400, (await api.Put("/accounting-settings", new { inventoryAccountCode = "1111" })).Status); // صندوق
        Assert.Equal(400, (await api.Put("/accounting-settings", new { inputVatAccountCode = "1112" })).Status);  // بنك
        Assert.Equal(400, (await api.Put("/accounting-settings", new { inventoryAccountCode = "1142" })).Status); // مخزون السيارات
        Assert.Equal(400, (await api.Put("/accounting-settings", new { cogsAccountCode = "512" })).Status);       // تكلفة السيارات
        Assert.Equal(400, (await api.Put("/accounting-settings", new { inventoryAccountCode = "1131" })).Status); // = ضريبة المدخلات الافتراضية
        Assert.Equal(400, (await api.Put("/accounting-settings", new { inputVatAccountCode = "213" })).Status);   // = ضريبة المخرجات
        var shared = await api.Put("/accounting-settings", new { inputVatAccountCode = "1132", outputVatAccountCode = "1132" });
        Assert.Equal(400, shared.Status);
        Assert.Null((await api.Get("/accounting-settings")).Data!["inputVatAccountCode"]);
    }

    // ---------- تعديل المستندات السابقة ----------

    [Fact]
    public async Task Editing_and_deleting_an_adjustment_stays_on_the_configured_inventory_account()
    {
        var api = await NewTenantAsync();
        await AccountAsync(api, "1145", "asset", "114");
        Assert.Equal(200, (await api.Put("/accounting-settings", new { inventoryAccountCode = "1145" })).Status);
        var product = await SeedProductAsync(api, stock: 10, cost: 40);
        var adjust = await api.Post("/stockmovements/adjust", new { itemId = product, type = "adjustment_in", quantity = 5, unitCost = 40, referenceNumber = "ADJ" });
        Assert.Equal(200, adjust.Status);
        var id = adjust.Data!["id"].S();

        Assert.Equal(200, (await api.Put($"/stockmovements/{id}", new { itemId = product, type = "adjustment_in", quantity = 6, unitCost = 40, referenceNumber = "ADJ" })).Status);
        Assert.Equal(640m, (await api.Get("/accounts/ByCode/1145")).Data!["balance"].D());
        Assert.Equal(200, (await api.Delete($"/stockmovements/{id}")).Status);
        Assert.Equal(400m, (await api.Get("/accounts/ByCode/1145")).Data!["balance"].D());
        Assert.Equal(0m, (await api.Get("/accounts/ByCode/1141")).Data!["balance"].D()); // لا أثر على الحساب الافتراضي
    }

    [Fact]
    public async Task Configured_inventory_account_reconciles_after_sales_and_revaluation()
    {
        var api = await NewTenantAsync();
        await AccountAsync(api, "1145", "asset", "114");
        Assert.Equal(200, (await api.Put("/accounting-settings", new { inventoryAccountCode = "1145" })).Status);
        var product = await SeedProductAsync(api, stock: 10, cost: 40);
        Assert.Equal(201, (await api.PostInvoice(Sale(product, 2))).Status);
        Assert.Equal(200, (await api.Put("/costing/policy", new { method = "last_purchase", recalculateOnNewPurchase = true, includeFreightAndCustoms = false,
            negativeInventoryPolicy = "prohibit", standardCostVarianceAccountCode = "513" })).Status);
        Assert.Equal(200, (await api.Post("/stockmovements/adjust", new { itemId = product, type = "adjustment_in", quantity = 2, unitCost = 70, referenceNumber = "X" })).Status);

        var items = (await api.Get("/reports/InventoryReconciliation")).Data!["rows"]!.AsArray().Single(r => r!["key"].S() == "items")!;
        Assert.True(items["difference"].D() == 0, items.ToJsonString());
        Assert.Equal(10 * 70m, items["ledgerValue"].D()); // الرصيد كله أُعيد تسعيره بآخر سعر
        Assert.Equal(10 * 70m, (await api.Get("/accounts/ByCode/1145")).Data!["balance"].D());
        Assert.Equal(0m, (await api.Get("/accounts/ByCode/1141")).Data!["balance"].D());
    }

    // ---------- المشتريات والمرتجعات والسندات والضريبة ----------

    [Fact]
    public async Task Purchase_return_follows_the_manual_inventory_account_of_its_original()
    {
        var api = await NewTenantAsync();
        await AccountAsync(api, "1146", "asset", "114");
        var product = await SeedProductAsync(api, stock: 0);
        var (supplier, _) = await SeedSupplierAsync(api); // فاتورة الشراء الضريبية تتطلب مورداً
        var purchase = await api.PostInvoice(new { kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "cash", partyId = supplier, inventoryAccountCode = "1146",
            items = new[] { new { itemId = product, quantity = 10, unitPrice = 50, vatRate = 15 } } });
        Assert.Equal(201, purchase.Status);
        Assert.Equal(500m, Debit(await JournalLinesAsync(api, purchase.Data!["journalEntryId"].S()), "1146"));

        var ret = await api.PostReturn(new { originalInvoiceId = purchase.Data["id"].S(), returnReason = "x", lines = new[] { new { itemId = product, quantity = 4 } } });
        Assert.True(ret.Success, ret.Body?.ToJsonString());
        Assert.Equal("1146", ret.Data!["inventoryAccountCode"].S());
        var lines = await JournalLinesAsync(api, ret.Data["journalEntryId"].S());
        Assert.Equal(200m, Credit(lines, "1146"));
        Assert.DoesNotContain(lines, l => l!["accountCode"].S() == "1141");
    }

    [Fact]
    public async Task Purchases_vouchers_and_the_vat_report_use_the_configured_vat_accounts()
    {
        var api = await NewTenantAsync();
        await AccountAsync(api, "1132", "asset", "113");
        await AccountAsync(api, "219", "liability", "21");
        Assert.Equal(200, (await api.Put("/accounting-settings", new { inputVatAccountCode = "1132", outputVatAccountCode = "219" })).Status);
        var product = await SeedProductAsync(api, stock: 5, cost: 40);

        Assert.Equal(201, (await api.PostInvoice(Sale(product, 1))).Status);                                     // مخرجات 15
        var (supplier, _) = await SeedSupplierAsync(api); // فاتورة الشراء الضريبية تتطلب مورداً
        var purchase = await api.PostInvoice(new { kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "cash", partyId = supplier,
            items = new[] { new { itemId = product, quantity = 1, unitPrice = 100, vatRate = 15 } } });          // مدخلات 15
        Assert.Equal(15m, Debit(await JournalLinesAsync(api, purchase.Data!["journalEntryId"].S()), "1132"));
        var voucher = await api.Post("/vouchers", new { type = "receipt", amount = 115, vatAmount = 15, partyName = "إيراد آخر", partyAccountCode = "421",
            treasuryAccountCode = "1111", paymentMethod = "cash" });                                             // مخرجات 15
        Assert.True(voucher.Success, voucher.Body?.ToJsonString());
        Assert.Equal(30m, (await api.Get("/accounts/ByCode/219")).Data!["balance"].D()); // بيع + سند
        // تسويتان يدويتان: مخرجات 40 ومدخلات 10
        Assert.Equal(200, (await api.Post("/journalentries", new { date = DateTime.UtcNow, description = "تسوية مخرجات",
            lines = new[] { new { accountCode = "524", debit = 40m, credit = 0m }, new { accountCode = "219", debit = 0m, credit = 40m } } })).Status);
        Assert.Equal(200, (await api.Post("/journalentries", new { date = DateTime.UtcNow, description = "تسوية مدخلات",
            lines = new[] { new { accountCode = "1132", debit = 10m, credit = 0m }, new { accountCode = "524", debit = 0m, credit = 10m } } })).Status);

        var vat = (await api.Get("/reports/VatReturn?from=2000-01-01&to=2100-01-01")).Data!;
        Assert.Equal(40m, vat["outputVatAdjustments"].D());
        Assert.Equal(10m, vat["inputVatAdjustments"].D());
        Assert.Equal(70m, vat["outputVat"].D());
        Assert.Equal(25m, vat["inputVat"].D());
        var stats = (await api.Get("/reports/FinancialStats")).Data!;
        Assert.Equal(70m, stats["outputVat"].D());
        Assert.Equal(25m, stats["inputVat"].D());
        foreach (var code in new[] { "213", "1131" }) Assert.Equal(0m, (await api.Get($"/accounts/ByCode/{code}")).Data!["balance"].D());
    }

    [Fact]
    public async Task Vat_return_separates_purchase_categories_net_of_returns_without_extra_tax_accounts()
    {
        var api = await NewTenantAsync();
        await AccountAsync(api, "1132", "asset", "113");
        Assert.Equal(200, (await api.Put("/accounting-settings", new { inputVatAccountCode = "1132" })).Status);
        var exemptItem = await SeedProductAsync(api, "EX", stock: 0);
        static object Line(string name, decimal price, decimal rate, string? category = null)
            => new { itemName = name, quantity = 1, unitPrice = price, vatRate = rate, vatCategory = category };

        var (supplier, _) = await SeedSupplierAsync(api); // فاتورة الشراء الضريبية تتطلب مورداً
        var purchase = await api.PostInvoice(new { kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "cash", partyId = supplier, items = new object[]
        {
            Line("خاضع", 100, 15), Line("صفري", 200, 0, "zero_rated"), Line("خارج النطاق", 400, 0, "out_of_scope"),
            new { itemId = exemptItem, quantity = 3, unitPrice = 100, vatRate = 0m, vatCategory = "exempt" },
        } });
        Assert.Equal(201, purchase.Status);
        var ret = await api.PostReturn(new { originalInvoiceId = purchase.Data!["id"].S(), returnReason = "x", lines = new[] { new { itemId = exemptItem, quantity = 1 } } });
        Assert.True(ret.Success, ret.Body?.ToJsonString());

        var vat = (await api.Get("/reports/VatReturn?from=2000-01-01&to=2100-01-01")).Data!;
        Assert.Equal(100m, vat["standardRatedPurchases"].D());
        Assert.Equal(200m, vat["zeroRatedPurchases"].D());
        Assert.Equal(200m, vat["exemptPurchases"].D());     // 300 ناقص مرتجع 100
        Assert.Equal(400m, vat["outOfScopePurchases"].D());
        Assert.Equal(15m, vat["inputVat"].D());              // الضريبة على الخاضع وحده
        Assert.Equal(15m, await BalanceOfAsync(api, "1132"));
    }

    private static async Task<decimal> BalanceOfAsync(Client api, string code) => (await api.Get($"/accounts/ByCode/{code}")).Data!["balance"].D();

    // ---------- العزل والصلاحيات ----------

    [Fact]
    public async Task Settings_are_isolated_per_company()
    {
        var a = await NewTenantAsync("شركة أ");
        await AccountAsync(a, "414", "revenue", "41");
        Assert.Equal(200, (await a.Put("/accounting-settings", new { salesRevenueAccountCode = "414" })).Status);

        var b = await NewTenantAsync("شركة ب");
        Assert.Equal("411", (await b.Get("/accounting-settings")).Data!["effectiveAccounts"]!["salesRevenueAccountCode"].S());
        var product = await SeedProductAsync(b);
        var sale = await b.PostInvoice(Sale(product, 1));
        Assert.Equal(100m, Credit(await JournalLinesAsync(b, sale.Data!["journalEntryId"].S()), "411"));
    }

    [Fact]
    public async Task Users_without_the_screen_cannot_read_or_change_settings()
    {
        var owner = await NewTenantAsync();
        var email = $"u{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await owner.Post("/users", new { name = "rep", email, phone = "1", password = "Passw0rd!", role = "sales_rep" })).Status);
        var rep = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");
        Assert.Equal(403, (await rep.Get("/accounting-settings")).Status);
        Assert.Equal(403, (await rep.Put("/accounting-settings", new { cogsAccountCode = "511" })).Status);
    }

    // ---------- حدود تغيير طريقة الجرد ----------

    [Fact]
    public async Task Method_change_respects_locked_periods_and_non_january_financial_years()
    {
        var api = await PeriodicReadyTenantAsync();
        await SeedProductAsync(api);
        var today = DateTime.UtcNow.Date;

        // بداية سنة مالية مقفلة
        var thisYear = new DateTime(today.Year, 1, 1);
        Assert.Equal(200, (await api.Put("/fiscalperiods/lock", new { lockedThrough = thisYear })).Status);
        Assert.Equal(409, (await api.Put("/accounting-settings", Periodic(thisYear))).Status);

        // سنة مالية تبدأ في يوليو: الأول من يناير ليس بداية سنة
        await SetFinancialYearStartAsync(api, "07-01");
        var nextJuly = new DateTime(today.Month >= 7 ? today.Year + 1 : today.Year, 7, 1);
        Assert.Equal(400, (await api.Put("/accounting-settings", Periodic(new DateTime(today.Year + 1, 1, 1)))).Status);
        var saved = await api.Put("/accounting-settings", Periodic(nextJuly));
        Assert.Equal(200, saved.Status);
        Assert.Equal(nextJuly, DateTime.Parse(saved.Data!["inventoryMethodEffectiveFrom"].S()).Date);

        // تغيير ثانٍ (لا إلغاء) قبل إقفال الدفاتر حتى بداية الطريقة الحالية
        Assert.Equal(200, (await api.Post("/journalentries", new { date = nextJuly.AddDays(5), description = "تكلفة",
            lines = new[] { new { accountCode = "511", debit = 1m, credit = 0m }, new { accountCode = "1111", debit = 0m, credit = 1m } } })).Status);
        Assert.Equal(409, (await api.Put("/accounting-settings", new { inventoryMethod = "perpetual", inventoryMethodEffectiveFrom = nextJuly.AddYears(1) })).Status);
    }

    [Fact]
    public async Task A_scheduled_method_change_can_be_cancelled_until_inventory_activity_reaches_it()
    {
        var api = await PeriodicReadyTenantAsync();
        await SeedProductAsync(api);
        var nextYear = new DateTime(DateTime.UtcNow.Year + 1, 1, 1);

        Assert.Equal(200, (await api.Put("/accounting-settings", Periodic(nextYear))).Status);
        var cancelled = (await api.Put("/accounting-settings", new { inventoryMethod = "perpetual" })).Data!;
        Assert.Equal("perpetual", cancelled["inventoryMethod"].S());
        Assert.Null(cancelled["inventoryMethodEffectiveFrom"]);
        Assert.Null(cancelled["previousInventoryMethod"]);

        // جدولة ثانية ثم قيد على تكلفة المبيعات بعد تاريخ السريان: لا إلغاء بعده
        Assert.Equal(200, (await api.Put("/accounting-settings", Periodic(nextYear))).Status);
        Assert.Equal(200, (await api.Post("/journalentries", new { date = nextYear.AddDays(3), description = "تكلفة",
            lines = new[] { new { accountCode = "511", debit = 1m, credit = 0m }, new { accountCode = "1111", debit = 0m, credit = 1m } } })).Status);
        Assert.False((await api.Put("/accounting-settings", new { inventoryMethod = "perpetual" })).Success);
        Assert.Equal("periodic", (await api.Get("/accounting-settings")).Data!["inventoryMethod"].S());
    }
}
