using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// حساب العميل/المورد/البنك المُنشأ من شجرة الحسابات يُحفظ بالرمز الذي عُرض للمستخدم (بعد التحقق)، فلا يظهر في الشجرة برقم آخر؛
/// وبلا رمز (شاشات العملاء والموردين والبنوك) يولّده الخادم كما كان.
/// </summary>
[Collection("api")]
public class LinkedAccountCodeTests : TestBase
{
    public LinkedAccountCodeTests(ErpFactory f) : base(f) { }

    private static object Customer(string name, string? accountCode = null)
        => new { nameAr = name, nameEn = name, phone = "0500000000", accountCode = accountCode ?? string.Empty };

    [Fact]
    public async Task Requested_codes_are_kept_under_the_chosen_parent()
    {
        var api = await NewTenantAsync();

        var customer = await api.Post("/customers", Customer("عميل أ", "112005"));
        Assert.Equal(201, customer.Status);
        Assert.Equal("112005", customer.Data!["accountCode"].S());
        var account = (await api.Get("/accounts/ByCode/112005")).Data!;
        Assert.Equal("112", account["parentCode"].S());
        Assert.Equal("customer", account["linkedEntityType"].S());

        // تحت مجموعة فرعية داخل العملاء: الأب أطول حساب قائم رمزه بادئة
        var group = await api.Post("/accounts", new { code = "1129", nameAr = "عملاء الجملة", nameEn = "Wholesale", type = "asset", parentCode = "112", isDebitNature = true });
        Assert.Equal(201, group.Status);
        Assert.Equal(201, (await api.Post("/customers", Customer("عميل ب", "11291"))).Status);
        Assert.Equal("1129", (await api.Get("/accounts/ByCode/11291")).Data!["parentCode"].S());

        var supplier = await api.Post("/suppliers", new { nameAr = "مورد", nameEn = "S", phone = "0500000001", accountCode = "211007" });
        Assert.Equal(201, supplier.Status);
        Assert.Equal("211007", supplier.Data!["accountCode"].S());
        var bank = await api.Post("/banks", new { nameAr = "بنك", nameEn = "B", accountCode = "11109" });
        Assert.Equal(201, bank.Status);
        Assert.Equal("11109", bank.Data!["accountCode"].S());
    }

    [Fact]
    public async Task General_accounts_inside_party_groups_stay_general_with_their_code()
    {
        var api = await NewTenantAsync();
        var customersBefore = (await api.Get("/customers")).Data!["totalCount"]!.GetValue<int>();
        foreach (var (code, parent, type) in new[] { ("1128", "112", "asset"), ("2118", "211", "liability"), ("1118", "111", "asset") })
        {
            var created = await api.Post("/accounts", new { code, nameAr = "عام " + code, nameEn = code, type, parentCode = parent, isDebitNature = type == "asset", linkedEntityType = "general" });
            Assert.Equal(201, created.Status);
            var saved = (await api.Get($"/accounts/ByCode/{code}")).Data!;
            Assert.Equal(parent, saved["parentCode"].S());
            Assert.True(saved["linkedEntityId"] == null);
            Assert.True(saved["linkedEntityType"] == null || saved["linkedEntityType"].S() == "general");
        }
        Assert.Equal(customersBefore, (await api.Get("/customers")).Data!["totalCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task Multi_level_codes_take_the_parent_with_the_longest_matching_prefix()
    {
        var api = await NewTenantAsync();
        Assert.Equal(201, (await api.Post("/accounts", new { code = "1129", nameAr = "مجموعة", nameEn = "G", type = "asset", parentCode = "112", isDebitNature = true })).Status);
        Assert.Equal(201, (await api.Post("/accounts", new { code = "11291", nameAr = "فرعية", nameEn = "S", type = "asset", parentCode = "1129", isDebitNature = true })).Status);

        Assert.Equal(201, (await api.Post("/customers", Customer("عميل عميق", "112915"))).Status);
        Assert.Equal("11291", (await api.Get("/accounts/ByCode/112915")).Data!["parentCode"].S());
        Assert.Equal(201, (await api.Post("/customers", Customer("عميل وسط", "11290"))).Status); // 11291 ليس بادئة لـ 11290
        Assert.Equal("1129", (await api.Get("/accounts/ByCode/11290")).Data!["parentCode"].S());
        var level1129 = (await api.Get("/accounts/ByCode/1129")).Data!["level"]!.GetValue<int>();
        Assert.Equal(level1129 + 1, (await api.Get("/accounts/ByCode/11290")).Data!["level"]!.GetValue<int>());
        Assert.Equal(level1129 + 2, (await api.Get("/accounts/ByCode/112915")).Data!["level"]!.GetValue<int>());
    }

    [Fact]
    public async Task Existing_accounts_keep_their_codes_when_requested_codes_are_used()
    {
        var api = await NewTenantAsync();
        var first = (await api.Post("/customers", Customer("قديم"))).Data!;
        var firstCode = first["accountCode"].S();
        Assert.Equal("112001", firstCode);

        Assert.Equal(201, (await api.Post("/customers", Customer("مختار", "112050"))).Status);
        var next = (await api.Post("/customers", Customer("تلقائي بعده"))).Data!;
        Assert.Equal("112051", next["accountCode"].S()); // بعد أكبر رمز قائم بالشكل نفسه، بلا تعارض

        Assert.Equal(firstCode, (await api.Get($"/customers/{first["id"].S()}")).Data!["accountCode"].S());
        Assert.Equal("112", (await api.Get($"/accounts/ByCode/{firstCode}")).Data!["parentCode"].S());
        foreach (var (code, parent) in new[] { ("112", "11"), ("211", "21"), ("111", "11") })
            Assert.Equal(parent, (await api.Get($"/accounts/ByCode/{code}")).Data!["parentCode"].S());
    }

    [Fact]
    public async Task Invalid_requested_codes_are_rejected_and_an_empty_code_is_generated()
    {
        var api = await NewTenantAsync();
        Assert.Equal(400, (await api.Post("/customers", Customer("خارج المجموعة", "211999"))).Status); // تحت الموردين
        Assert.Equal(400, (await api.Post("/customers", Customer("حروف", "112A1"))).Status);
        var first = await api.Post("/customers", Customer("أول", "112010"));
        Assert.Equal(201, first.Status);
        Assert.Equal(409, (await api.Post("/customers", Customer("مكرر", "112010"))).Status);
        // تحت حساب عميل آخر: مرفوض
        Assert.Equal(400, (await api.Post("/customers", Customer("تحت عميل", "1120101"))).Status);
        Assert.Equal(409, (await api.Post("/customers", Customer("رمز المجموعة", "112"))).Status);
        Assert.Equal(400, (await api.Post("/customers", Customer("مسافة", "112 05"))).Status);
        Assert.Equal(400, (await api.Post("/customers", Customer("تحت البنوك", "111077"))).Status);
        Assert.Equal(400, (await api.Post("/suppliers", new { nameAr = "مورد", nameEn = "S", phone = "0500000002", accountCode = "112077" })).Status);
        Assert.Equal(400, (await api.Post("/banks", new { nameAr = "بنك", nameEn = "B", accountCode = "211077" })).Status);
        // لا أثر للمحاولات المرفوضة
        Assert.Equal(404, (await api.Get("/accounts/ByCode/111077")).Status);

        var generated = await api.Post("/customers", Customer("تلقائي"));
        Assert.Equal(201, generated.Status);
        Assert.StartsWith("112", generated.Data!["accountCode"].S());
        Assert.Equal(6, generated.Data["accountCode"].S().Length);
    }
}
