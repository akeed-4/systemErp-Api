using ERP.Core.Models.Accounting;
using ERP.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Tests;

/// <summary>
/// مواقع المستخدم (المعارض/المستودعات) وموقع الجلسة: الختم بموقع الجلسة، رفض الموقع غير المسموح، قوائم المستندات
/// حسب المعرض أو حسب المستخدم (إعداد المنشأة)، وفتح مستند من موقع غير مسموح.
/// </summary>
[Collection("api")]
public class WarehouseSessionTests : TestBase
{
    public WarehouseSessionTests(ErpFactory f) : base(f) { }

    private static async Task<Guid> WarehouseAsync(Client owner, string code)
    {
        var r = await owner.Post("/warehouses", new { code, nameAr = "معرض " + code, nameEn = code, location = "الرياض", status = "active" });
        Assert.True(r.Status is 200 or 201, r.Body?.ToJsonString());
        return r.Data!["id"].G();
    }

    /// <summary>مستخدم بدور يملك العقود كاملة ومواقع محددة (فارغة = كل المواقع).</summary>
    private async Task<(Client Client, Guid Id)> UserAsync(Client owner, params Guid[] warehouses)
    {
        var role = (await owner.Post("/roles", new { nameAr = "موظف معرض " + Guid.NewGuid().ToString("N")[..6] })).Data!["code"].S();
        await owner.Post("/permissions", new
        {
            roleId = role,
            permissions = new[] { new { screenId = "commercial-contracts", screenNameAr = "", screenNameEn = "", actions = new[] { "view", "create", "edit", "submit" } } },
        });
        var roleId = (await owner.Get("/roles")).Data!.AsArray().First(r => r!["code"].S() == role)!["id"].G();
        var email = $"s{Guid.NewGuid():N}@test.com";
        var created = await owner.Post("/users", new { name = "موظف", email, phone = "1", password = "Passw0rd!", role = "sales_rep", customRoleId = roleId, allowedWarehouseIds = warehouses });
        Assert.Equal(200, created.Status);
        return (await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!"), created.Data!["id"].G());
    }

    private static async Task<Res> ContractAsync(Client api, string title)
        => await api.Post("/commercialcontracts", new
        {
            title, contractType = "supply_materials", partyName = "عميل", contractValue = 100,
            items = Array.Empty<object>(), milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
        });

    private static async Task<List<string>> TitlesAsync(Client api)
        => (await api.Get("/commercialcontracts?pageSize=100")).Data!["items"]!.AsArray().Select(c => c!["title"].S()).ToList();

    private async Task<Guid?> StoredWarehouseAsync(string id)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>();
        return (await db.Set<CommercialContract>().IgnoreQueryFilters().FirstAsync(c => c.Id == Guid.Parse(id))).WarehouseId;
    }

    [Fact]
    public async Task Session_lists_allowed_warehouses_and_new_documents_take_the_session_warehouse()
    {
        var owner = await NewTenantAsync();
        var c = (await owner.Get("/warehouses")).Data!["items"]!.AsArray().Select(w => w!["id"].G()).First(); // المستودع الافتراضي
        var a = await WarehouseAsync(owner, "SA"); var b = await WarehouseAsync(owner, "SB");
        var (user, userId) = await UserAsync(owner, a, b);
        Assert.Equal(new[] { a, b }.OrderBy(x => x), (await owner.Get($"/users/{userId}")).Data!["allowedWarehouseIds"]!.AsArray().Select(x => x!.G()).OrderBy(x => x));

        var session = (await user.Get("/warehouse-session")).Data!;
        Assert.False(session["allWarehouses"]!.GetValue<bool>());
        Assert.Equal(new[] { a, b }.OrderBy(x => x), session["warehouses"]!.AsArray().Select(s => s!["id"].G()).OrderBy(x => x));
        Assert.True((await owner.Get("/warehouse-session")).Data!["allWarehouses"]!.GetValue<bool>()); // المالك: كل المواقع

        // موقعان ولم يختر: لا مستند بلا موقع
        Assert.Equal(400, (await ContractAsync(user, "بلا موقع")).Status);
        // موقع غير مسموح في الترويسة يُتجاهل (فيبقى بلا موقع)
        user.WarehouseId = c;
        Assert.Equal(400, (await ContractAsync(user, "موقع ممنوع")).Status);

        user.WarehouseId = b;
        Assert.Equal(b, (await user.Get("/warehouse-session")).Data!["currentWarehouseId"].G());
        var created = await ContractAsync(user, "في ب");
        Assert.Equal(201, created.Status);
        Assert.Equal(b, await StoredWarehouseAsync(created.Data!["id"].S()));

        // موقع واحد: يُضبط تلقائياً بلا اختيار
        var (single, _) = await UserAsync(owner, a);
        Assert.Equal(a, (await single.Get("/warehouse-session")).Data!["currentWarehouseId"].G());
        Assert.Equal(a, await StoredWarehouseAsync((await ContractAsync(single, "في أ")).Data!["id"].S()));
    }

    [Fact]
    public async Task By_warehouse_lists_only_the_session_warehouse_and_other_warehouses_are_forbidden()
    {
        var owner = await NewTenantAsync();
        var a = await WarehouseAsync(owner, "SA"); var b = await WarehouseAsync(owner, "SB");
        owner.WarehouseId = a; await ContractAsync(owner, "عقد أ");
        owner.WarehouseId = b; var inB = (await ContractAsync(owner, "عقد ب")).Data!["id"].S();

        Assert.Equal(new[] { "عقد ب" }, await TitlesAsync(owner)); // حسب المعرض: موقع الجلسة فقط
        owner.WarehouseId = null;
        Assert.Equal(2, (await TitlesAsync(owner)).Count); // «كل المواقع»

        var (user, _) = await UserAsync(owner, a);
        Assert.Equal(new[] { "عقد أ" }, await TitlesAsync(user));
        Assert.Equal(403, (await user.Get($"/commercialcontracts/{inB}")).Status);
    }

    [Fact]
    public async Task By_user_lists_only_own_documents_within_allowed_warehouses()
    {
        var owner = await NewTenantAsync();
        var a = await WarehouseAsync(owner, "SA");
        var (first, _) = await UserAsync(owner, a);
        var (second, _) = await UserAsync(owner, a);
        await ContractAsync(first, "للأول");
        await ContractAsync(second, "للثاني");

        Assert.Equal(2, (await TitlesAsync(first)).Count); // الافتراضي: حسب المعرض
        var bad = await owner.Put("/warehouse-session/document-visibility", new { documentVisibility = "x" });
        Assert.True(bad.Status == 400, bad.Body?.ToJsonString());
        Assert.Equal(403, (await first.Put("/warehouse-session/document-visibility", new { documentVisibility = "user" })).Status);
        Assert.Equal(200, (await owner.Put("/warehouse-session/document-visibility", new { documentVisibility = "user" })).Status);

        Assert.Equal(new[] { "للأول" }, await TitlesAsync(first));
        Assert.Equal(new[] { "للثاني" }, await TitlesAsync(second));
        Assert.Equal(2, (await TitlesAsync(owner)).Count); // الصلاحية الكاملة لا يقيّدها «حسب المستخدم»
        Assert.Equal("user", (await first.Get("/warehouse-session")).Data!["documentVisibility"].S());
    }
}
