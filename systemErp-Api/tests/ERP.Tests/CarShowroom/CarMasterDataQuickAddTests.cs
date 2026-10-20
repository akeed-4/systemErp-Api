using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// الإضافة السريعة من بطاقة السيارة: كل قائمة مرجعية (الماركة، الوكيل، الموديل، الفئة، السنة، الألوان) ترفض التكرار في السرفر
/// ولو بمسافات زائدة، وتتطلب صلاحية الإضافة على شاشة المعارض.
/// </summary>
[Collection("api")]
public class CarMasterDataQuickAddTests : TestBase
{
    public CarMasterDataQuickAddTests(ErpFactory f) : base(f) { }

    [Fact]
    public async Task Each_reference_list_rejects_duplicates_even_with_extra_spaces()
    {
        var api = await NewTenantAsync();
        var brand = await api.Post("/carbrands", new { nameAr = "جينيسيس", country = "كوريا" });
        Assert.Equal(201, brand.Status);
        Assert.Equal("جينيسيس", brand.Data!["nameAr"].S());
        Assert.Equal(409, (await api.Post("/carbrands", new { nameAr = "  جينيسيس ", country = "كوريا" })).Status);
        var brandId = brand.Data["id"].S();
        var other = (await api.Post("/carbrands", new { nameAr = "كيا", country = "كوريا" })).Data!["id"].S();

        // الوكيل: لا يتكرر للماركة نفسها، ويجوز الاسم نفسه لماركة أخرى
        var agent = await api.Post("/caragents", new { brandId, nameAr = "الوكيل الأول" });
        Assert.Equal(201, agent.Status);
        Assert.Equal(409, (await api.Post("/caragents", new { brandId, nameAr = "الوكيل الأول " })).Status);
        Assert.Equal(201, (await api.Post("/caragents", new { brandId = other, nameAr = "الوكيل الأول" })).Status);

        var model = await api.Post("/carmodels", new { brandId, agentId = agent.Data!["id"].S(), nameAr = "G80" });
        Assert.Equal(201, model.Status);
        Assert.Equal(409, (await api.Post("/carmodels", new { brandId, nameAr = " G80" })).Status);

        var modelId = model.Data!["id"].S();
        var trim = await api.Post("/cartrims", new { modelId, nameAr = "رويال" });
        Assert.Equal(201, trim.Status);
        Assert.Equal(409, (await api.Post("/cartrims", new { modelId, nameAr = "رويال  " })).Status);

        var trimId = trim.Data!["id"].S();
        Assert.Equal(201, (await api.Post("/caryears", new { trimId, year = 2025 })).Status);
        Assert.Equal(409, (await api.Post("/caryears", new { trimId, year = 2025 })).Status);

        // الألوان: الخارجي والداخلي قائمتان مستقلتان
        var color = await api.Post("/carcolors", new { nameAr = "أزرق ملكي", hex = "#1E3A8A", isExterior = true });
        Assert.Equal(201, color.Status);
        Assert.Equal(409, (await api.Post("/carcolors", new { nameAr = " أزرق ملكي", isExterior = true })).Status);
        Assert.Equal(201, (await api.Post("/carcolors", new { nameAr = "أزرق ملكي", isExterior = false })).Status);
        Assert.Equal(400, (await api.Post("/carcolors", new { nameAr = "أخضر", hex = "green", isExterior = true })).Status);
    }

    [Fact]
    public async Task Adding_reference_entries_needs_the_create_permission_on_the_showroom_screen()
    {
        var owner = await NewTenantAsync();
        var email = $"u{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await owner.Post("/users", new { name = "مستخدم", email, phone = "1", password = "Passw0rd!", role = "sales_rep" })).Status);
        var rep = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");
        Assert.Equal(201, (await rep.Post("/carcolors", new { nameAr = "فضي", isExterior = true })).Status);

        // سحب صلاحية الإضافة على شاشة المعارض: القراءة متاحة والإضافة مرفوضة
        var perms = (await owner.Get("/permissions?roleId=sales_rep")).Data!.AsArray().Select(p => new
        {
            screenId = p!["screenId"].S(), screenNameAr = p["screenNameAr"].S(), screenNameEn = p["screenNameEn"].S(),
            canView = p["canView"]!.GetValue<bool>(), canCreate = p["screenId"].S() != "car-showroom" && p["canCreate"]!.GetValue<bool>(),
            canEdit = p["canEdit"]!.GetValue<bool>(), canDelete = p["canDelete"]!.GetValue<bool>(), canApprove = p["canApprove"]!.GetValue<bool>(),
        }).ToList();
        Assert.Equal(200, (await owner.Post("/permissions", new { roleId = "sales_rep", permissions = perms })).Status);
        Assert.Equal(200, (await rep.Get("/carcolors")).Status);
        Assert.Equal(403, (await rep.Post("/carcolors", new { nameAr = "ذهبي", isExterior = true })).Status);
        Assert.Equal(403, (await rep.Post("/carbrands", new { nameAr = "لكزس", country = "اليابان" })).Status);
    }
}
