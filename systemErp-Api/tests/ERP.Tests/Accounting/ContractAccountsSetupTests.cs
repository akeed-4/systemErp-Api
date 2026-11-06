using ERP.Core.Models.Accounting;
using ERP.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Tests;

/// <summary>
/// تهيئة حسابات العقود: المنشأة الجديدة تُنشأ لها مربوطة؛ القائمة تُنشأ لها الناقصة فقط؛ الحساب القائم المناسب يُعاد استخدامه؛
/// التكرار لا يكرر؛ اختيار المستخدم لا يُمس (والمخالف يُبلَّغ عنه)؛ القيود السابقة لا تتغير؛ الفوترة تهيّئ الناقص تلقائياً.
/// </summary>
[Collection("api")]
public class ContractAccountsSetupTests : TestBase
{
    public ContractAccountsSetupTests(ErpFactory f) : base(f) { }

    /// <summary>دور ← (الرقم المتوقع في الشجرة الافتراضية، الأب، النوع).</summary>
    private static readonly (string Key, string Code, string Parent, string Type)[] DefaultLayout =
    {
        ("contractAdvancesReceivedAccountCode", "218", "21", "liability"),
        ("contractRetentionPayableAccountCode", "219", "21", "liability"),
        ("contractRetentionReceivableAccountCode", "1132", "113", "asset"),
        ("contractAdvancesPaidAccountCode", "1133", "113", "asset"),
        ("contractPenaltiesIncomeAccountCode", "424", "42", "revenue"),
        ("contractPenaltiesExpenseAccountCode", "532", "52", "expense"),
        ("contractOtherDeductionsAccountCode", "533", "52", "expense"),
    };

    private static readonly string[] SetupFields =
    {
        "ContractAdvancesReceivedAccountCode", "ContractRetentionPayableAccountCode", "ContractRetentionReceivableAccountCode",
        "ContractAdvancesPaidAccountCode", "ContractPenaltiesIncomeAccountCode", "ContractPenaltiesExpenseAccountCode", "ContractOtherDeductionsAccountCode",
    };

    private static Task<Res> Initialize(Client api) => api.Post("/accounting-settings/contract-accounts");

    private static async Task<string?> Setting(Client api, string key) => (await api.Get("/accounting-settings")).Data![key]?.GetValue<string?>();

    /// <summary>رصيد الحساب الختامي (مدين − دائن) من ميزان المراجعة.</summary>
    private static async Task<decimal> BalanceAsync(Client api, string code)
        => (await api.Get("/reports/TrialBalance")).Data!.AsArray()
            .Where(r => r!["accountCode"].S() == code).Sum(r => r!["closingDebit"].D() - r!["closingCredit"].D());

    private static string Status(Res report, string role)
        => report.Data!["items"]!.AsArray().Single(i => i!["role"].S() == role)!["status"].S();

