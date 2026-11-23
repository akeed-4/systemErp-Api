using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// المرحلة الأولى من هيكل «النشاط ← الفرع ← الموقع»: الفرع الرئيسي عند التأسيس، ربط الموقع بفرع من نشاطه، نقله بين الفروع
/// دون مساس حساباته، وحسابات الموقع الخاصة كما هي (بلا تكرار عند التعديل أو إعادة التفعيل)، وقواعد الفرع، وعزل الأنشطة.
/// </summary>
[Collection("api")]
public class BranchLocationTests : TestBase
{
    public BranchLocationTests(ErpFactory f) : base(f) { }

    private static readonly string[] AccountFields =
        { "inventoryAccountCode", "cogsAccountCode", "salesAccountCode", "cashAccountCode", "shortageAccountCode", "surplusAccountCode" };

    private static async Task<Guid> BranchAsync(Client api, string code, string status = "active", bool isDefault = false)
    {
        var r = await api.Post("/branches", new { code, nameAr = "فرع " + code, nameEn = code, city = "الرياض", status, isDefault });
        Assert.True(r.Status == 201, r.Body?.ToJsonString());
        return r.Data!["id"].G();
    }

    private static object Location(string code, Guid? branchId, string status = "active") => new
    {
        branchId, code, nameAr = "موقع " + code, nameEn = code, location = "الرياض", status,
    };

    private static async Task<System.Text.Json.Nodes.JsonNode> LocationAsync(Client api, Guid id) => (await api.Get($"/warehouses/{id}")).Data!;

    private static async Task<int> LinkedAccountsAsync(Client api, Guid locationId)
    {
        var w = await LocationAsync(api, locationId);
        var codes = AccountFields.Select(f => w[f]?.ToString()).Where(c => !string.IsNullOrEmpty(c)).ToList();
        foreach (var code in codes) Assert.Equal(200, (await api.Get($"/accounts/ByCode/{code}")).Status);
        return codes.Count;
    }

    [Fact]
    public async Task New_activity_has_a_default_main_branch_holding_its_main_location()
    {
        var api = await NewTenantAsync();
        var branches = (await api.Get("/branches")).Data!["items"]!.AsArray();
        var main = Assert.Single(branches)!;
        Assert.Equal("BR-001", main["code"].S());
        Assert.True(main["isDefault"]!.GetValue<bool>());
        Assert.Equal("active", main["status"].S());

        var location = Assert.Single((await api.Get("/warehouses")).Data!["items"]!.AsArray())!;
        Assert.Equal(main["id"].G(), location["branchId"].G());
    }

    [Fact]
    public async Task Location_under_a_branch_gets_its_own_accounts_distinct_from_other_locations()
    {
        var api = await NewTenantAsync();
        var north = await BranchAsync(api, "N");
        var created = await api.Post("/warehouses", Location("N1", north));
        Assert.Equal(201, created.Status);
        var id = created.Data!["id"].G();
        Assert.Equal(north, created.Data!["branchId"].G());

        // حسابات الموقع الستة خاصة به وموجودة في الدليل، ولا يشارك فيها الموقع الرئيسي
        Assert.Equal(6, await LinkedAccountsAsync(api, id));
        var mine = await LocationAsync(api, id);
        var main = (await api.Get("/warehouses")).Data!["items"]!.AsArray().Single(w => w!["isDefault"]!.GetValue<bool>())!;
        foreach (var f in AccountFields)
            Assert.NotEqual(main[f]?.ToString(), mine[f]!.ToString());
        var second = (await api.Post("/warehouses", Location("N2", north))).Data!;
        foreach (var f in AccountFields)
            Assert.NotEqual(second[f]!.ToString(), mine[f]!.ToString());
    }

