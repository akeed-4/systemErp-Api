using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// الأرصدة الافتتاحية كقيود، ربط السداد بالفواتير وأعمار الديون، القيمة التخريدية واستبعاد الأصل وعكس الإهلاك،
/// ضريبة المصروف المباشر بسند صرف، وإيراد كل سطر على حسابه ومركز تكلفته.
/// </summary>
[Collection("api")]
public class AccountingGapsTests : TestBase
{
    public AccountingGapsTests(ErpFactory f) : base(f) { }

    private static async Task<decimal> BalanceAsync(Client api, string code) => (await api.Get($"/accounts/ByCode/{code}")).Data!["balance"].D();

    private static object CreditSale(Guid customer, decimal price, DateTime? issueDate = null)
        => new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "credit", partyId = customer, issueDate = issueDate ?? DateTime.UtcNow,
            items = new[] { new { itemName = "خدمة", quantity = 1, unitPrice = price, vatRate = 15 } },
        };

    private static object Receipt(string account, decimal amount, params object[] allocations)
        => new { type = "receipt", amount, partyName = "عميل", partyAccountCode = account, treasuryAccountCode = "1111", paymentMethod = "cash", allocations };

    // ---------- الأرصدة الافتتاحية ----------
    [Fact]
    public async Task Opening_balances_are_posted_to_the_ledger_and_not_added_on_top_of_it()
    {
        var api = await NewTenantAsync();
        object Customer(decimal opening) => new { nameAr = "عميل افتتاحي", phone = "050", city = "الرياض", creditLimit = 100000, creditPeriodDays = 30, status = "active", openingBalance = opening };

        var created = await api.Post("/customers", Customer(500));
        Assert.Equal(201, created.Status);
        var id = created.Data!["id"].G(); var account = created.Data["accountCode"].S();
        Assert.Equal(500, created.Data["currentBalance"].D());
        Assert.Equal(500, await BalanceAsync(api, account)); // في حساب العميل لا خارجه
        Assert.Equal(500, await BalanceAsync(api, "33"));    // مقابل حساب الأرصدة الافتتاحية

        // الحركة تُضاف مرة واحدة فوق الافتتاحي
        Assert.Equal(201, (await api.Post("/invoices", CreditSale(id, 100))).Status);
        Assert.Equal(615, (await api.Get($"/customers/{id}")).Data!["currentBalance"].D());

        // تعديل الافتتاحي يعكس قيده ويرحّل الجديد
        Assert.Equal(200, (await api.Put($"/customers/{id}", Customer(200))).Status);
        Assert.Equal(315, (await api.Get($"/customers/{id}")).Data!["currentBalance"].D());
        Assert.Equal(200, await BalanceAsync(api, "33"));

        // المورد دائن والبنك مدين
        var supplier = await api.Post("/suppliers", new { nameAr = "مورد", phone = "050", city = "جدة", status = "active", vatNumber = Client.NewVat(), openingBalance = 300 });
        Assert.Equal(300, supplier.Data!["currentBalance"].D());
        Assert.Equal(300, await BalanceAsync(api, supplier.Data["accountCode"].S()));
        var bank = await api.Post("/banks", new { nameAr = "بنك", nameEn = "Bank", accountNumber = "123456", iban = "SA0380000000608010167519", openingBalance = 1000 });
        Assert.Equal(201, bank.Status);
        Assert.Equal(1000, bank.Data!["currentBalance"].D());
        Assert.Equal(200 - 300 + 1000, await BalanceAsync(api, "33"));

        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
    }

    // ---------- ربط السداد بالفواتير وأعمار الديون ----------
    [Fact]
    public async Task Receipts_are_allocated_to_invoices_and_drive_amount_due_and_aging()
    {
        var api = await NewTenantAsync();
        var (customer, account) = await SeedCustomerAsync(api);
        var (otherCustomer, _) = await SeedCustomerAsync(api, name: "عميل آخر");
        var old = (await api.Post("/invoices", CreditSale(customer, 200, DateTime.UtcNow.AddDays(-100)))).Data!["id"].S(); // 230 عمرها 100 يوم
        var recent = (await api.Post("/invoices", CreditSale(customer, 100))).Data!["id"].S();                             // 115
        var foreign = (await api.Post("/invoices", CreditSale(otherCustomer, 100))).Data!["id"].S();
        Assert.Equal(230, (await api.Get($"/invoices/{old}")).Data!["amountDue"].D());

        // توزيع مرفوض: فاتورة طرف آخر، أكثر من المتبقي، أكثر من مبلغ السند
        Assert.Equal(400, (await api.Post("/vouchers", Receipt(account, 50, new { invoiceId = foreign, amount = 50 }))).Status);
        Assert.Equal(400, (await api.Post("/vouchers", Receipt(account, 300, new { invoiceId = recent, amount = 200 }))).Status);
        Assert.Equal(400, (await api.Post("/vouchers", Receipt(account, 100, new { invoiceId = old, amount = 150 }))).Status);

        // سند واحد يسدّد القديمة كلها وجزءاً من الحديثة
        var voucher = await api.Post("/vouchers", Receipt(account, 300, new { invoiceId = old, amount = 230 }, new { invoiceId = recent, amount = 70 }));
        Assert.True(voucher.Success);
        Assert.Equal(new[] { 230m, 70m }, voucher.Data!["allocations"]!.AsArray().Select(a => a!["amount"].D()));
        Assert.Equal(0, (await api.Get($"/invoices/{old}")).Data!["amountDue"].D());
        Assert.Equal(45, (await api.Get($"/invoices/{recent}")).Data!["amountDue"].D());
        var open = Assert.Single((await api.Get($"/vouchers/OpenInvoices?partyAccountCode={account}")).Data!.AsArray())!;
        Assert.Equal(recent, open["id"].S()); Assert.Equal(45, open["amountDue"].D());

        // دفعة مقدمة بلا توزيع تظهر غير موزّعة في أعمار الديون
        Assert.True((await api.Post("/vouchers", Receipt(account, 20))).Success);
        var row = (await api.Get("/reports/Aging?type=receivable")).Data!["rows"]!.AsArray().Single(r => r!["partyId"].G() == customer)!;
        Assert.Equal(45, row["days0To30"].D()); Assert.Equal(0, row["over90"].D());
        Assert.Equal(45, row["totalDue"].D()); Assert.Equal(20, row["unallocated"].D()); Assert.Equal(25, row["net"].D());
        Assert.Equal(25, (await api.Get($"/customers/{customer}")).Data!["currentBalance"].D()); // يطابق الدفاتر

        // حذف السند يعيد المتبقي على فواتيره، والقديمة تعود لشريحة ما فوق 90 يوماً
        Assert.Equal(200, (await api.Delete($"/vouchers/{voucher.Data["id"].S()}")).Status);
        Assert.Equal(230, (await api.Get($"/invoices/{old}")).Data!["amountDue"].D());
        row = (await api.Get("/reports/Aging?type=receivable")).Data!["rows"]!.AsArray().Single(r => r!["partyId"].G() == customer)!;
        Assert.Equal(230, row["over90"].D()); Assert.Equal(115, row["days0To30"].D());

        // مرتجع آجل يُنقص المتبقي على فاتورته
        Assert.Equal(200, (await api.Post("/invoices/returns", new { originalInvoiceId = recent, returnReason = "x" })).Status);
        Assert.Equal(0, (await api.Get($"/invoices/{recent}")).Data!["amountDue"].D());
    }

    // ---------- الأصول الثابتة ----------
    private static async Task<Guid> SeedCostCenterAsync(Client api, string code)
        => (await api.Post("/costcenters", new { code, nameAr = "مركز " + code, nameEn = code, isActive = true })).Data!["id"].G();

    private static object Asset(string code, Guid center, decimal cost, decimal rate, decimal salvage = 0, decimal? book = null)
        => new { assetCode = code, nameAr = "أصل " + code, nameEn = code, purchaseDate = "2026-01-10", purchaseCost = cost, currentBookValue = book ?? cost, depreciationRate = rate, salvageValue = salvage, costCenterId = center };

    private static Task<Res> Depreciate(Client api, string period, Guid asset) => api.Post("/fixedassets/depreciation/post", new { period, assetIds = new[] { asset } });

    [Fact]
    public async Task Depreciation_is_based_on_cost_less_salvage_and_never_goes_below_it()
    {
        var api = await NewTenantAsync();
        var center = await SeedCostCenterAsync(api, "MNT");
        Assert.Equal(400, (await api.Post("/fixedassets", Asset("FA-0", center, 1000, 20, salvage: 1500))).Status); // تخريدية فوق التكلفة

        var asset = (await api.Post("/fixedassets", Asset("FA-1", center, 12000, 100, salvage: 2000, book: 2900))).Data!["id"].G();
        Assert.Equal(833.33m, (await Depreciate(api, "2026-01", asset)).Data!["totalAmount"].D()); // (12000 − 2000) ÷ 12
        Assert.Equal(66.67m, (await Depreciate(api, "2026-02", asset)).Data!["totalAmount"].D());  // المتبقي فوق التخريدية فقط
        Assert.Equal(2000, (await api.Get($"/fixedassets/{asset}")).Data!["currentBookValue"].D());
        Assert.Equal(400, (await Depreciate(api, "2026-03", asset)).Status); // بلغ القيمة التخريدية
    }

    [Fact]
    public async Task Reversing_a_depreciation_run_restores_the_asset_and_only_from_the_assets_screen()
    {
        var api = await NewTenantAsync();
        var center = await SeedCostCenterAsync(api, "MNT");
        var asset = (await api.Post("/fixedassets", Asset("FA-1", center, 12000, 10))).Data!["id"].G(); // 100 شهرياً
        var first = (await Depreciate(api, "2026-01", asset)).Data!["journalEntryId"].S();
        var second = (await Depreciate(api, "2026-02", asset)).Data!["journalEntryId"].S();

        // قيد الإهلاك يُدار من الأصول: لا يُعكس ولا يُحذف من شاشة القيود
        Assert.Equal(409, (await api.Post($"/journalentries/{second}/reverse")).Status);
        Assert.Equal(409, (await api.Delete($"/journalentries/{second}")).Status);
        Assert.Equal(409, (await api.Post($"/fixedassets/depreciation/{first}/reverse")).Status); // ليس آخر فترة

        Assert.Equal(200, (await api.Post($"/fixedassets/depreciation/{second}/reverse")).Status);
        var after = (await api.Get($"/fixedassets/{asset}")).Data!;
        Assert.Equal(11900, after["currentBookValue"].D()); Assert.Equal(100, after["accumulatedDepreciation"].D());
        Assert.Equal("2026-01", Assert.Single((await api.Get($"/fixedassets/depreciation?fixedAssetId={asset}")).Data!["items"]!.AsArray())!["period"].S());
        Assert.Equal(100, await BalanceAsync(api, "122"));
        Assert.Equal(200, (await Depreciate(api, "2026-02", asset)).Status); // تُرحَّل الفترة من جديد
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
    }

    [Fact]
    public async Task Disposing_an_asset_removes_it_from_the_books_and_books_the_gain_or_loss()
    {
        var api = await NewTenantAsync();
        var center = await SeedCostCenterAsync(api, "MNT");
        var asset = (await api.Post("/fixedassets", Asset("FA-1", center, 12000, 10))).Data!["id"].G();
        Assert.Equal(200, (await Depreciate(api, "2026-01", asset)).Status); // قيمة دفترية 11900

        Assert.Equal(400, (await api.Post($"/fixedassets/{asset}/dispose", new { date = "2026-02-15", proceeds = 9000 })).Status); // بيع بلا حساب خزينة
        Assert.Equal(400, (await api.Post($"/fixedassets/{asset}/dispose", new { date = "2025-12-01", proceeds = 0 })).Status);   // قبل الشراء
        var disposed = await api.Post($"/fixedassets/{asset}/dispose", new { date = "2026-02-15", proceeds = 9000, treasuryAccountCode = "1111" });
        Assert.Equal(200, disposed.Status);
        Assert.Equal(0, disposed.Data!["currentBookValue"].D()); Assert.Equal(9000, disposed.Data["disposalProceeds"].D());
        Assert.StartsWith("2026-02-15", disposed.Data["disposedAt"].S());

        var lines = (await api.Get($"/journalentries/{disposed.Data["disposalJournalEntryId"].S()}")).Data!["lines"]!.AsArray();
        decimal Debit(string code) => lines.Where(l => l!["accountCode"].S() == code).Sum(l => l!["debit"].D());
        decimal Credit(string code) => lines.Where(l => l!["accountCode"].S() == code).Sum(l => l!["credit"].D());
        Assert.Equal(100, Debit("122")); Assert.Equal(9000, Debit("1111")); Assert.Equal(2900, Debit("525")); // خسارة = 11900 − 9000
        Assert.Equal(12000, Credit("121"));
        Assert.Equal(center, lines.Single(l => l!["accountCode"].S() == "525")!["costCenterId"].G());

        // أصل مستبعد: لا إهلاك ولا تعديل ولا استبعاد ثانٍ، وقيده لا يُعكس من شاشة القيود
        Assert.Equal(400, (await Depreciate(api, "2026-02", asset)).Status);
        Assert.Equal(409, (await api.Put($"/fixedassets/{asset}", Asset("FA-1", center, 12000, 10))).Status);
        Assert.Equal(409, (await api.Post($"/fixedassets/{asset}/dispose", new { proceeds = 0 })).Status);
        Assert.Equal(409, (await api.Post($"/journalentries/{disposed.Data["disposalJournalEntryId"].S()}/reverse")).Status);

        // بيع بأعلى من القيمة الدفترية: ربح استبعاد
        var gainAsset = (await api.Post("/fixedassets", Asset("FA-2", center, 5000, 10, book: 1000))).Data!["id"].G();
        var sold = await api.Post($"/fixedassets/{gainAsset}/dispose", new { date = "2026-03-01", proceeds = 1500, treasuryAccountCode = "1111" });
        Assert.Equal(500, await BalanceAsync(api, "422"));
        Assert.Equal(200, sold.Status);
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
    }

    // ---------- ضريبة المصروف المباشر بسند صرف ----------
    [Fact]
    public async Task Payment_voucher_with_included_vat_books_input_vat_and_reaches_the_vat_return()
    {
        var api = await NewTenantAsync();
        var (_, supplierAccount) = await SeedSupplierAsync(api);
        object Payment(string account, decimal amount, decimal vat) => new { type = "payment", amount, vatAmount = vat, partyName = "مصروف كهرباء", partyAccountCode = account, treasuryAccountCode = "1111", paymentMethod = "cash" };

        Assert.Equal(400, (await api.Post("/vouchers", Payment("521", 115, 115))).Status);           // الضريبة ليست كل المبلغ
        Assert.Equal(400, (await api.Post("/vouchers", Payment(supplierAccount, 115, 15))).Status);  // سداد مورد: ضريبته في فاتورته

        var voucher = await api.Post("/vouchers", Payment("521", 115, 15));
        Assert.True(voucher.Success);
        Assert.Equal(100, await BalanceAsync(api, "521"));  // المصروف بالصافي
        Assert.Equal(15, await BalanceAsync(api, "1131"));  // ضريبة المدخلات
        Assert.Equal(-115, await BalanceAsync(api, "1111"));

        var from = DateTime.UtcNow.Date.AddDays(-1).ToString("yyyy-MM-dd"); var to = DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd");
        var vat = (await api.Get($"/reports/VatReturn?from={from}&to={to}")).Data!;
        Assert.Equal(100, vat["standardRatedPurchases"].D()); Assert.Equal(15, vat["inputVat"].D()); Assert.Equal(-15, vat["netVatPayable"].D());
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
    }

    // ---------- إيراد ومركز تكلفة لكل سطر ----------
    [Fact]
    public async Task Sales_invoice_posts_each_line_to_its_own_revenue_account_and_cost_center()
    {
        var api = await NewTenantAsync();
        var north = await SeedCostCenterAsync(api, "N"); var south = await SeedCostCenterAsync(api, "S");
        var invoice = await api.Post("/invoices", new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash",
            items = new object[]
            {
                new { itemName = "خدمة شمال", quantity = 1, unitPrice = 100, vatRate = 15, costCenterId = north, revenueAccountCode = "412" },
                new { itemName = "خدمة جنوب", quantity = 1, unitPrice = 200, vatRate = 15, costCenterId = south },
                new { itemName = "خدمة جنوب 2", quantity = 1, unitPrice = 50, vatRate = 15, costCenterId = south },
            },
        });
        Assert.Equal(201, invoice.Status);

        var lines = (await api.Get($"/journalentries/{invoice.Data!["journalEntryId"].S()}")).Data!["lines"]!.AsArray();
        JsonNode Revenue(string code) => lines.Single(l => l!["accountCode"].S() == code && l["credit"].D() > 0)!;
        Assert.Equal(100, Revenue("412")["credit"].D()); Assert.Equal(north, Revenue("412")["costCenterId"].G());
        Assert.Equal(250, Revenue("411")["credit"].D()); Assert.Equal(south, Revenue("411")["costCenterId"].G()); // سطرا الجنوب مجمَّعان
        Assert.Equal(400, (await api.Post("/invoices", new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash",
            items = new[] { new { itemName = "x", quantity = 1, unitPrice = 10, vatRate = 15, revenueAccountCode = "9999" } } })).Status); // حساب غير موجود
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
    }

    // ---------- تسوية المخزون اليدوية وقيد اقتناء الأصل وسجل التدقيق ----------
    [Fact]
    public async Task Manual_stock_adjustment_posts_its_value_to_the_ledger_and_edits_keep_it_in_step()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 0);
        object Adjust(string type, decimal qty, decimal cost, string? counter = null) => new { itemId = product, type, quantity = qty, unitCost = cost, referenceNumber = "ADJ", counterAccountCode = counter };

        // مخزون أول المدة مقابل الأرصدة الافتتاحية
        var opening = await api.Post("/stockmovements/adjust", Adjust("adjustment_in", 10, 40, "33"));
        Assert.Equal(200, opening.Status);
        Assert.NotNull(opening.Data!["journalEntryId"]);
        Assert.Equal(400, await BalanceAsync(api, "1141")); Assert.Equal(400, await BalanceAsync(api, "33"));

        // خصم تالف بمتوسط التكلفة على فروقات الجرد (الحساب الافتراضي)
        var damaged = await api.Post("/stockmovements/adjust", Adjust("adjustment_out", 2, 0));
        Assert.Equal(320, await BalanceAsync(api, "1141")); Assert.Equal(80, await BalanceAsync(api, "513"));
        var stock = (await api.Get($"/products/{product}")).Data!;
        Assert.Equal(await BalanceAsync(api, "1141"), stock["currentStock"].D() * stock["averageCost"].D()); // الدفاتر = قيمة المخزون

        // قيد التسوية يُدار من حركتها لا من شاشة القيود
        Assert.Equal(409, (await api.Post($"/journalentries/{damaged.Data!["journalEntryId"].S()}/reverse")).Status);

        // تعديل التسوية يعكس قيدها ويرحّل الجديد، وحذفها يعكسه
        Assert.Equal(200, (await api.Put($"/stockmovements/{opening.Data["id"].S()}", Adjust("adjustment_in", 10, 50, "33"))).Status);
        Assert.Equal(500, await BalanceAsync(api, "33"));
        Assert.Equal(200, (await api.Delete($"/stockmovements/{damaged.Data["id"].S()}")).Status);
        Assert.Equal(0, await BalanceAsync(api, "513"));
        Assert.Equal(500, await BalanceAsync(api, "1141"));
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
    }

    [Fact]
    public async Task Registering_an_asset_with_a_funding_account_posts_its_acquisition_entry()
    {
        var api = await NewTenantAsync();
        var center = await SeedCostCenterAsync(api, "MNT");
        object Funded(string code, decimal cost, decimal book, string? account) => new { assetCode = code, nameAr = "أصل " + code, nameEn = code, purchaseDate = "2026-01-10", purchaseCost = cost, currentBookValue = book, depreciationRate = 10, costCenterId = center, acquisitionAccountCode = account };

        // شراء نقدي: مدين الأصل، دائن الصندوق
        var bought = await api.Post("/fixedassets", Funded("FA-1", 12000, 12000, "1111"));
        Assert.Equal(201, bought.Status);
        Assert.NotNull(bought.Data!["acquisitionJournalEntryId"]);
        Assert.Equal(12000, await BalanceAsync(api, "121")); Assert.Equal(-12000, await BalanceAsync(api, "1111"));

        // أصل قائم يُدخَل بتكلفته ومجمع إهلاكه مقابل الأرصدة الافتتاحية بقيمته الدفترية
        Assert.Equal(201, (await api.Post("/fixedassets", Funded("FA-2", 5000, 3000, "33"))).Status);
        Assert.Equal(17000, await BalanceAsync(api, "121")); Assert.Equal(2000, await BalanceAsync(api, "122")); Assert.Equal(3000, await BalanceAsync(api, "33"));

        // بلا حساب تمويل: لا قيد (الاقتناء مقيَّد خارج شاشة الأصول)
        var manual = await api.Post("/fixedassets", Funded("FA-3", 900, 900, null));
        Assert.Null(manual.Data!["acquisitionJournalEntryId"]);
        Assert.Equal(17000, await BalanceAsync(api, "121"));

        // التكلفة المقيَّدة لا تُعدَّل من نموذج الأصل، وباقي البيانات تُعدَّل
        var id = bought.Data["id"].S();
        Assert.Equal(409, (await api.Put($"/fixedassets/{id}", Funded("FA-1", 15000, 15000, null))).Status);
        Assert.Equal(200, (await api.Put($"/fixedassets/{id}", Funded("FA-1", 12000, 12000, null))).Status);
        Assert.NotNull((await api.Get($"/fixedassets/{id}")).Data!["acquisitionJournalEntryId"]);

        // استبعاده الآن يصفّر حساب الأصل فعلاً
        Assert.Equal(200, (await api.Post($"/fixedassets/{id}/dispose", new { date = "2026-02-01", proceeds = 12000, treasuryAccountCode = "1111" })).Status);
        Assert.Equal(5000, await BalanceAsync(api, "121")); Assert.Equal(0, await BalanceAsync(api, "1111"));
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
    }

    [Fact]
    public async Task Invoices_vouchers_and_manual_entries_leave_an_audit_trail()
    {
        var api = await NewTenantAsync();
        var (customer, account) = await SeedCustomerAsync(api);
        var invoice = (await api.Post("/invoices", CreditSale(customer, 100))).Data!["id"].S();
        var voucher = (await api.Post("/vouchers", Receipt(account, 115, new { invoiceId = invoice, amount = 115 }))).Data!["id"].S();
        Assert.Equal(200, (await api.Delete($"/vouchers/{voucher}")).Status);
        var entry = (await api.Post("/journalentries", new { date = DateTime.UtcNow, description = "x",
            lines = new[] { new { accountCode = "1111", debit = 10m, credit = 0m }, new { accountCode = "31", debit = 0m, credit = 10m } } })).Data!["id"].S();
        Assert.Equal(200, (await api.Post($"/journalentries/{entry}/reverse")).Status);

        async Task<List<string>> Actions(string entity, string id) => (await api.Get($"/auditlogs?entityName={entity}&entityId={id}")).Data!["items"]!.AsArray().Select(x => x!["action"].S()).ToList();
        Assert.Contains("INVOICE_POSTED", await Actions("Invoice", invoice));
        var voucherActions = await Actions("Voucher", voucher);
        Assert.Contains("VOUCHER_CREATED", voucherActions); Assert.Contains("VOUCHER_DELETED", voucherActions);
        var entryActions = await Actions("JournalEntry", entry);
        Assert.Contains("JOURNAL_ENTRY_CREATED", entryActions); Assert.Contains("JOURNAL_ENTRY_REVERSED", entryActions);
    }

    [Fact]
    public async Task Balance_sheet_is_built_from_the_ledger_and_balances()
    {
        var api = await NewTenantAsync();
        var (customer, account) = await SeedCustomerAsync(api);
        Assert.Equal(201, (await api.Post("/invoices", CreditSale(customer, 1000))).Status);           // ذمم 1150، إيراد 1000، ضريبة 150
        Assert.True((await api.Post("/vouchers", Receipt(account, 400))).Success);                      // نقد 400
        Assert.Equal(200, (await api.Post("/journalentries", new { date = DateTime.UtcNow, description = "رأس المال",
            lines = new[] { new { accountCode = "1111", debit = 5000m, credit = 0m }, new { accountCode = "31", debit = 0m, credit = 5000m } } })).Status);

        var sheet = (await api.Get("/reports/BalanceSheet")).Data!;
        decimal Amount(string section, string code) => sheet[section]!.AsArray().Single(l => l!["accountCode"].S() == code)!["amount"].D();
        Assert.Equal(5400, Amount("assets", "1111")); Assert.Equal(750, Amount("assets", account));
        Assert.Equal(150, Amount("liabilities", "213")); Assert.Equal(5000, Amount("equity", "31"));
        Assert.Equal(6150, sheet["totalAssets"].D()); Assert.Equal(1000, sheet["netProfit"].D());
        Assert.Equal(6150, sheet["totalLiabilitiesAndEquity"].D());
        Assert.True(sheet["isBalanced"]!.GetValue<bool>());

        // حتى تاريخ سابق: لا شيء بعد
        var before = (await api.Get($"/reports/BalanceSheet?asOf={DateTime.UtcNow.AddDays(-5):yyyy-MM-dd}")).Data!;
        Assert.Equal(0, before["totalAssets"].D()); Assert.Empty(before["assets"]!.AsArray());
    }
}
