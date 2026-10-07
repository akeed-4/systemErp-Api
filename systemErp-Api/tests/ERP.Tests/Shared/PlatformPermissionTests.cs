using ERP.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ERP.Tests;

/// <summary>
/// صلاحية إدارة المنصة من قائمة الصلاحيات: يمنحها مدير منصة لمستخدم محدد، ولا يراها ولا يمنحها مدير أي منشأة.
/// </summary>
[Collection("api")]
public class PlatformPermissionTests : TestBase
{
    private const string Screen = "platform-admin";

    public PlatformPermissionTests(ErpFactory f) : base(f) { }

    /// <summary>المدير الجذر: بريده ضمن Platform:AdminEmails.</summary>
    private async Task<Client> RootAdminAsync()
    {
        var admin = await NewTenantAsync("شركة مدير المنصة");
        var host = Factory.WithWebHostBuilder(b => b.UseSetting("Platform:AdminEmails:0", admin.Email));
        return await admin.LoginAsAsync(host.CreateClient(), admin.Email, "Passw0rd!");
    }

    private async Task<(Client Client, Guid Id)> NewUserAsync(Client owner, string role = "sales_rep")
    {
        var email = $"u{Guid.NewGuid():N}@test.com";
        var created = await owner.Post("/users", new { name = "موظف", email, phone = "1", password = "Passw0rd!", role });
        Assert.Equal(200, created.Status);
        return (await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!"), created.Data!["id"].G());
    }

    /// <summary>يعيد إرسال شبكة صلاحيات المستخدم كما هي مع ضبط سطر المنصة (يُضاف إن لم يكن ضمن الشبكة).</summary>
    private static async Task<Res> SaveWithPlatformAsync(Client by, Guid userId, bool view, bool edit)
    {
        var grid = (await by.Get($"/permissions?userId={userId}")).Data!.AsArray();
        var permissions = grid.Where(p => p!["screenId"].S() != Screen).Select(p => (object)new
        {
            screenId = p!["screenId"].S(),
            canView = p["canView"]!.GetValue<bool>(), canCreate = p["canCreate"]!.GetValue<bool>(),
            canEdit = p["canEdit"]!.GetValue<bool>(), canDelete = p["canDelete"]!.GetValue<bool>(), canApprove = p["canApprove"]!.GetValue<bool>(),
        }).ToList();
        permissions.Add(new { screenId = Screen, canView = view, canCreate = false, canEdit = edit, canDelete = false, canApprove = false });
        return await by.Post("/permissions", new { userId, permissions });
    }

    private static async Task<bool> GridHasPlatformRowAsync(Client by, string query)
        => (await by.Get($"/permissions?{query}")).Data!.AsArray().Any(p => p!["screenId"].S() == Screen);

    [Fact]
    public async Task Tenant_owner_neither_sees_nor_grants_the_platform_permission()
    {
        var owner = await NewTenantAsync();
        var (staff, staffId) = await NewUserAsync(owner);

        Assert.False(await GridHasPlatformRowAsync(owner, $"userId={staffId}"));
        Assert.False(await GridHasPlatformRowAsync(owner, "roleId=owner"));
        Assert.DoesNotContain((await owner.Get("/permissions/screens")).Data!.AsArray(), s => s!["id"].S() == Screen);

        Assert.Equal(403, (await SaveWithPlatformAsync(owner, staffId, view: true, edit: true)).Status);
        var roleGrant = new[] { new { screenId = Screen, canView = true, canCreate = true, canEdit = true, canDelete = true, canApprove = true } };
        Assert.Equal(403, (await owner.Post("/permissions", new { roleId = "owner", permissions = roleGrant })).Status);

        foreach (var user in new[] { owner, staff })
        {
            Assert.False((await user.Get("/platform/access")).Data!["isPlatformAdmin"]!.GetValue<bool>());
            Assert.Equal(403, (await user.Get("/platform/tenants")).Status);
        }
    }

    [Fact]
    public async Task View_grant_opens_the_dashboard_read_only()
    {
        var root = await RootAdminAsync();
        var other = await NewTenantAsync("شركة عميلة");
        var (staff, staffId) = await NewUserAsync(root);

        Assert.True(await GridHasPlatformRowAsync(root, $"userId={staffId}"));
        Assert.False(await GridHasPlatformRowAsync(root, "roleId=sales_rep"));
        Assert.Equal(200, (await SaveWithPlatformAsync(root, staffId, view: true, edit: false)).Status);

        var access = (await staff.Get("/platform/access")).Data!;
        Assert.True(access["isPlatformAdmin"]!.GetValue<bool>());
        Assert.False(access["canManage"]!.GetValue<bool>());
        Assert.Equal(200, (await staff.Get("/platform/tenants")).Status);
        Assert.Equal(200, (await staff.Get("/platform/plans")).Status);
        Assert.Equal(403, (await staff.Post($"/platform/tenants/{other.TenantId}/subscriptions/extend", new { days = 30 })).Status);

        // صلاحية العرض لا تمنح لغيره، ولا تظهر ضمن صلاحيات الشاشات العادية
        var (_, colleagueId) = await NewUserAsync(root);
        Assert.Equal(403, (await SaveWithPlatformAsync(staff, colleagueId, view: true, edit: false)).Status);
        Assert.DoesNotContain((await staff.Get("/permissions/me")).Data!.AsArray(), p => p!["screenId"].S() == Screen);
    }

    [Fact]
    public async Task Edit_grant_manages_subscriptions_and_can_be_revoked()
    {
        var root = await RootAdminAsync();
        var other = await NewTenantAsync("شركة عميلة");
        var (staff, staffId) = await NewUserAsync(root);

        Assert.Equal(200, (await SaveWithPlatformAsync(root, staffId, view: true, edit: true)).Status);
        Assert.True((await staff.Get("/platform/access")).Data!["canManage"]!.GetValue<bool>());
        Assert.Equal(200, (await staff.Post($"/platform/tenants/{other.TenantId}/subscriptions/extend", new { days = 30 })).Status);

        Assert.Equal(200, (await SaveWithPlatformAsync(root, staffId, view: false, edit: false)).Status);
        Assert.False((await staff.Get("/platform/access")).Data!["isPlatformAdmin"]!.GetValue<bool>());
        Assert.Equal(403, (await staff.Get("/platform/tenants")).Status);
        Assert.Equal(403, (await staff.Post($"/platform/tenants/{other.TenantId}/subscriptions/extend", new { days = 30 })).Status);
    }

    [Fact]
    public async Task Platform_permission_is_per_user_not_per_role()
    {
        var root = await RootAdminAsync();
        var grant = new[] { new { screenId = Screen, canView = true, canCreate = false, canEdit = true, canDelete = false, canApprove = false } };
        Assert.Equal(400, (await root.Post("/permissions", new { roleId = "sales_rep", permissions = grant })).Status);
    }

    [Fact]
    public async Task Tenant_admin_saving_other_permissions_keeps_the_platform_grant()
    {
        var root = await RootAdminAsync();
        var (staff, staffId) = await NewUserAsync(root);
        var (tenantAdmin, _) = await NewUserAsync(root, "admin");
        Assert.Equal(200, (await SaveWithPlatformAsync(root, staffId, view: true, edit: false)).Status);

        // مدير المنشأة (ليس مدير منصة) لا يرى السطر، وحفظه لباقي الصلاحيات لا يسحب المنحة
        Assert.False(await GridHasPlatformRowAsync(tenantAdmin, $"userId={staffId}"));
        var grid = (await tenantAdmin.Get($"/permissions?userId={staffId}")).Data!.AsArray();
        var permissions = grid.Select(p => new
        {
            screenId = p!["screenId"].S(),
            canView = p["canView"]!.GetValue<bool>(), canCreate = p["canCreate"]!.GetValue<bool>(),
            canEdit = p["canEdit"]!.GetValue<bool>(), canDelete = p["canDelete"]!.GetValue<bool>(), canApprove = p["canApprove"]!.GetValue<bool>(),
        }).ToList();
        Assert.Equal(200, (await tenantAdmin.Post("/permissions", new { userId = staffId, permissions })).Status);

        Assert.True((await staff.Get("/platform/access")).Data!["isPlatformAdmin"]!.GetValue<bool>());
        Assert.Equal(200, (await staff.Get("/platform/tenants")).Status);
    }
}