    [Fact]
    public async Task Branch_is_required_when_the_activity_has_several_branches()
    {
        var api = await NewTenantAsync();
        // فرع واحد: الموقع بلا فرع يُلحق به (توافق مع العملاء الحاليين والاستيراد)
        var single = await api.Post("/warehouses", Location("S1", null));
        Assert.Equal(201, single.Status);
        var mainBranch = (await api.Get("/branches")).Data!["items"]!.AsArray().Single()!["id"].G();
        Assert.Equal(mainBranch, single.Data!["branchId"].G());

        await BranchAsync(api, "E");
        Assert.Equal(400, (await api.Post("/warehouses", Location("S2", null))).Status);
        Assert.Equal(400, (await api.Post("/warehouses", Location("S3", Guid.NewGuid()))).Status); // فرع غير موجود
    }

    [Fact]
    public async Task Moving_a_location_between_branches_keeps_its_accounts_and_editing_does_not_duplicate_them()
    {
        var api = await NewTenantAsync();
        var a = await BranchAsync(api, "A");
        var b = await BranchAsync(api, "B");
        var id = (await api.Post("/warehouses", Location("M1", a))).Data!["id"].G();
        var before = await LocationAsync(api, id);
        var accountsBefore = (await api.Get("/accounts")).Data!["totalCount"]!.GetValue<int>();

        // تعديل بلا فرع: يبقى الفرع
        Assert.Equal(200, (await api.Put($"/warehouses/{id}", Location("M1", null))).Status);
        Assert.Equal(a, (await LocationAsync(api, id))["branchId"].G());

        // نقل إلى فرع آخر: الحسابات نفسها
        Assert.Equal(200, (await api.Put($"/warehouses/{id}", Location("M1", b))).Status);
        var moved = await LocationAsync(api, id);
        Assert.Equal(b, moved["branchId"].G());
        foreach (var f in AccountFields) Assert.Equal(before[f]!.ToString(), moved[f]!.ToString());

        // إيقاف ثم إعادة تفعيل: لا حسابات جديدة
        Assert.Equal(200, (await api.Put($"/warehouses/{id}", Location("M1", b, "inactive"))).Status);
        Assert.Equal(200, (await api.Put($"/warehouses/{id}", Location("M1", b, "active"))).Status);
        var after = await LocationAsync(api, id);
        foreach (var f in AccountFields) Assert.Equal(before[f]!.ToString(), after[f]!.ToString());
        Assert.Equal(6, await LinkedAccountsAsync(api, id));
        Assert.Equal(accountsBefore, (await api.Get("/accounts")).Data!["totalCount"]!.GetValue<int>()); // لا حساب جديد في الدليل
    }

    [Fact]
    public async Task Inactive_branch_accepts_no_active_location()
    {
        var api = await NewTenantAsync();
        var closed = await BranchAsync(api, "C", status: "inactive");
        var open = await BranchAsync(api, "O");
        Assert.Equal(400, (await api.Post("/warehouses", Location("C1", closed))).Status);

        var id = (await api.Post("/warehouses", Location("O1", open))).Data!["id"].G();
        Assert.Equal(400, (await api.Put($"/warehouses/{id}", Location("O1", closed))).Status); // نقل فعّال إلى فرع موقوف
        Assert.Equal(open, (await LocationAsync(api, id))["branchId"].G());
    }

