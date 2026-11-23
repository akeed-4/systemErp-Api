using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// حسابا الأصل الثابت المخصصان: يُنشآن معه تحت مجمع الإهلاك (122) ومصروف الإهلاك (523) ومعرّفاهما على الأصل، تسمية الأصل تعيد
/// تسميتهما دون تغيير الرمز، وحذف الأصل يحذفهما ما لم يكن له أثر في الدفاتر، ولا يُحذفان منفردين من شجرة الحسابات.
/// </summary>
[Collection("api")]
public class FixedAssetAccountsTests : TestBase
{
    public FixedAssetAccountsTests(ErpFactory f) : base(f) { }

    private static async Task<Guid> CenterAsync(Client api)
    {
        var c = await api.Post("/costcenters", new { code = "MNT", nameAr = "الصيانة", nameEn = "MNT", isActive = true });
        Assert.Equal(201, c.Status);
        return c.Data!["id"].G();
    }

    private static object Asset(string code, Guid center, string name = "سيارة", decimal cost = 12000, string? acquisition = null)
        => new { assetCode = code, nameAr = name, nameEn = "Car", purchaseDate = "2026-01-10", purchaseCost = cost, currentBookValue = cost, depreciationRate = 10, costCenterId = center, acquisitionAccountCode = acquisition };

    private static async Task<JsonNode> AccountAsync(Client api, string id) => (await api.Get($"/accounts/{id}")).Data!;

    private static async Task<int> ChildCountAsync(Client api, string parentCode)
        => (await api.Get("/accounts/tree")).Data!.AsArray().SelectMany(Flatten).Count(a => a["parentCode"].S() == parentCode);

    private static IEnumerable<JsonNode> Flatten(JsonNode? node)
    {
        if (node == null) yield break;
        yield return node;
        foreach (var child in node["children"]?.AsArray() ?? new JsonArray()) foreach (var n in Flatten(child)) yield return n;
    }

    [Fact]
    public async Task Creating_an_asset_creates_and_links_its_two_accounts_under_the_default_parents()
    {
        var api = await NewTenantAsync();
        var center = await CenterAsync(api);

        var created = await api.Post("/fixedassets", Asset("FA-1", center));
        Assert.Equal(201, created.Status);
        var id = created.Data!["id"].S();

        var accumulated = await AccountAsync(api, created.Data["accumulatedDepreciationAccountId"].S());
        var expense = await AccountAsync(api, created.Data["depreciationExpenseAccountId"].S());
        Assert.Equal("122", accumulated["parentCode"].S()); Assert.Equal("122001", accumulated["code"].S());
        Assert.Equal("523", expense["parentCode"].S()); Assert.Equal("523001", expense["code"].S());
        foreach (var account in new[] { accumulated, expense })
        {
            Assert.Equal("fixed_asset", account["linkedEntityType"].S());
            Assert.Equal(id, account["linkedEntityId"].S());
            Assert.Contains("سيارة", account["nameAr"].S());
            Assert.Contains("FA-1", account["nameAr"].S());
        }
        Assert.False(accumulated["isDebitNature"]!.GetValue<bool>()); // يرث طبيعة مجمع الإهلاك الدائنة
        Assert.Equal("expense", expense["type"].S());

        // الأصل التالي يأخذ الرقم التالي، ولا يتكرر حساب للأصل نفسه
        var second = await api.Post("/fixedassets", Asset("FA-2", center));
        Assert.Equal("122002", (await AccountAsync(api, second.Data!["accumulatedDepreciationAccountId"].S()))["code"].S());
        Assert.Equal(2, await ChildCountAsync(api, "122"));
        Assert.Equal(2, await ChildCountAsync(api, "523"));
    }

