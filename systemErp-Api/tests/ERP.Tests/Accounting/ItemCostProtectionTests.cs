using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// حماية تكلفة الأصناف: الرصيد والمتوسط وآخر تكلفة شراء لا تتغير إلا بالحركات؛ والتكلفة المعيارية لصنف مستخدم
/// لا يغيّرها إلا صاحب صلاحية «تعديل التكلفة المعيارية لصنف مستخدم»، وبالتكلفة المعيارية يُرحَّل قيد إعادة تقييم.
/// </summary>
[Collection("api")]
public class ItemCostProtectionTests : TestBase
{
    public ItemCostProtectionTests(ErpFactory f) : base(f) { }

    private const string Screen = "item-standard-cost-revaluation";

    private static object Policy(string method)
        => new { method, recalculateOnNewPurchase = true, includeFreightAndCustoms = false, negativeInventoryPolicy = "prohibit", standardCostVarianceAccountCode = "513" };

    private async Task<Client> UserAsync(Client owner, string role)
    {
        var email = $"u{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await owner.Post("/users", new { name = "مستخدم", email, phone = "1", password = "Passw0rd!", role })).Status);
        var user = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");
        Assert.NotNull(user.Token);
        return user;
    }

    private static async Task<JsonObject> ProductAsync(Client api, Guid id) => (await api.Get($"/products/{id}")).Data!.AsObject();

    /// <summary>تعديل الصنف كما ترسله شاشة الأصناف: كل الحقول الحالية مع ما يُغيَّر.</summary>
    private static async Task<Res> UpdateAsync(Client who, Client owner, Guid id, decimal? standardCost, decimal? sellingPrice = null,
        decimal? averageCost = null, decimal? currentStock = null)
    {
        var p = await ProductAsync(owner, id);
        return await who.Put($"/products/{id}", new
        {
            sku = p["sku"].S(), nameAr = p["nameAr"].S(), category = "C1", unit = "PCS", vatRate = 15,
            sellingPrice = sellingPrice ?? p["sellingPrice"].D(), standardCost,
            averageCost = averageCost ?? p["averageCost"].D(), currentStock = currentStock ?? p["currentStock"].D(),
        });
    }

    private static async Task<bool> UsedAsync(Client api, Guid id) => (await api.Get($"/products/{id}/usage")).Data!["used"]!.GetValue<bool>();

    [Fact]
    public async Task Standard_cost_of_an_unused_item_is_editable_and_of_a_used_item_needs_the_revaluation_permission()
    {
        var owner = await NewTenantAsync();
        var rep = await UserAsync(owner, "sales_rep");
        var fresh = await SeedProductAsync(owner, "NEW", stock: 0);
        var used = await SeedProductAsync(owner, "USED", stock: 10, cost: 40);

        // صنف لم يُستخدم: يعدّله صاحب صلاحية البيانات الأساسية كما يشاء
        Assert.False(await UsedAsync(owner, fresh));
        Assert.Equal(200, (await UpdateAsync(rep, owner, fresh, 30)).Status);
        Assert.Equal(200, (await UpdateAsync(rep, owner, fresh, 35)).Status);
        Assert.Equal(35, (await ProductAsync(owner, fresh))["standardCost"].D());

        // صنف مستخدم: التكلفة المعيارية مقفلة بلا الصلاحية، وبقية الحقول تُعدَّل
        Assert.True(await UsedAsync(owner, used));
        Assert.Equal(403, (await UpdateAsync(rep, owner, used, 45)).Status);
        Assert.Null((await ProductAsync(owner, used))["standardCost"]);
        Assert.Equal(200, (await UpdateAsync(rep, owner, used, null, sellingPrice: 120)).Status);
        Assert.Equal(120, (await ProductAsync(owner, used))["sellingPrice"].D());

        // الرصيد والمتوسط لا يتغيران من شاشة الصنف (حتى لو أُرسلا)
        Assert.Equal(200, (await UpdateAsync(rep, owner, used, null, averageCost: 999, currentStock: 500)).Status);
        var p = await ProductAsync(owner, used);
        Assert.Equal(40, p["averageCost"].D()); Assert.Equal(10, p["currentStock"].D());

        // منح الصلاحية صراحةً يسمح بالتعديل
        var perms = (await owner.Get("/permissions?roleId=sales_rep")).Data!.AsArray();
        Assert.False(perms.Single(x => x!["screenId"].S() == Screen)!["canEdit"]!.GetValue<bool>()); // مقفلة افتراضياً لغير الإدارة
        var granted = perms.Select(x => new
        {
            screenId = x!["screenId"].S(), screenNameAr = x["screenNameAr"].S(), screenNameEn = x["screenNameEn"].S(),
            canView = x["screenId"].S() == Screen || x["canView"]!.GetValue<bool>(), canCreate = x["canCreate"]!.GetValue<bool>(),
            canEdit = x["screenId"].S() == Screen || x["canEdit"]!.GetValue<bool>(),
            canDelete = x["canDelete"]!.GetValue<bool>(), canApprove = x["canApprove"]!.GetValue<bool>(),
        }).ToList();
        Assert.Equal(200, (await owner.Post("/permissions", new { roleId = "sales_rep", permissions = granted })).Status);
        Assert.Equal(200, (await UpdateAsync(rep, owner, used, 45)).Status);
        Assert.Equal(45, (await ProductAsync(owner, used))["standardCost"].D());
    }

    [Fact]
    public async Task Changing_the_standard_cost_of_a_used_item_posts_a_revaluation_and_purchases_still_move_the_cost()
    {
        var owner = await NewTenantAsync();
        var item = await SeedProductAsync(owner, "STD", stock: 10, cost: 40);
        Assert.Equal(400, (await owner.Get("/accounts/ByCode/1141")).Data!["balance"].D());

        // بالتكلفة المعيارية: صاحب الصلاحية يغيّرها فيُعاد تقييم الرصيد بقيد
        Assert.Equal(200, (await owner.Put("/costing/policy", Policy("standard"))).Status);
        Assert.Equal(200, (await UpdateAsync(owner, owner, item, 48)).Status);
        Assert.Equal(48, (await ProductAsync(owner, item))["averageCost"].D());
        Assert.Equal(480, (await owner.Get("/accounts/ByCode/1141")).Data!["balance"].D());

        // الشراء الجديد بالمتوسط المتحرك يغيّر التكلفة طبيعياً (القفل على التعديل اليدوي فقط)
        Assert.Equal(200, (await owner.Put("/costing/policy", Policy("moving_average"))).Status);
        var (supplier, _) = await SeedSupplierAsync(owner);
        Assert.Equal(201, (await owner.PostInvoice(new { kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier,
            items = new[] { new { itemId = item, quantity = 10, unitPrice = 60, vatRate = 15 } } })).Status);
        var p = await ProductAsync(owner, item);
        Assert.Equal(20, p["currentStock"].D());
        Assert.Equal(50, p["averageCost"].D());
    }
}