    [Fact]
    public async Task Branch_rules_default_code_deactivation_and_deletion()
    {
        var api = await NewTenantAsync();
        var mainId = (await api.Get("/branches")).Data!["items"]!.AsArray().Single()!["id"].G();

        Assert.Equal(409, (await api.Post("/branches", new { code = "BR-001", nameAr = "مكرر", status = "active" })).Status);
        Assert.Equal(400, (await api.Post("/branches", new { code = "", nameAr = "بلا رمز", status = "active" })).Status);
        Assert.Equal(400, (await api.Post("/branches", new { code = "X", nameAr = "حالة", status = "closed" })).Status);

        // الافتراضي: لا يُلغى ولا يوقف ولا يُحذف
        Assert.Equal(409, (await api.Put($"/branches/{mainId}", new { code = "BR-001", nameAr = "الرئيسي", isDefault = false, status = "active" })).Status);
        Assert.Equal(409, (await api.Put($"/branches/{mainId}", new { code = "BR-001", nameAr = "الرئيسي", isDefault = true, status = "inactive" })).Status);
        Assert.Equal(409, (await api.Delete($"/branches/{mainId}")).Status);

        // فرع له موقع فعّال: لا يوقف ولا يُحذف؛ بعد نقل موقعه يُحذف
        var west = await BranchAsync(api, "W");
        var loc = (await api.Post("/warehouses", Location("W1", west))).Data!["id"].G();
        Assert.Equal(409, (await api.Put($"/branches/{west}", new { code = "W", nameAr = "فرع W", status = "inactive" })).Status);
        Assert.Equal(409, (await api.Delete($"/branches/{west}")).Status);
        Assert.Equal(200, (await api.Put($"/warehouses/{loc}", Location("W1", mainId))).Status);
        Assert.Equal(200, (await api.Put($"/branches/{west}", new { code = "W", nameAr = "فرع W", status = "inactive" })).Status);
        Assert.Equal(200, (await api.Delete($"/branches/{west}")).Status);
        Assert.Equal(404, (await api.Get($"/branches/{west}")).Status);

        // تعيين فرع آخر افتراضياً ينقل الصفة إليه وحده
        var south = await BranchAsync(api, "S", isDefault: true);
        var all = (await api.Get("/branches")).Data!["items"]!.AsArray();
        Assert.Equal(south, Assert.Single(all, x => x!["isDefault"]!.GetValue<bool>())!["id"].G());
    }

    [Fact]
    public async Task Branches_are_isolated_between_activities()
    {
        var a = await NewTenantAsync("نشاط أ");
        var b = await NewTenantAsync("نشاط ب");
        var branchA = await BranchAsync(a, "ISO");

        Assert.DoesNotContain((await b.Get("/branches")).Data!["items"]!.AsArray(), x => x!["id"].G() == branchA);
        Assert.Equal(404, (await b.Get($"/branches/{branchA}")).Status);
        Assert.Equal(404, (await b.Put($"/branches/{branchA}", new { code = "ISO", nameAr = "اختراق", status = "active" })).Status);
        Assert.Equal(404, (await b.Delete($"/branches/{branchA}")).Status);
        // موقع في النشاط ب لا يُربط بفرع من النشاط أ (والرمز نفسه مسموح في كل نشاط)
        Assert.Equal(400, (await b.Post("/warehouses", Location("X1", branchA))).Status);
        Assert.Equal(201, (await b.Post("/branches", new { code = "ISO", nameAr = "فرع ب", status = "active" })).Status);
    }

    [Fact]
    public async Task Branch_screen_inherits_location_permissions()
    {
        var owner = await NewTenantAsync();
        var email = $"s{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await owner.Post("/users", new { name = "مندوب", email, phone = "1", password = "Passw0rd!", role = "sales_rep" })).Status);
        var rep = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");

        // الفروع ترث صلاحية المواقع: المندوب يعرض المواقع ويضيفها بلا حذف، فكذلك الفروع
        var mainId = (await owner.Get("/branches")).Data!["items"]!.AsArray().Single()!["id"].G();
        Assert.Equal(200, (await rep.Get("/warehouses")).Status);
        Assert.Equal(200, (await rep.Get("/branches")).Status);
        var created = await rep.Post("/branches", new { code = "R", nameAr = "فرع المندوب", status = "active" });
        Assert.Equal(201, created.Status);
        Assert.Equal(403, (await rep.Delete($"/branches/{created.Data!["id"].G()}")).Status);
        Assert.Equal(403, (await rep.Delete($"/branches/{mainId}")).Status);
    }

    [Fact]
    public async Task Role_without_location_access_has_no_branch_access()
    {
        var owner = await NewTenantAsync();
        var email = $"e{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await owner.Post("/users", new { name = "موظف", email, phone = "1", password = "Passw0rd!", role = "employee" })).Status);
        var employee = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");
        Assert.Equal(403, (await employee.Get("/warehouses")).Status);
        Assert.Equal(403, (await employee.Get("/branches")).Status);
        Assert.Equal(403, (await employee.Post("/branches", new { code = "E", nameAr = "فرع", status = "active" })).Status);
    }
}
