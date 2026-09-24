using ERP.Tests.Infrastructure;

namespace ERP.Tests;

[Collection("api")]
public class PermissionTests : TestBase
{
    public PermissionTests(ErpFactory f) : base(f) { }

    private async Task<Client> SalesRepAsync(Client owner)
    {
        var email = $"s{Guid.NewGuid():N}@test.com";
        var created = await owner.Post("/users", new { name = "مندوب", email, phone = "1", password = "Passw0rd!", role = "sales_rep" });
        Assert.Equal(200, created.Status);
        var rep = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");
        Assert.NotNull(rep.Token);
        return rep;
    }

    [Fact]
    public async Task Sales_rep_follows_the_default_screen_permissions()
    {
        var owner = await NewTenantAsync();
        var (customerId, _) = await SeedCustomerAsync(owner);
        var rep = await SalesRepAsync(owner);

        Assert.Equal(200, (await rep.Get("/customers")).Status);              // master-data: view
        Assert.Equal(403, (await rep.Delete($"/customers/{customerId}")).Status); // لا حذف
        Assert.Equal(403, (await rep.Get("/accounts")).Status);               // accounts محجوبة
        Assert.Equal(403, (await rep.Get("/reports/trial-balance")).Status);  // reports محجوبة
        Assert.Equal(403, (await rep.Get("/permissions?roleId=owner")).Status);
        Assert.Equal(403, (await rep.Get("/approval-policies")).Status);
    }

    [Fact]
    public async Task Owner_can_change_role_permissions_and_they_take_effect()
    {
        var owner = await NewTenantAsync();
        var rep = await SalesRepAsync(owner);
        Assert.Equal(403, (await rep.Get("/accounts")).Status);

        var current = (await owner.Get("/permissions?roleId=sales_rep")).Data!.AsArray();
        var updated = current.Select(p => new
        {
            screenId = p!["screenId"].S(), screenNameAr = p["screenNameAr"].S(), screenNameEn = p["screenNameEn"].S(),
            canView = p["screenId"].S() == "accounts" || p["canView"]!.GetValue<bool>(), canCreate = p["canCreate"]!.GetValue<bool>(),
            canEdit = p["canEdit"]!.GetValue<bool>(), canDelete = p["canDelete"]!.GetValue<bool>(), canApprove = p["canApprove"]!.GetValue<bool>(),
        }).ToList();
        Assert.Equal(200, (await owner.Post("/permissions", new { roleId = "sales_rep", permissions = updated })).Status);
        Assert.Equal(200, (await rep.Get("/accounts")).Status);
    }

    [Fact]
    public async Task Sales_rep_cannot_apply_manual_pos_discounts()
    {
        var owner = await NewTenantAsync();
        var product = await SeedProductAsync(owner);
        var rep = await SalesRepAsync(owner);
        Assert.Equal(200, (await rep.Post("/pos/shifts/open", new { openingCash = 100, posTerminalName = "T1" })).Status);
        var r = await rep.Post("/pos/transactions/checkout", new { items = new[] { new { itemId = product, quantity = 1, manualDiscount = 5 } }, paymentMethod = "cash", paidCash = 200 });
        Assert.Equal(403, r.Status);
    }

    [Fact]
    public async Task Users_endpoints_never_expose_password_hashes_and_owner_cannot_be_deactivated()
    {
        var owner = await NewTenantAsync();
        var list = await owner.Get("/users");
        Assert.All(list.Data!.AsArray(), u => Assert.Null(u!["passwordHash"]));
        var ownerId = (await owner.Get("/auth/me")).Data!["id"].S();
        Assert.Equal(409, (await owner.Delete($"/users/{ownerId}")).Status);
    }
}
