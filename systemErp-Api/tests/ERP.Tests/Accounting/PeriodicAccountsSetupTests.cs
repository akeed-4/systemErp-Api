using ERP.Core.Models.Accounting;
using ERP.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Tests;

/// <summary>
/// تهيئة حسابات الجرد الدوري (آلية تهيئة حسابات العقود نفسها): المعاينة لا تكتب؛ الزر ينشئ المشتريات وأول المدة وآخرها مصروفات داخل
/// مجموعة تكلفة المبيعات بترقيم إخوتها ويربطها؛ المردودات اختيارية تُعاد استخدامها فقط؛ حساب المخزون لا يُنشأ؛ القائم المناسب يُعاد استخدامه؛
/// التكرار لا يكرر؛ أكثر من مرشّح تعارض يُبلَّغ؛ اختيار المستخدم لا يُمس؛ اختيار الدوري بالحفظ يهيّئ الناقص؛ قواعد الدوري تُفرض في السرفر؛
/// ولا تتسرب الحسابات بين المنشآت.
/// </summary>
[Collection("api")]
public class PeriodicAccountsSetupTests : TestBase
{
    public PeriodicAccountsSetupTests(ErpFactory f) : base(f) { }

    private const string Purchases = "purchasesAccountCode", Returns = "purchaseReturnsAccountCode", Opening = "openingInventoryAccountCode",
        Ending = "endingInventoryAccountCode";

    private static Task<Res> Initialize(Client api) => api.Post("/accounting-settings/periodic-accounts");

    private static async Task<string?> Setting(Client api, string key) => (await api.Get("/accounting-settings")).Data![key]?.GetValue<string?>();

    private static string Status(Res report, string role)
        => report.Data!["items"]!.AsArray().Single(i => i!["role"].S().Equals(role, StringComparison.OrdinalIgnoreCase))!["status"].S();

    private static string? Message(Res report, string role)
        => report.Data!["items"]!.AsArray().Single(i => i!["role"].S().Equals(role, StringComparison.OrdinalIgnoreCase))!["message"]?.GetValue<string?>();

