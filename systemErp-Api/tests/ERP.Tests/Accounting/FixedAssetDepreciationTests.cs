using ERP.Tests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace ERP.Tests;

/// <summary>مركز التكلفة (النشاط) على الأصل الثابت، وإهلاكه الشهري عبر المحرك المحاسبي دون تكرار.</summary>
[Collection("api")]
public class FixedAssetDepreciationTests : TestBase
{
    public FixedAssetDepreciationTests(ErpFactory f) : base(f) { }

    private static async Task<Guid> SeedCostCenterAsync(Client api, string code, bool isActive = true)
    {
        var c = await api.Post("/costcenters", new { code, nameAr = "مركز " + code, nameEn = code, isActive });
        Assert.Equal(201, c.Status);
        return c.Data!["id"].G();
    }

    private static object Asset(string code, Guid? costCenterId, decimal cost = 100000, decimal rate = 20, decimal? bookValue = null, string purchaseDate = "2026-01-10")
        => new { assetCode = code, nameAr = "أصل " + code, nameEn = code, purchaseDate, purchaseCost = cost, currentBookValue = bookValue ?? cost, depreciationRate = rate, costCenterId };

    private static Task<Res> Post(Client api, string period, params Guid[] assetIds)
        => api.Post("/fixedassets/depreciation/post", new { period, assetIds });