    /// <summary>منشأة «قديمة»: بلا حسابات العقود المُهيَّأة ولا ربطها (كما كانت قبل التهيئة التلقائية).</summary>
    private async Task MakeLegacyAsync(Client api)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>();
        var settings = await db.Set<AccountingSettings>().IgnoreQueryFilters().SingleAsync(s => s.TenantId == api.TenantId);
        var codes = SetupFields.Select(f => (string?)typeof(AccountingSettings).GetProperty(f)!.GetValue(settings)).OfType<string>().ToList();
        foreach (var f in SetupFields) typeof(AccountingSettings).GetProperty(f)!.SetValue(settings, null);
        db.RemoveRange(await db.Set<Account>().IgnoreQueryFilters().Where(a => a.TenantId == api.TenantId && codes.Contains(a.Code)).ToListAsync());
        await db.SaveChangesAsync();
    }

    private async Task<int> CountAsync(Client api, Func<IQueryable<Account>, IQueryable<Account>> filter)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>();
        return await filter(db.Set<Account>().IgnoreQueryFilters().Where(a => a.TenantId == api.TenantId)).CountAsync();
    }

    private static async Task AssertDefaultLayoutAsync(Client api)
    {
        var settings = (await api.Get("/accounting-settings")).Data!;
        foreach (var (key, code, parent, type) in DefaultLayout)
        {
            Assert.Equal(code, settings[key].S());
            var account = (await api.Get($"/accounts/ByCode/{code}")).Data!;
            Assert.Equal(parent, account["parentCode"].S());
            Assert.Equal(type, account["type"].S().ToLowerInvariant());
            Assert.Equal(type is "asset" or "expense", account["isDebitNature"]!.GetValue<bool>()); // الطبيعة من النوع
        }
    }

    [Fact]
    public async Task New_company_gets_contract_accounts_under_matching_groups_linked_to_settings()
    {
        var api = await NewTenantAsync();
        await AssertDefaultLayoutAsync(api);

        // تشغيل التهيئة على منشأة جديدة: كل شيء «باقٍ» ولا حساب مكرر
        var report = await Initialize(api);
        Assert.Equal(200, report.Status);
        Assert.Equal(7, report.Data!["kept"]!.GetValue<int>());
        Assert.Equal(0, report.Data["created"]!.GetValue<int>());
        Assert.Equal(1, await CountAsync(api, q => q.Where(a => a.NameAr == "دفعات مقدمة مستلمة على العقود")));
    }

    [Fact]
    public async Task Existing_company_gets_only_missing_accounts_and_repeated_runs_do_not_duplicate()
    {
        var api = await NewTenantAsync();
        await MakeLegacyAsync(api);
        var accountsBefore = await CountAsync(api, q => q);

        var first = await Initialize(api);
        Assert.Equal(200, first.Status);
        Assert.Equal(7, first.Data!["created"]!.GetValue<int>());
        await AssertDefaultLayoutAsync(api);
        Assert.Equal(accountsBefore + 7, await CountAsync(api, q => q));

        var second = await Initialize(api);
        Assert.Equal(7, second.Data!["kept"]!.GetValue<int>());
        Assert.Equal(accountsBefore + 7, await CountAsync(api, q => q));

        // مُسح الربط بعد الإنشاء: يُعرف الحساب المُنشأ باسمه ولا يُنشأ ثانٍ
        Assert.Equal(200, (await api.Put("/accounting-settings", new { contractAdvancesReceivedAccountCode = "" })).Status);
        var third = await Initialize(api);
        Assert.Equal("reused", Status(third, "ContractAdvancesReceivedAccountCode"));
        Assert.Equal("218", await Setting(api, "contractAdvancesReceivedAccountCode"));
        Assert.Equal(accountsBefore + 7, await CountAsync(api, q => q));
    }

    [Fact]
    public async Task Compatible_existing_accounts_are_reused_and_incompatible_ones_are_not()
    {
        var api = await NewTenantAsync();
        await MakeLegacyAsync(api);
        // حساب المنشأة نفسها بأسماء معروفة لهذا الغرض وبأرقامها هي
        Assert.Equal(201, (await api.Post("/accounts", new { code = "2190", nameAr = "دفعات مقدمة من العملاء", nameEn = "Customer advances", type = "liability", parentCode = "21", isDebitNature = false })).Status);
        Assert.Equal(201, (await api.Post("/accounts", new { code = "1139", nameAr = "حساب", nameEn = "Retention Receivable", type = "asset", parentCode = "113", isDebitNature = true })).Status);
        // الاسم يطابق لكن النوع لا يناسب الدور (مصروف لدور أصل): لا يُستخدم
        Assert.Equal(201, (await api.Post("/accounts", new { code = "5999", nameAr = "دفعات مقدمة للموردين", nameEn = "x", type = "expense", parentCode = "52", isDebitNature = true })).Status);

        var report = await Initialize(api);
        Assert.Equal(200, report.Status);
        Assert.Equal("reused", Status(report, "ContractAdvancesReceivedAccountCode"));
        Assert.Equal("2190", await Setting(api, "contractAdvancesReceivedAccountCode"));
        Assert.Equal("reused", Status(report, "ContractRetentionReceivableAccountCode"));
        Assert.Equal("1139", await Setting(api, "contractRetentionReceivableAccountCode"));
        Assert.Equal("created", Status(report, "ContractAdvancesPaidAccountCode"));
        Assert.NotEqual("5999", await Setting(api, "contractAdvancesPaidAccountCode"));
        Assert.Equal(0, await CountAsync(api, q => q.Where(a => a.NameAr == "دفعات مقدمة مستلمة على العقود")));

        // الحساب المُعاد استخدامه لم يُعدَّل
        var reused = (await api.Get("/accounts/ByCode/2190")).Data!;
        Assert.Equal("دفعات مقدمة من العملاء", reused["nameAr"].S());
        Assert.Equal("21", reused["parentCode"].S());
    }

    [Fact]
    public async Task Ambiguous_candidates_are_reported_without_guessing()
    {
        var api = await NewTenantAsync();
        await MakeLegacyAsync(api);
        Assert.Equal(201, (await api.Post("/accounts", new { code = "2190", nameAr = "دفعات مقدمة من العملاء", nameEn = "a", type = "liability", parentCode = "21", isDebitNature = false })).Status);
        Assert.Equal(201, (await api.Post("/accounts", new { code = "2191", nameAr = "b", nameEn = "Customer Advances", type = "liability", parentCode = "21", isDebitNature = false })).Status);

        var report = await Initialize(api);
        var item = report.Data!["items"]!.AsArray().Single(i => i!["role"].S() == "ContractAdvancesReceivedAccountCode")!;
        Assert.Equal("problem", item["status"].S());
        Assert.Contains("2190", item["message"].S());
        Assert.Null(await Setting(api, "contractAdvancesReceivedAccountCode"));
        Assert.Equal(0, await CountAsync(api, q => q.Where(a => a.NameAr == "دفعات مقدمة مستلمة على العقود")));
    }

    [Fact]
    public async Task User_mappings_are_preserved_and_invalid_ones_are_reported_not_replaced()
    {
        var api = await NewTenantAsync();
        await MakeLegacyAsync(api);
        Assert.Equal(201, (await api.Post("/accounts", new { code = "2195", nameAr = "حساب يختاره المستخدم", nameEn = "Own", type = "liability", parentCode = "21", isDebitNature = false })).Status);
        Assert.Equal(200, (await api.Put("/accounting-settings", new { contractAdvancesReceivedAccountCode = "2195" })).Status);

        // إعداد غير صالح وصل للقاعدة (حساب تجميعي): يُبلَّغ عنه ولا يُستبدل
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>();
            var s = await db.Set<AccountingSettings>().IgnoreQueryFilters().SingleAsync(x => x.TenantId == api.TenantId);
            s.ContractRetentionPayableAccountCode = "21";
            await db.SaveChangesAsync();
        }

        var report = await Initialize(api);
        Assert.Equal("kept", Status(report, "ContractAdvancesReceivedAccountCode"));
        Assert.Equal("2195", await Setting(api, "contractAdvancesReceivedAccountCode"));
        Assert.Equal("problem", Status(report, "ContractRetentionPayableAccountCode"));
        Assert.Equal("21", await Setting(api, "contractRetentionPayableAccountCode"));
        Assert.Equal(0, await CountAsync(api, q => q.Where(a => a.NameAr == "دفعات مقدمة مستلمة على العقود")));
        Assert.Equal(0, await CountAsync(api, q => q.Where(a => a.NameAr == "محتجزات ضمان مستحقة لمقاولي الباطن")));
    }

    [Fact]
    public async Task Posted_history_is_untouched_and_billing_initializes_missing_accounts_for_customer_and_supplier_contracts()
    {
        var api = await NewTenantAsync();
        await MakeLegacyAsync(api);
        var (customer, _) = await SeedCustomerAsync(api, creditLimit: 1_000_000);
        var (supplier, _) = await SeedSupplierAsync(api);
        Assert.Equal(200, (await api.Post("/journalentries", new { date = DateTime.UtcNow, description = "رأس المال",
            lines = new[] { new { accountCode = "1111", debit = 5000m, credit = 0m }, new { accountCode = "31", debit = 0m, credit = 5000m } } })).Status);
        var before = (await api.Get("/reports/TrialBalance")).Data!.ToJsonString();

        // التهيئة لا تمس القيود
        Assert.Equal(200, (await Initialize(api)).Status);
        Assert.Equal(before, (await api.Get("/reports/TrialBalance")).Data!.ToJsonString());

        // منشأة قديمة لم تُهيَّأ: أول فوترة عقد تهيّئ الناقص وتُرحّل عليه
        await MakeLegacyAsync(api);
        async Task<string> ActiveBoqAsync(string partyType, Guid party)
        {
            var created = await api.Post("/commercialcontracts", new
            {
                title = "عقد", contractType = "construction_boq", workflow = "simple", requiresGuarantee = false,
                partyType, partyId = party, partyName = "طرف", startDate = DateTime.UtcNow.Date, endDate = DateTime.UtcNow.Date.AddMonths(3),
                vatRate = 15, advancePercent = 10, retentionPercent = 5,
                items = new[] { new { description = "بند", unit = "م", quantity = 100, unitPrice = 100, vatRate = 15, lineKind = "measured" } },
                milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
            });
            Assert.True(created.Status == 201, created.Body?.ToJsonString());
            var id = created.Data!["id"].S();
            var active = await api.Post($"/commercialcontracts/{id}/move", new { toStatus = "active" });
            Assert.True(active.Status == 200, active.Body?.ToJsonString());
            return id;
        }

        var customerContract = await ActiveBoqAsync("customer", customer);
        var advance = await api.Post($"/commercialcontracts/{customerContract}/advance-invoice");
        Assert.True(advance.Status == 200, advance.Body?.ToJsonString());
        Assert.Equal("218", await Setting(api, "contractAdvancesReceivedAccountCode"));
        Assert.Equal(-1000m, await BalanceAsync(api, "218")); // دائن 1,000 (التزام)

        var supplierContract = await ActiveBoqAsync("supplier", supplier);
        var paid = await api.Post($"/commercialcontracts/{supplierContract}/advance-invoice");
        Assert.True(paid.Status == 200, paid.Body?.ToJsonString());
        Assert.Equal("1133", await Setting(api, "contractAdvancesPaidAccountCode"));
        Assert.Equal(1000m, await BalanceAsync(api, "1133")); // مدين 1,000 (أصل)

        var (d, c) = Totals(await api.Get("/reports/TrialBalance"));
        Assert.Equal(d, c);
    }
}