    private async Task<int> AccountCountAsync(Client api)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>();
        return await db.Set<Account>().IgnoreQueryFilters().CountAsync(a => a.TenantId == api.TenantId);
    }

    private static async Task NamedAccountAsync(Client api, string code, string nameAr, string type, string parent)
        => Assert.Equal(201, (await api.Post("/accounts", new { code, nameAr, nameEn = nameAr, type, parentCode = parent, isDebitNature = type is "asset" or "expense" })).Status);

    /// <summary>مجموعة تكلفة المبيعات: أب حساب تكلفة البضاعة المباعة.</summary>
    private static async Task<string> CostOfSalesGroupAsync(Client api)
        => (await api.Get("/accounts/ByCode/511")).Data!["parentCode"].S();

    [Fact]
    public async Task Preview_writes_nothing_and_the_button_creates_linked_expense_accounts_inside_cost_of_sales()
    {
        var api = await NewTenantAsync();
        var before = await AccountCountAsync(api);

        var preview = await api.Get("/accounting-settings/periodic-accounts");
        Assert.True(preview.Data!["isPreview"]!.GetValue<bool>());
        Assert.Equal("missing", Status(preview, Purchases));
        Assert.Equal("missing", Status(preview, Opening));
        Assert.Equal("missing", Status(preview, Ending));
        Assert.Equal("skipped", Status(preview, Returns));
        Assert.Equal("kept", Status(preview, "inventoryAccountCode")); // المخزون لا يُنشأ
        Assert.Equal(before, await AccountCountAsync(api));
        Assert.Null(await Setting(api, Purchases));

        var report = await Initialize(api);
        Assert.True(report.Status == 200, report.Body?.ToJsonString());
        Assert.Equal(3, report.Data!["created"]!.GetValue<int>());
        Assert.Equal("skipped", Status(report, Returns));
        Assert.False(string.IsNullOrWhiteSpace(Message(report, Returns))); // المردودات تخفّض المشتريات نفسها
        Assert.Equal(before + 3, await AccountCountAsync(api));

        var group = await CostOfSalesGroupAsync(api);
        var codes = new List<string>();
        foreach (var key in new[] { Purchases, Opening, Ending })
        {
            var code = (await Setting(api, key))!;
            codes.Add(code);
            var account = (await api.Get($"/accounts/ByCode/{code}")).Data!;
            Assert.Equal("expense", account["type"].S().ToLowerInvariant());
            Assert.Equal(group, account["parentCode"].S());
            Assert.StartsWith(group, code); // ترقيم إخوته تحت المجموعة
        }
        Assert.Equal(3, codes.Distinct().Count());
        Assert.Null(await Setting(api, Returns));
    }

    [Fact]
    public async Task Repeated_runs_do_not_duplicate_and_existing_named_accounts_are_reused()
    {
        var api = await NewTenantAsync();
        var group = await CostOfSalesGroupAsync(api);
        await NamedAccountAsync(api, group + "88", "المشتريات", "expense", group);
        await NamedAccountAsync(api, group + "89", "مردودات المشتريات", "expense", group);
        await NamedAccountAsync(api, "5299", "بضاعة آخر المدة", "expense", "52"); // خارج تكلفة المبيعات: لا يُعاد استخدامه

        var first = await Initialize(api);
        Assert.Equal("reused", Status(first, Purchases));
        Assert.Equal(group + "88", await Setting(api, Purchases));
        Assert.Equal("reused", Status(first, Returns));
        Assert.Equal(group + "89", await Setting(api, Returns));
        Assert.Equal("created", Status(first, Ending));
        Assert.NotEqual("5299", await Setting(api, Ending));

        var count = await AccountCountAsync(api);
        var second = await Initialize(api);
        Assert.All(new[] { Purchases, Returns, Opening, Ending }, k => Assert.Equal("kept", Status(second, k)));
        Assert.Equal(count, await AccountCountAsync(api));
        Assert.True((await api.Get("/accounting-settings/periodic-accounts")).Data!["success"]!.GetValue<bool>()); // الفحص عند إعادة الفتح
    }

    [Fact]
    public async Task Ambiguous_candidates_are_reported_without_guessing()
    {
        var api = await NewTenantAsync();
        var group = await CostOfSalesGroupAsync(api);
        await NamedAccountAsync(api, group + "86", "بضاعة أول المدة", "expense", group);
        await NamedAccountAsync(api, group + "87", "Opening Inventory", "expense", group);

        var report = await Initialize(api);
        Assert.Equal("problem", Status(report, Opening));
        Assert.Contains(group + "86", Message(report, Opening));
        Assert.Contains(group + "87", Message(report, Opening));
        Assert.Null(await Setting(api, Opening));
        Assert.Equal("created", Status(report, Purchases));
    }

    [Fact]
    public async Task User_choices_are_kept_and_unsuitable_ones_are_reported_not_replaced()
    {
        var api = await NewTenantAsync();
        // في المستمر قواعد الدوري لا تُطبَّق فيُحفظ اختيار خارج تكلفة المبيعات
        Assert.Equal(200, (await api.Put("/accounting-settings", new { purchasesAccountCode = "524" })).Status);
        var report = await Initialize(api);
        Assert.Equal("problem", Status(report, Purchases));
        Assert.Equal("524", await Setting(api, Purchases));
        Assert.Equal("created", Status(report, Opening));
    }

    [Fact]
    public async Task Choosing_periodic_on_save_initializes_missing_accounts()
    {
        var api = await NewTenantAsync();
        var saved = await api.Put("/accounting-settings", new { inventoryMethod = "periodic" });
        Assert.True(saved.Status == 200, saved.Body?.ToJsonString());
        Assert.Equal(3, saved.Data!["periodicAccountsSetup"]!["created"]!.GetValue<int>());
        Assert.NotNull(await Setting(api, Purchases));
        Assert.NotNull(await Setting(api, Opening));
        Assert.NotNull(await Setting(api, Ending));

        // الحفظ التالي بالحسابات مكتملة لا يهيّئ شيئاً
        var again = await api.Put("/accounting-settings", new { inventoryMethod = "periodic" });
        Assert.Null(again.Data!["periodicAccountsSetup"]);
    }

    [Fact]
    public async Task Periodic_rules_reject_unsuitable_or_overlapping_accounts_on_the_server()
    {
        var api = await NewTenantAsync();
        Assert.Equal(200, (await api.Put("/accounting-settings", new { inventoryMethod = "periodic" })).Status);
        var purchases = (await Setting(api, Purchases))!;
        var opening = (await Setting(api, Opening))!;

        Assert.Equal(400, (await api.Put("/accounting-settings", new { openingInventoryAccountCode = "524" })).Status);   // خارج تكلفة المبيعات
        Assert.Equal(400, (await api.Put("/accounting-settings", new { endingInventoryAccountCode = "421" })).Status);    // إيراد
        Assert.Equal(400, (await api.Put("/accounting-settings", new { openingInventoryAccountCode = purchases })).Status); // المشتريات = أول المدة
        Assert.Equal(400, (await api.Put("/accounting-settings", new { endingInventoryAccountCode = purchases })).Status);
        Assert.Equal(400, (await api.Put("/accounting-settings", new { purchaseReturnsAccountCode = purchases })).Status);  // المردودات = المشتريات
        Assert.Equal(400, (await api.Put("/accounting-settings", new { endingInventoryAccountCode = "511" })).Status);     // تكلفة البضاعة المباعة
        // حساب واحد لأول المدة وآخرها (تغيّر المخزون) مسموح
        Assert.Equal(200, (await api.Put("/accounting-settings", new { endingInventoryAccountCode = opening })).Status);
    }

    [Fact]
    public async Task Accounts_do_not_leak_between_companies()
    {
        var a = await NewTenantAsync("شركة أ");
        var b = await NewTenantAsync("شركة ب");
        var bBefore = await AccountCountAsync(b);

        Assert.Equal(200, (await Initialize(a)).Status);
        Assert.Equal(bBefore, await AccountCountAsync(b));
        Assert.Null(await Setting(b, Purchases));
        Assert.Equal("missing", Status(await b.Get("/accounting-settings/periodic-accounts"), Purchases));
    }
}