    /// <summary>أصل مسجَّل قبل إضافة مركز التكلفة: يُفرَّغ العمودان الجديدان مباشرة كما تتركهما الـ migration.</summary>
    private async Task MakeLegacyAsync(Guid assetId)
    {
        await using var conn = new SqlConnection(Factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE FixedAsset SET CostCenterId = NULL, DepreciationExpenseAccountId = NULL WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", assetId);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task New_asset_requires_an_active_cost_center_of_the_same_tenant()
    {
        var api = await NewTenantAsync();
        var other = await NewTenantAsync();
        var foreign = await SeedCostCenterAsync(other, "X");
        var inactive = await SeedCostCenterAsync(api, "OFF", isActive: false);
        var active = await SeedCostCenterAsync(api, "MNT");

        Assert.Equal(400, (await api.Post("/fixedassets", Asset("FA-1", null))).Status);
        Assert.Equal(400, (await api.Post("/fixedassets", Asset("FA-1", Guid.NewGuid()))).Status);
        Assert.Equal(400, (await api.Post("/fixedassets", Asset("FA-1", foreign))).Status);
        Assert.Equal(400, (await api.Post("/fixedassets", Asset("FA-1", inactive))).Status);

        var created = await api.Post("/fixedassets", Asset("FA-1", active));
        Assert.Equal(201, created.Status);
        Assert.Equal(active, created.Data!["costCenterId"].G());
        Assert.Equal((await api.Get("/accounts/ByCode/523")).Data!["id"].S(), created.Data["depreciationExpenseAccountId"].S());

        await api.Post("/fixedassets", Asset("FA-2", await SeedCostCenterAsync(api, "SLS")));
        var filtered = (await api.Get($"/fixedassets?costCenterId={active}")).Data!["items"]!.AsArray();
        Assert.Equal("FA-1", Assert.Single(filtered)!["assetCode"].S());
        Assert.Equal(2, (await api.Get("/fixedassets")).Data!["items"]!.AsArray().Count);
    }

    [Fact]
    public async Task Depreciation_preview_has_no_effect_and_posting_charges_the_asset_cost_center_once_per_period()
    {
        var api = await NewTenantAsync();
        var center = await SeedCostCenterAsync(api, "MNT");
        var asset = (await api.Post("/fixedassets", Asset("FA-1", center))).Data!["id"].G();
        var entriesBefore = (await api.Get("/journalentries")).Data!["totalCount"]!.GetValue<int>();

        var preview = await api.Post("/fixedassets/depreciation/preview", new { period = "2026-01" });
        Assert.Equal(200, preview.Status);
        Assert.True(preview.Data!["isPreview"]!.GetValue<bool>());
        var line = Assert.Single(preview.Data["lines"]!.AsArray())!;
        Assert.Equal("ready", line["status"].S()); Assert.Equal(1666.67m, line["amount"].D()); // 100,000 × 20% ÷ 12
        Assert.Equal(entriesBefore, (await api.Get("/journalentries")).Data!["totalCount"]!.GetValue<int>());
        Assert.Equal(100000, (await api.Get($"/fixedassets/{asset}")).Data!["currentBookValue"].D());

        var posted = await Post(api, "2026-01");
        Assert.Equal(200, posted.Status);
        Assert.Equal(1666.67m, posted.Data!["totalAmount"].D());
        var entry = (await api.Get($"/journalentries/{posted.Data["journalEntryId"].S()}")).Data!;
        Assert.Equal("posted", entry["status"].S());
        var lines = entry["lines"]!.AsArray();
        Assert.Equal(2, lines.Count);
        var debit = lines.Single(l => l!["debit"].D() > 0)!; var credit = lines.Single(l => l!["credit"].D() > 0)!;
        Assert.Equal("523", debit["accountCode"].S()); Assert.Equal(1666.67m, debit["debit"].D()); Assert.Equal(center, debit["costCenterId"].G());
        Assert.Equal("122", credit["accountCode"].S()); Assert.Equal(1666.67m, credit["credit"].D()); Assert.Equal(center, credit["costCenterId"].G());

        var after = (await api.Get($"/fixedassets/{asset}")).Data!;
        Assert.Equal(1666.67m, after["accumulatedDepreciation"].D()); Assert.Equal(98333.33m, after["currentBookValue"].D());
        Assert.Equal(1666.67m, (await api.Get("/accounts/ByCode/122")).Data!["balance"].D());

        // نفس الفترة لا تُرحَّل مرتين: لا للأصل المحدد ولا ضمن ترحيل الكل، ولا يتغيّر رصيد.
        Assert.Equal(409, (await Post(api, "2026-01", asset)).Status);
        Assert.Equal(400, (await Post(api, "2026-01")).Status);
        Assert.Equal("posted", (await api.Post("/fixedassets/depreciation/preview", new { period = "2026-01" })).Data!["lines"]![0]!["status"].S());
        Assert.Equal(98333.33m, (await api.Get($"/fixedassets/{asset}")).Data!["currentBookValue"].D());
        Assert.Single((await api.Get($"/fixedassets/depreciation?fixedAssetId={asset}")).Data!["items"]!.AsArray());

        Assert.Equal(200, (await Post(api, "2026-02")).Status);
        Assert.Equal(96666.66m, (await api.Get($"/fixedassets/{asset}")).Data!["currentBookValue"].D());
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);

        Assert.Equal(400, (await Post(api, "2026/01")).Status);
        Assert.Equal(400, (await Post(api, DateTime.UtcNow.AddMonths(1).ToString("yyyy-MM"))).Status);
    }

    [Fact]
    public async Task Legacy_asset_without_cost_center_stays_valid_but_cannot_be_depreciated_until_assigned()
    {
        var api = await NewTenantAsync();
        api.Language = "en";
        var center = await SeedCostCenterAsync(api, "MNT");
        var asset = (await api.Post("/fixedassets", Asset("FA-1", center))).Data!["id"].G();
        await MakeLegacyAsync(asset);

        Assert.Null((await api.Get($"/fixedassets/{asset}")).Data!["costCenterId"]);
        var blocked = await Post(api, "2026-01", asset);
        Assert.Equal(400, blocked.Status);
        Assert.Equal("Cost center is required before depreciation can be posted for asset FA-1.", blocked.Body!["message"].S());
        Assert.Equal("blocked", (await api.Post("/fixedassets/depreciation/preview", new { period = "2026-01" })).Data!["lines"]![0]!["status"].S());

        // يبقى قابلاً للتعديل بلا مركز تكلفة، ثم يُحدَّد له يدوياً فيُرحَّل إهلاكه.
        Assert.Equal(200, (await api.Put($"/fixedassets/{asset}", Asset("FA-1", null))).Status);
        Assert.Equal(200, (await api.Put($"/fixedassets/{asset}", Asset("FA-1", center))).Status);
        Assert.Equal(400, (await api.Put($"/fixedassets/{asset}", Asset("FA-1", null))).Status); // لا يُفرَّغ بعد تحديده
        Assert.Equal(200, (await Post(api, "2026-01", asset)).Status);
    }

    [Fact]
    public async Task Changing_the_cost_center_affects_only_later_periods_and_is_audited()
    {
        var api = await NewTenantAsync();
        var a = await SeedCostCenterAsync(api, "A"); var b = await SeedCostCenterAsync(api, "B");
        var asset = (await api.Post("/fixedassets", Asset("FA-1", a))).Data!["id"].G();

        var first = await Post(api, "2026-01");
        Assert.Equal(200, (await api.Put($"/fixedassets/{asset}", Asset("FA-1", b))).Status);
        var second = await Post(api, "2026-02");

        async Task<List<Guid>> Centers(Res run) => (await api.Get($"/journalentries/{run.Data!["journalEntryId"].S()}")).Data!["lines"]!.AsArray().Select(l => l!["costCenterId"].G()).Distinct().ToList();
        Assert.Equal(new[] { a }, await Centers(first));
        Assert.Equal(new[] { b }, await Centers(second));
        Assert.Equal("2026-01", Assert.Single((await api.Get($"/fixedassets/depreciation?costCenterId={a}")).Data!["items"]!.AsArray())!["period"].S());
        Assert.Equal("2026-02", Assert.Single((await api.Get($"/fixedassets/depreciation?costCenterId={b}")).Data!["items"]!.AsArray())!["period"].S());

        var audit = (await api.Get($"/auditlogs?entityName=FixedAsset&entityId={asset}")).Data!["items"]!.AsArray().Select(x => x!["action"].S()).ToList();
        Assert.Contains("COST_CENTER_CHANGED", audit);
    }

    [Fact]
    public async Task Periods_are_posted_in_order_without_gaps_or_backfill()
    {
        var api = await NewTenantAsync();
        api.Language = "en";
        var center = await SeedCostCenterAsync(api, "MNT");
        var asset = (await api.Post("/fixedassets", Asset("FA-1", center, purchaseDate: "2025-11-15"))).Data!["id"].G();

        Assert.Equal(200, (await Post(api, "2026-01", asset)).Status); // أول فترة حرّة
        var gap = await Post(api, "2026-03", asset);
        Assert.Equal(400, gap.Status);
        Assert.Equal("The next depreciation period due for asset FA-1 is 2026-02; periods cannot be posted out of order.", gap.Body!["message"].S());
        Assert.Equal(400, (await Post(api, "2025-12", asset)).Status); // لا رجوع قبل آخر فترة مرحَّلة
        Assert.Equal("blocked", (await api.Post("/fixedassets/depreciation/preview", new { period = "2026-03" })).Data!["lines"]![0]!["status"].S());
        Assert.Equal(400, (await Post(api, "2026-03")).Status); // ولا ضمن ترحيل الكل

        Assert.Equal(200, (await Post(api, "2026-02", asset)).Status);
        Assert.Equal(200, (await Post(api, "2026-03", asset)).Status);
        Assert.Equal(3, (await api.Get($"/fixedassets/depreciation?fixedAssetId={asset}")).Data!["items"]!.AsArray().Count);
        Assert.Equal(94999.99m, (await api.Get($"/fixedassets/{asset}")).Data!["currentBookValue"].D()); // 100,000 − 3 × 1666.67
    }

    [Fact]
    public async Task Depreciation_never_goes_below_zero_and_posted_balances_cannot_be_overwritten_by_an_edit()
    {
        var api = await NewTenantAsync();
        var center = await SeedCostCenterAsync(api, "MNT");
        // تكلفة 1000 بنسبة 100% (83.33 شهرياً) والمتبقي 100: قسط كامل ثم المتبقي ثم لا شيء.
        var asset = (await api.Post("/fixedassets", Asset("FA-1", center, cost: 1000, rate: 100, bookValue: 100))).Data!["id"].G();
        var later = (await api.Post("/fixedassets", Asset("FA-2", center, purchaseDate: "2026-03-01"))).Data!["id"].G();

        Assert.Equal(83.33m, (await Post(api, "2026-01")).Data!["totalAmount"].D()); // FA-2 لم يُقتنَ بعد
        Assert.Equal(400, (await Post(api, "2026-01", later)).Status);

        // تعديل يحمل أرصدة قديمة لا يمحو الإهلاك المرحَّل، وتغيير التكلفة مرفوض.
        Assert.Equal(200, (await api.Put($"/fixedassets/{asset}", Asset("FA-1", center, cost: 1000, rate: 100, bookValue: 1000))).Status);
        Assert.Equal(16.67m, (await api.Get($"/fixedassets/{asset}")).Data!["currentBookValue"].D());
        Assert.Equal(409, (await api.Put($"/fixedassets/{asset}", Asset("FA-1", center, cost: 5000, rate: 100))).Status);

        Assert.Equal(16.67m, (await Post(api, "2026-02", asset)).Data!["totalAmount"].D());
        Assert.Equal(0, (await api.Get($"/fixedassets/{asset}")).Data!["currentBookValue"].D());
        Assert.Equal(400, (await Post(api, "2026-03", asset)).Status); // مُهلك بالكامل
        Assert.Equal(409, (await api.Delete($"/fixedassets/{asset}")).Status); // له سجلات إهلاك
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
    }
}