    [Fact]
    public async Task Renaming_an_asset_renames_its_accounts_without_new_accounts_or_new_codes()
    {
        var api = await NewTenantAsync();
        var center = await CenterAsync(api);
        var created = (await api.Post("/fixedassets", Asset("FA-1", center))).Data!;
        var asset = created["id"].G();
        var (accumulated, expense) = await AssetAccountsAsync(api, asset);

        // التعديل يرسل حسابات أخرى (الافتراضية) فلا يستبدل الحسابين المخصصين
        var defaults = (await api.Get("/accounts/ByCode/122")).Data!["id"].S();
        var renamed = await api.Put($"/fixedassets/{asset}", new
        {
            assetCode = "FA-1", nameAr = "شاحنة", nameEn = "Truck", purchaseDate = "2026-01-10", purchaseCost = 12000, currentBookValue = 12000,
            depreciationRate = 10, costCenterId = center, accumulatedDepreciationAccountId = defaults,
            depreciationExpenseAccountId = created["depreciationExpenseAccountId"].S(),
        });
        Assert.Equal(200, renamed.Status);
        Assert.Equal(created["accumulatedDepreciationAccountId"].S(), renamed.Data!["accumulatedDepreciationAccountId"].S());

        Assert.Equal((accumulated, expense), await AssetAccountsAsync(api, asset));
        var acc = (await api.Get($"/accounts/ByCode/{accumulated}")).Data!;
        Assert.Contains("شاحنة", acc["nameAr"].S()); Assert.DoesNotContain("سيارة", acc["nameAr"].S());
        Assert.Contains("Truck", acc["nameEn"].S());
        Assert.Contains("شاحنة", (await api.Get($"/accounts/ByCode/{expense}")).Data!["nameAr"].S());
        Assert.Equal(1, await ChildCountAsync(api, "122"));
        Assert.Equal(1, await ChildCountAsync(api, "523"));
    }

    [Fact]
    public async Task Deleting_an_asset_without_history_deletes_its_accounts_and_they_cannot_be_deleted_alone()
    {
        var api = await NewTenantAsync();
        var center = await CenterAsync(api);
        var created = (await api.Post("/fixedassets", Asset("FA-1", center))).Data!;
        var accumulatedId = created["accumulatedDepreciationAccountId"].S();
        var expenseId = created["depreciationExpenseAccountId"].S();

        // من شجرة الحسابات: الحساب المخصص لا يُحذف منفرداً
        var direct = await api.Delete($"/accounts/{accumulatedId}");
        Assert.Equal(409, direct.Status);
        Assert.Contains("أصل", direct.Body!["message"].S());

        Assert.Equal(200, (await api.Delete($"/fixedassets/{created["id"].S()}")).Status);
        Assert.Equal(404, (await api.Get($"/accounts/{accumulatedId}")).Status);
        Assert.Equal(404, (await api.Get($"/accounts/{expenseId}")).Status);

        // الرقم المحذوف لا يُعاد لأصل جديد
        var next = (await api.Post("/fixedassets", Asset("FA-2", center))).Data!;
        Assert.Equal("122002", (await AccountAsync(api, next["accumulatedDepreciationAccountId"].S()))["code"].S());
    }

    [Fact]
    public async Task Assets_with_accounting_history_cannot_be_deleted_and_keep_their_accounts()
    {
        var api = await NewTenantAsync();
        var center = await CenterAsync(api);

        // إهلاك مرحَّل
        var depreciated = (await api.Post("/fixedassets", Asset("FA-1", center))).Data!;
        Assert.Equal(200, (await api.Post("/fixedassets/depreciation/post", new { period = "2026-01", assetIds = new[] { depreciated["id"].G() } })).Status);
        var blocked = await api.Delete($"/fixedassets/{depreciated["id"].S()}");
        Assert.Equal(409, blocked.Status);
        Assert.Contains("استبعد", blocked.Body!["message"].S());
        Assert.Equal(200, (await api.Get($"/accounts/{depreciated["accumulatedDepreciationAccountId"].S()}")).Status);

        // قيد اقتناء
        var funded = (await api.Post("/fixedassets", Asset("FA-2", center, acquisition: "1111"))).Data!;
        Assert.Equal(409, (await api.Delete($"/fixedassets/{funded["id"].S()}")).Status);

        // قيد يدوي على حسابه المخصص
        var manual = (await api.Post("/fixedassets", Asset("FA-3", center))).Data!;
        var (_, expense) = await AssetAccountsAsync(api, manual["id"].G());
        var entry = await api.Post("/journalentries", new
        {
            date = DateTime.UtcNow, description = "قيد",
            lines = new object[] { new { accountCode = expense, debit = 5, credit = 0, costCenterId = center }, new { accountCode = "1111", debit = 0, credit = 5 } },
        });
        Assert.Equal(200, entry.Status);
        Assert.Equal(409, (await api.Delete($"/fixedassets/{manual["id"].S()}")).Status);

        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
    }

