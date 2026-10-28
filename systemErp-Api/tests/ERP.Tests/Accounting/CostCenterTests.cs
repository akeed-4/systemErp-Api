using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// مراكز التكلفة: إعداد الحساب (غير مستخدم / اختياري / إلزامي) على الخادم، العزل بين المنشآت، توزيع قيود المستندات على مراكز أسطرها،
/// المرتجعات والسندات والأصول، ثبات القيود التاريخية، الإقفال، الهرم، وتقارير المراكز.
/// </summary>
[Collection("api")]
public class CostCenterTests : TestBase
{
    public CostCenterTests(ErpFactory f) : base(f) { }

    private static readonly string Today = DateTime.UtcNow.ToString("yyyy-MM-dd");
    private static readonly string MonthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).ToString("yyyy-MM-dd");

    private static async Task<Guid> CenterAsync(Client api, string code, Guid? parentId = null, bool isActive = true)
    {
        var created = await api.Post("/costcenters", new { code, nameAr = "مركز " + code, nameEn = code, isActive, parentId });
        Assert.Equal(201, created.Status);
        return created.Data!["id"].G();
    }

    private static Task<Res> UpdateCenterAsync(Client api, Guid id, string code, Guid? parentId = null, bool isActive = true)
        => api.Put($"/costcenters/{id}", new { code, nameAr = "مركز " + code, nameEn = code, isActive, parentId });

    private static async Task<Res> SetPolicyAsync(Client api, string code, string policy)
    {
        var account = (await api.Get($"/accounts/ByCode/{code}")).Data!.AsObject();
        account["costCenterPolicy"] = policy;
        return await api.Put($"/accounts/{account["id"].S()}", account);
    }

    private static object L(string account, decimal debit, decimal credit, Guid? center = null)
        => new { accountCode = account, debit, credit, costCenterId = center };

    private static Task<Res> Manual(Client api, params object[] lines) => Manual(api, DateTime.UtcNow, lines);
    private static Task<Res> Manual(Client api, DateTime date, params object[] lines) => api.Post("/journalentries", new { date, description = "قيد", lines });

    private static object Service(string name, decimal price, Guid? center = null) => new { itemName = name, quantity = 1, unitPrice = price, vatRate = 15, costCenterId = center };
    private static object Sale(Guid? header, params object[] items) => Sale(DateTime.UtcNow, header, items);
    private static object Sale(DateTime date, Guid? header, params object[] items)
        => new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", issueDate = date, costCenterId = header, items };

    private static async Task<JsonArray> EntryLinesAsync(Client api, string entryId) => (await api.Get($"/journalentries/{entryId}")).Data!["lines"]!.AsArray();
    private static Guid? CenterOf(JsonNode? line) => line?["costCenterId"] is { } c ? Guid.Parse(c.GetValue<string>()) : null;
    private static decimal Amount(JsonArray lines, string account, Guid? center, string side)
        => lines.Where(l => l!["accountCode"].S() == account && CenterOf(l) == center).Sum(l => l![side].D());

    private static async Task AssertBalancedAsync(Client api)
    {
        var (d, c) = Totals(await api.Get("/reports/TrialBalance"));
        Assert.Equal(d, c);
    }

    // ---------- إعداد الحساب ----------
    [Fact]
    public async Task Account_policy_makes_the_cost_center_optional_required_or_unused_on_manual_entries()
    {
        var api = await NewTenantAsync();
        var center = await CenterAsync(api, "ADM");

        // اختياري افتراضياً (سلوك الحسابات القائمة): بمركز وبدونه
        Assert.Equal("optional", (await api.Get("/accounts/ByCode/521")).Data!["costCenterPolicy"].S());
        Assert.Equal(200, (await Manual(api, L("521", 100, 0), L("1111", 0, 100))).Status);
        Assert.Equal(200, (await Manual(api, L("521", 100, 0, center), L("1111", 0, 100))).Status);

        // إلزامي: السطر بلا مركز يُرفض برسالة تسمّي الحساب، وبمركز يُحفظ مركزه على السطر
        Assert.Equal(200, (await SetPolicyAsync(api, "521", "required")).Status);
        var missing = await Manual(api, L("521", 50, 0), L("1111", 0, 50));
        Assert.Equal(400, missing.Status);
        Assert.Contains("521", missing.Body!["message"].S());
        var ok = await Manual(api, L("521", 50, 0, center), L("1111", 0, 50));
        Assert.Equal(200, ok.Status);
        Assert.Equal(center, CenterOf((await EntryLinesAsync(api, ok.Data!["id"].S())).Single(l => l!["accountCode"].S() == "521")));

        // غير مستخدم: مركز على سطر الحساب في القيد اليدوي خطأ صريح
        Assert.Equal(200, (await SetPolicyAsync(api, "1111", "none")).Status);
        Assert.Equal(400, (await Manual(api, L("521", 10, 0, center), L("1111", 0, 10, center))).Status);
        Assert.Equal(200, (await Manual(api, L("521", 10, 0, center), L("1111", 0, 10))).Status);

        // تعديل الحساب من عميل لا يرسل الإعداد لا يمسحه، والقيمة غير المعروفة تُرفض
        var account = (await api.Get("/accounts/ByCode/521")).Data!.AsObject();
        account.Remove("costCenterPolicy");
        Assert.Equal(200, (await api.Put($"/accounts/{account["id"].S()}", account)).Status);
        Assert.Equal("required", (await api.Get("/accounts/ByCode/521")).Data!["costCenterPolicy"].S());
        Assert.Equal(400, (await SetPolicyAsync(api, "521", "sometimes")).Status);
        await AssertBalancedAsync(api);
    }

    // ---------- العزل بين المنشآت والحالة ----------
    [Fact]
    public async Task Only_active_cost_centers_of_the_same_company_can_be_posted_to_or_reported_on()
    {
        var api = await NewTenantAsync();
        var other = await NewTenantAsync("شركة أخرى");
        var foreign = await CenterAsync(other, "X");
        var inactive = await CenterAsync(api, "OFF", isActive: false);

        Assert.Equal(400, (await Manual(api, L("521", 10, 0, foreign), L("1111", 0, 10))).Status);
        Assert.Equal(400, (await Manual(api, L("521", 10, 0, inactive), L("1111", 0, 10))).Status);
        Assert.Equal(400, (await Manual(api, L("521", 10, 0, Guid.NewGuid()), L("1111", 0, 10))).Status);
        Assert.Equal(400, (await api.PostInvoice(Sale(foreign, Service("خدمة", 100)))).Status);           // مركز الترويسة
        Assert.Equal(400, (await api.PostInvoice(Sale(null, Service("خدمة", 100, inactive)))).Status);    // مركز السطر
        Assert.Equal(400, (await api.Post("/vouchers", new { type = "payment", amount = 50, partyName = "مصروف", partyAccountCode = "521",
            treasuryAccountCode = "1111", paymentMethod = "cash", costCenterId = foreign })).Status);

        // لا قراءة ولا تبعية ولا تقرير على مركز منشأة أخرى
        Assert.Equal(404, (await api.Get($"/costcenters/{foreign}")).Status);
        Assert.Equal(400, (await api.Post("/costcenters", new { code = "C", nameAr = "فرعي", nameEn = "Child", isActive = true, parentId = foreign })).Status);
        Assert.Equal(404, (await api.Get($"/financialstatements/CostCenterPerformance?from={MonthStart}&to={Today}&costCenterId={foreign}")).Status);
        Assert.Equal(404, (await api.Get($"/financialstatements/IncomeStatement?from={MonthStart}&to={Today}&costCenterId={foreign}")).Status);
        Assert.Empty((await api.Get("/reports/JournalLedger")).Data!.AsArray()); // لم يُرحَّل شيء
    }

    // ---------- الفواتير: توزيع القيد على مراكز الأسطر ----------
    [Fact]
    public async Task Sales_invoice_posts_revenue_and_cost_of_sales_to_each_line_center_with_the_header_as_default()
    {
        var api = await NewTenantAsync();
        var a = await CenterAsync(api, "A"); var b = await CenterAsync(api, "B");
        var p1 = await SeedProductAsync(api, "P1"); var p2 = await SeedProductAsync(api, "P2"); // تكلفة 40 لكل قطعة

        var sale = await api.PostInvoice(Sale(b,
            new { itemId = p1, quantity = 2, unitPrice = 100, vatRate = 15, costCenterId = (Guid?)a },
            new { itemId = p2, quantity = 1, unitPrice = 100, vatRate = 15, costCenterId = (Guid?)null }));
        Assert.Equal(201, sale.Status);
        // المركز الفعلي محفوظ على السطر (الافتراضي نُسخ للسطر الفارغ)
        Assert.Equal(new Guid?[] { a, b }, sale.Data!["items"]!.AsArray().Select(CenterOf).ToArray());

        var lines = await EntryLinesAsync(api, sale.Data["journalEntryId"].S());
        Assert.Equal(200, Amount(lines, "411", a, "credit")); Assert.Equal(100, Amount(lines, "411", b, "credit"));
        Assert.Equal(80, Amount(lines, "511", a, "debit")); Assert.Equal(40, Amount(lines, "511", b, "debit"));
        Assert.Equal(80, Amount(lines, "1141", a, "credit")); Assert.Equal(40, Amount(lines, "1141", b, "credit"));
        // النقد والضريبة لا يُنسبان لمركز
        Assert.Equal(345, Amount(lines, "1111", null, "debit")); Assert.Equal(45, Amount(lines, "213", null, "credit"));
        Assert.Equal(lines.Sum(l => l!["debit"].D()), lines.Sum(l => l!["credit"].D()));
        await AssertBalancedAsync(api);
    }

    [Fact]
    public async Task Purchase_invoice_posts_inventory_and_services_to_their_own_line_centers_not_the_first_one()
    {
        var api = await NewTenantAsync();

        var (supplier, _) = await SeedSupplierAsync(api); // فاتورة الشراء الضريبية تتطلب مورداً
        var a = await CenterAsync(api, "A"); var b = await CenterAsync(api, "B");
        var product = await SeedProductAsync(api, stock: 0);
        object Purchase() => new
        {
            kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "cash", partyId = supplier,
            items = new object[]
            {
                new { itemId = product, quantity = 5, unitPrice = 40, vatRate = 15, costCenterId = a },
                new { itemName = "صيانة", quantity = 1, unitPrice = 100, vatRate = 15, costCenterId = b },
            },
        };

        var purchase = await api.PostInvoice(Purchase());
        Assert.Equal(201, purchase.Status);
        var lines = await EntryLinesAsync(api, purchase.Data!["journalEntryId"].S());
        Assert.Equal(200, Amount(lines, "1141", a, "debit"));
        Assert.Equal(100, Amount(lines, "524", b, "debit")); // كان يُنسب لمركز أول سطر
        Assert.Equal(0, Amount(lines, "524", a, "debit"));
        Assert.Equal(345, Amount(lines, "1111", null, "credit"));

        // حساب المخزون «غير مستخدم»: يُسقط مركزه من قيد المستند ويبقى على المصروف
        Assert.Equal(200, (await SetPolicyAsync(api, "1141", "none")).Status);
        var second = await api.PostInvoice(Purchase());
        Assert.Equal(201, second.Status);
        lines = await EntryLinesAsync(api, second.Data!["journalEntryId"].S());
        Assert.Equal(200, Amount(lines, "1141", null, "debit"));
        Assert.Equal(100, Amount(lines, "524", b, "debit"));

        // حساب الخدمات «إلزامي»: سطر خدمة بلا مركز يرفض الفاتورة كلها
        Assert.Equal(200, (await SetPolicyAsync(api, "524", "required")).Status);
        Assert.Equal(400, (await api.PostInvoice(new { kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "cash", partyId = supplier,
            items = new[] { new { itemName = "شحن", quantity = 1, unitPrice = 50, vatRate = 15 } } })).Status);
        await AssertBalancedAsync(api);
    }

    // ---------- المرتجعات ----------
    [Fact]
    public async Task Return_reverses_the_original_line_centers_even_after_the_center_is_deactivated()
    {
        var api = await NewTenantAsync();
        var a = await CenterAsync(api, "A");
        var product = await SeedProductAsync(api);
        var sale = await api.PostInvoice(Sale(null, new { itemId = product, quantity = 2, unitPrice = 100, vatRate = 15, costCenterId = a }));
        Assert.Equal(201, sale.Status);

        Assert.Equal(200, (await UpdateCenterAsync(api, a, "A", isActive: false)).Status);
        Assert.Equal(400, (await api.PostInvoice(Sale(a, Service("جديدة", 10)))).Status); // لا ترحيل جديد على مركز موقوف

        var ret = await api.PostReturn(new { originalInvoiceId = sale.Data!["id"].S(), returnReason = "تالف" });
        Assert.Equal(200, ret.Status);
        Assert.Equal(a, CenterOf(ret.Data!["items"]![0]));
        var lines = await EntryLinesAsync(api, ret.Data["journalEntryId"].S());
        Assert.Equal(200, Amount(lines, "411", a, "debit"));
        Assert.Equal(80, Amount(lines, "1141", a, "debit")); Assert.Equal(80, Amount(lines, "511", a, "credit"));

        var performance = (await api.Get($"/financialstatements/CostCenterPerformance?from={MonthStart}&to={Today}&costCenterId={a}")).Data!;
        var row = performance["rows"]!.AsArray().Single()!;
        Assert.Equal(0, row["revenue"].D()); Assert.Equal(0, row["costOfSales"].D());
        await AssertBalancedAsync(api);
    }

    // ---------- السندات ----------
    [Fact]
    public async Task Expense_voucher_tags_only_the_expense_line_with_its_cost_center()
    {
        var api = await NewTenantAsync();
        var a = await CenterAsync(api, "A");
        var voucher = await api.Post("/vouchers", new { type = "payment", amount = 115, vatAmount = 15, partyName = "كهرباء", partyAccountCode = "521",
            treasuryAccountCode = "1111", paymentMethod = "cash", costCenterId = a });
        Assert.True(voucher.Success);
        Assert.Equal(a, CenterOf(voucher.Data));

        var lines = await EntryLinesAsync(api, voucher.Data!["journalEntryId"].S());
        Assert.Equal(100, Amount(lines, "521", a, "debit"));
        Assert.Equal(15, Amount(lines, "1131", null, "debit")); Assert.Equal(115, Amount(lines, "1111", null, "credit"));
        await AssertBalancedAsync(api);
    }

    // ---------- الأصول الثابتة وثبات القيود التاريخية ----------
    private static object Asset(Guid center) => new { assetCode = "FA-1", nameAr = "أصل", nameEn = "Asset", purchaseDate = "2026-01-10", purchaseCost = 12000,
        currentBookValue = 12000, depreciationRate = 10, salvageValue = 0, costCenterId = center };

    [Fact]
    public async Task Changing_an_asset_center_or_account_settings_later_does_not_rewrite_posted_depreciation()
    {
        var api = await NewTenantAsync();
        var a = await CenterAsync(api, "A"); var b = await CenterAsync(api, "B");
        var asset = (await api.Post("/fixedassets", Asset(a))).Data!["id"].G();
        var first = await api.Post("/fixedassets/depreciation/post", new { period = "2026-01", assetIds = new[] { asset } });
        Assert.Equal(200, first.Status);
        var firstEntry = first.Data!["journalEntryId"].S();

        // المركز الافتراضي للأصل يتغيّر، ومجمع الإهلاك يصبح «غير مستخدم» ومصروفه «إلزامي»
        Assert.Equal(200, (await api.Put($"/fixedassets/{asset}", Asset(b))).Status);
        Assert.Equal(200, (await SetPolicyAsync(api, "122", "none")).Status);
        Assert.Equal(200, (await SetPolicyAsync(api, "523", "required")).Status);

        var old = await EntryLinesAsync(api, firstEntry); // القيد المرحّل كما هو
        Assert.Equal(100, Amount(old, "523", a, "debit")); Assert.Equal(100, Amount(old, "122", a, "credit"));

        var second = await api.Post("/fixedassets/depreciation/post", new { period = "2026-02", assetIds = new[] { asset } });
        Assert.Equal(200, second.Status);
        var lines = await EntryLinesAsync(api, second.Data!["journalEntryId"].S());
        Assert.Equal(100, Amount(lines, "523", b, "debit")); Assert.Equal(100, Amount(lines, "122", null, "credit"));

        // تقرير المركز القديم يبقى بحركته التاريخية
        var income = (await api.Get($"/financialstatements/IncomeStatement?from=2026-01-01&to=2026-01-31&costCenterId={a}")).Data!;
        Assert.Equal(100, income["operatingExpenses"]!["total"].D());
        await AssertBalancedAsync(api);
    }

    [Fact]
    public async Task Header_default_is_stored_per_line_and_reversals_mirror_the_original_after_settings_change()
    {
        var api = await NewTenantAsync();
        var a = await CenterAsync(api, "A"); var b = await CenterAsync(api, "B");
        var first = await api.PostInvoice(Sale(a, Service("خدمة", 100)));
        Assert.Equal(201, first.Status);
        Assert.Equal(201, (await api.PostInvoice(Sale(b, Service("خدمة", 100)))).Status); // الافتراضي تغيّر في المستند التالي
        Assert.Equal(a, CenterOf((await api.Get(await api.InvoicePath(first.Data!["id"].S()))).Data!["items"]![0]));
        Assert.Equal(100, Amount(await EntryLinesAsync(api, first.Data["journalEntryId"].S()), "411", a, "credit"));

        // قيد يدوي بلا مركز، ثم يصبح حسابه إلزامياً: عكسه يبقى ممكناً ومرآة لأصله
        var manual = await Manual(api, L("521", 70, 0), L("1111", 0, 70));
        Assert.Equal(200, (await SetPolicyAsync(api, "521", "required")).Status);
        var reversal = await api.Post($"/journalentries/{manual.Data!["id"].S()}/reverse");
        Assert.Equal(200, reversal.Status);
        Assert.Equal(70, Amount(await EntryLinesAsync(api, reversal.Data!["id"].S()), "521", null, "credit"));
        Assert.Equal(409, (await api.Post($"/journalentries/{manual.Data["id"].S()}/reverse")).Status); // لا عكس مكرر
        await AssertBalancedAsync(api);
    }

    // ---------- التوازن ومنع الترحيل المكرر ----------
    [Fact]
    public async Task Split_entries_stay_balanced_and_a_draft_is_posted_only_once()
    {
        var api = await NewTenantAsync();
        var a = await CenterAsync(api, "A"); var b = await CenterAsync(api, "B");
        var draft = await api.PostInvoice(new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "draft", costCenterId = a,
            items = new object[] { Service("أ", 33.33m), Service("ب", 66.67m, b), Service("ج", 0.01m) } });
        Assert.Equal("draft", draft.Data!["status"].S());
        var path = await api.InvoicePath(draft.Data["id"].S(), "/post");

        var posted = await api.Post(path);
        Assert.Equal("posted", posted.Data!["status"].S());
        Assert.Equal(409, (await api.Post(path)).Status);

        var lines = await EntryLinesAsync(api, posted.Data["journalEntryId"].S());
        Assert.Equal(lines.Sum(l => l!["debit"].D()), lines.Sum(l => l!["credit"].D()));
        Assert.Equal(33.34m, Amount(lines, "411", a, "credit")); Assert.Equal(66.67m, Amount(lines, "411", b, "credit"));
        var entries = (await api.Get("/reports/JournalLedger")).Data!.AsArray().Select(r => r!["entryId"].S()).Distinct();
        Assert.Single(entries); // قيد واحد للمستند
        await AssertBalancedAsync(api);
    }

    // ---------- إقفال السنة ----------
    [Fact]
    public async Task Year_closing_closes_each_center_even_when_accounts_became_required_or_centers_inactive()
    {
        var api = await NewTenantAsync();
        var lastYear = DateTime.UtcNow.Year - 1;
        var a = await CenterAsync(api, "A");
        Assert.Equal(201, (await api.PostInvoice(Sale(new DateTime(lastYear, 3, 10), a, Service("خدمة", 1000)))).Status);
        Assert.Equal(200, (await Manual(api, new DateTime(lastYear, 6, 1), L("521", 300, 0), L("1111", 0, 300))).Status); // بلا مركز

        Assert.Equal(200, (await SetPolicyAsync(api, "521", "required")).Status);
        Assert.Equal(200, (await UpdateCenterAsync(api, a, "A", isActive: false)).Status);
        var closed = await api.Post("/fiscalperiods/close-year", new { year = lastYear });
        Assert.Equal(200, closed.Status);

        var lines = await EntryLinesAsync(api, closed.Data!["journalEntryId"].S());
        Assert.Equal(1000, Amount(lines, "411", a, "debit"));
        Assert.Equal(300, Amount(lines, "521", null, "credit"));
        // تقرير الأداء يستبعد قيد الإقفال كقائمة الدخل
        var performance = (await api.Get($"/financialstatements/CostCenterPerformance?from={lastYear}-01-01&to={lastYear}-12-31")).Data!;
        Assert.Equal(1000, performance["rows"]!.AsArray().Single()!["revenue"].D());
        Assert.Equal(300, performance["unassigned"]!["operatingExpenses"].D());
        await AssertBalancedAsync(api);
    }

    // ---------- الهرم والتقارير ----------
    [Fact]
    public async Task Hierarchy_is_validated_and_reports_filter_a_center_with_its_sub_centers()
    {
        var api = await NewTenantAsync();
        var parent = await CenterAsync(api, "P");
        var north = await CenterAsync(api, "N", parent); var south = await CenterAsync(api, "S", parent);

        Assert.Equal(400, (await UpdateCenterAsync(api, parent, "P", parentId: north)).Status);  // دورة
        Assert.Equal(400, (await UpdateCenterAsync(api, parent, "P", parentId: parent)).Status); // نفسه
        var blocked = await api.Delete($"/costcenters/{parent}");
        Assert.Equal(409, blocked.Status);
        Assert.Equal(parent, (await api.Get($"/costcenters/{north}")).Data!["parentId"].G());

        Assert.Equal(201, (await api.PostInvoice(Sale(null, Service("شمال", 100, north), Service("جنوب", 50, south), Service("عام", 30)))).Status);
        Assert.Equal(200, (await Manual(api, L("521", 40, 0, north), L("1111", 0, 40))).Status);

        // قائمة دخل المركز الأب تشمل فروعه، والفرع وحده
        var income = (await api.Get($"/financialstatements/IncomeStatement?from={MonthStart}&to={Today}&costCenterId={parent}")).Data!;
        Assert.Equal(150, income["revenue"]!["total"].D()); Assert.Equal(40, income["operatingExpenses"]!["total"].D()); Assert.Equal(110, income["netProfit"]!["total"].D());
        Assert.Equal(50, (await api.Get($"/financialstatements/IncomeStatement?from={MonthStart}&to={Today}&costCenterId={south}")).Data!["revenue"]!["total"].D());
        Assert.Equal(180, (await api.Get($"/financialstatements/IncomeStatement?from={MonthStart}&to={Today}")).Data!["revenue"]!["total"].D());

        // الأداء: صف لكل مركز، وغير الموزَّع منفصل، ولا مجاميع مدين/دائن توحي بالتوازن
        var all = (await api.Get($"/financialstatements/CostCenterPerformance?from={MonthStart}&to={Today}")).Data!;
        JsonNode Row(JsonNode report, Guid id) => report["rows"]!.AsArray().Single(r => r!["costCenterId"].G() == id)!;
        Assert.Equal(100, Row(all, north)["revenue"].D()); Assert.Equal(40, Row(all, north)["operatingExpenses"].D()); Assert.Equal(60, Row(all, north)["netResult"].D());
        Assert.Equal(30, all["unassigned"]!["revenue"].D());
        Assert.Equal(150, all["totalRevenue"].D()); Assert.Equal(110, all["netResult"].D());
        var scoped = (await api.Get($"/financialstatements/CostCenterPerformance?from={MonthStart}&to={Today}&costCenterId={parent}")).Data!;
        Assert.Equal(3, scoped["rows"]!.AsArray().Count); Assert.Equal(0, Row(scoped, parent)["revenue"].D()); Assert.Null(scoped["unassigned"]);

        // كشف الحساب ودفتر اليومية بمرشّح المركز
        var statement = (await api.Get($"/reports/AccountStatement/411?costCenterId={north}")).Data!;
        Assert.Equal(100, statement["closingBalance"].D());
        Assert.Equal("N", statement["entries"]![0]!["costCenterCode"].S());
        var ledger = (await api.Get($"/reports/JournalLedger?costCenterId={parent}")).Data!.AsArray();
        Assert.Equal(3, ledger.Count);
        Assert.All(ledger, r => Assert.Contains(r!["costCenterCode"].S(), new[] { "N", "S" }));

        // ميزان المراجعة لا يُرشَّح بالمركز ويبقى متوازناً
        await AssertBalancedAsync(api);
    }
}