    [Fact]
    public async Task Tenants_already_posting_on_the_default_accounts_keep_using_them()
    {
        var api = await NewTenantAsync();
        var center = await CenterAsync(api);
        var shared122 = (await api.Get("/accounts/ByCode/122")).Data!["id"].S();
        var shared523 = (await api.Get("/accounts/ByCode/523")).Data!["id"].S();

        // أصل قائم على الحسابين العامين (حُدِّدا صراحةً كما قبل الحسابات المخصصة) وله إهلاك مرحَّل عليهما
        var legacy = await api.Post("/fixedassets", new
        {
            assetCode = "FA-1", nameAr = "أصل", nameEn = "A", purchaseDate = "2026-01-10", purchaseCost = 1200, currentBookValue = 1200,
            depreciationRate = 10, costCenterId = center, accumulatedDepreciationAccountId = shared122, depreciationExpenseAccountId = shared523,
        });
        Assert.Equal(201, legacy.Status);
        Assert.Equal(shared122, legacy.Data!["accumulatedDepreciationAccountId"].S());
        Assert.Equal(200, (await api.Post("/fixedassets/depreciation/post", new { period = "2026-01", assetIds = new[] { legacy.Data["id"].G() } })).Status);

        // 122 و523 مستخدمان مباشرة: الأصل الجديد يبقى عليهما ولا يصيران حسابين تجميعيين يرفض المحرك الترحيل عليهما
        var fresh = (await api.Post("/fixedassets", Asset("FA-2", center))).Data!;
        Assert.Equal(("122", "523"), await AssetAccountsAsync(api, fresh["id"].G()));
        Assert.Equal(0, await ChildCountAsync(api, "122"));
        Assert.Equal(0, await ChildCountAsync(api, "523"));
        Assert.Equal(200, (await api.Post("/fixedassets/depreciation/post", new { period = "2026-02" })).Status); // الأصلان معاً

        // تعديل اسم الأصل لا يعيد تسمية الحساب العام
        Assert.Equal(200, (await api.Put($"/fixedassets/{fresh["id"].S()}", Asset("FA-2", center, name: "شاحنة"))).Status);
        Assert.Equal("مجمع الإهلاك", (await api.Get("/accounts/ByCode/122")).Data!["nameAr"].S());
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
    }

    [Fact]
    public async Task Concurrent_asset_creation_creates_distinct_accounts()
    {
        var api = await NewTenantAsync();
        var center = await CenterAsync(api);
        var results = await Task.WhenAll(Enumerable.Range(1, 8).Select(i => api.Post("/fixedassets", Asset($"FA-{i}", center))));
        Assert.All(results, r => Assert.Equal(201, r.Status));
        var ids = results.SelectMany(r => new[] { r.Data!["accumulatedDepreciationAccountId"].S(), r.Data["depreciationExpenseAccountId"].S() }).ToList();
        Assert.Equal(16, ids.Distinct().Count());
        Assert.Equal(8, await ChildCountAsync(api, "122"));
        Assert.Equal(8, await ChildCountAsync(api, "523"));
    }
}
