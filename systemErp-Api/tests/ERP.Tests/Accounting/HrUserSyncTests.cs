using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// ملف الموظف مصدر المستخدم المربوط به (حساب البوابة أو مستخدم النظام): الاسم والحروف والقسم والمسمى تتبعه، والحذف وانتهاء الخدمة
/// يعطّلانه (المالك لا يُعطَّل)، واسم المستخدم المربوط لا يُعدَّل من شاشة المستخدمين ولا من الملف الشخصي.
/// </summary>
[Collection("api")]
public class HrUserSyncTests : TestBase
{
    public HrUserSyncTests(ErpFactory f) : base(f) { }

    private const string Password = "Passw0rd!";
    private static string Day(int offset) => DateTime.UtcNow.Date.AddDays(offset).ToString("yyyy-MM-dd");

    /// <summary>مستخدم نظام (محاسب) ثم موظف ببريده نفسه مربوط به.</summary>
    private static async Task<(string EmployeeId, string UserId, string Email)> LinkedSystemUserAsync(Client api, string code)
    {
        var email = $"u{Guid.NewGuid():N}@test.com";
        var user = await api.Post("/users", new { name = "اسم قديم", email, phone = "1", password = Password, role = "admin" });
        Assert.True(user.Status == 200, user.Body?.ToJsonString());
        var employeeId = await EmployeeAsync(api, code, "أحمد علي", email);
        return (employeeId, user.Data!["id"].S(), email);
    }

    private static async Task<string> EmployeeAsync(Client api, string code, string nameAr, string email)
    {
        var r = await api.Post("/employees", new { code, nameAr, hireDate = Day(-800), basicSalary = 6000, email, jobTitle = "محاسب" });
        Assert.True(r.Status == 201, r.Body?.ToJsonString());
        var id = r.Data!["id"].S();
        if (string.IsNullOrEmpty((await api.Get($"/employees/{id}")).Data!["userId"].S()))
            Assert.Equal(200, (await api.Post($"/employees/{id}/portal-access/invite")).Status);
        return id;
    }

    private static async Task<JsonNode> UserAsync(Client api, string userId) => (await api.Get($"/users/{userId}")).Data!;

    private static async Task<int> RenameAsync(Client api, string employeeId, string nameAr, string? status = null)
    {
        var employee = (await api.Get($"/employees/{employeeId}")).Data!.AsObject();
        employee["nameAr"] = nameAr;
        if (status != null) { employee["status"] = status; employee["terminationDate"] = Day(0); }
        return (await api.Put($"/employees/{employeeId}", employee)).Status;
    }

    [Fact]
    public async Task Linking_and_renaming_the_employee_renames_the_system_user_in_the_users_list()
    {
        var api = await NewTenantWithModulesAsync("hr");
        var (employeeId, userId, _) = await LinkedSystemUserAsync(api, "E1");

        // من لحظة الربط: الاسم من ملف الموظف
        var linked = await UserAsync(api, userId);
        Assert.Equal("أحمد علي", linked["name"].S());
        Assert.Equal(employeeId, linked["employeeId"].S());
        Assert.Equal("admin", linked["role"].S()); // دوره في النظام لا يتغيّر

        Assert.Equal(200, await RenameAsync(api, employeeId, "أحمد علي الزهراني"));
        var row = (await api.Get("/users")).Data!.AsArray().Single(u => u!["id"].S() == userId)!;
        Assert.Equal("أحمد علي الزهراني", row["name"].S());
        Assert.Equal(employeeId, row["employeeId"].S());
        Assert.False(string.IsNullOrEmpty(row["avatarInitials"].S()));
        Assert.Equal("محاسب", row["jobTitle"].S());
        Assert.True((bool)row["isActive"]!);
    }

    [Fact]
    public async Task Portal_account_follows_the_employee_name_too()
    {
        var api = await NewTenantWithModulesAsync("hr");
        var employeeId = await EmployeeAsync(api, "E2", "سارة", $"p{Guid.NewGuid():N}@test.com");
        var userId = (await api.Get($"/employees/{employeeId}")).Data!["userId"].S();

        Assert.Equal(200, await RenameAsync(api, employeeId, "سارة محمد"));
        Assert.Equal("سارة محمد", (await UserAsync(api, userId))["name"].S());
    }

    [Fact]
    public async Task Name_of_a_linked_user_is_locked_in_the_users_screen_and_profile()
    {
        var api = await NewTenantWithModulesAsync("hr");
        var (_, userId, email) = await LinkedSystemUserAsync(api, "E3");

        object Body(string name) => new { name, phone = "2", role = "admin", isActive = true, allowedWarehouseIds = Array.Empty<Guid>() };
        Assert.Equal(400, (await api.Put($"/users/{userId}", Body("اسم آخر"))).Status);
        // الاسم نفسه مع تعديل غيره مقبول
        var same = await api.Put($"/users/{userId}", Body("أحمد علي"));
        Assert.True(same.Status == 200, same.Body?.ToJsonString());
        Assert.Equal("2", same.Data!["phone"].S());

        var self = await api.LoginAsAsync(NewHttp(), email, Password);
        Assert.Equal(400, (await self.Put("/auth/profile", new { name = "اسمي الجديد" })).Status);
        Assert.Equal(200, (await self.Put("/auth/profile", new { phone = "3" })).Status);
        Assert.Equal("أحمد علي", (await UserAsync(api, userId))["name"].S());

        // مستخدم غير مربوط بموظف يبقى اسمه قابلاً للتعديل
        var free = await api.Post("/users", new { name = "حر", email = $"f{Guid.NewGuid():N}@test.com", phone = "1", password = Password, role = "admin" });
        Assert.Equal(200, (await api.Put($"/users/{free.Data!["id"].S()}", Body("حر معدّل"))).Status);
    }

    [Fact]
    public async Task Deleting_the_employee_deactivates_the_linked_system_user_but_never_the_owner()
    {
        var api = await NewTenantWithModulesAsync("hr");
        var (employeeId, userId, _) = await LinkedSystemUserAsync(api, "E4");
        Assert.Equal(200, (await api.Delete($"/employees/{employeeId}")).Status);
        var user = await UserAsync(api, userId);
        Assert.False((bool)user["isActive"]!);
        Assert.True(string.IsNullOrEmpty(user["employeeId"].S())); // زال الربط بزوال الملف

        // المالك موظف أيضاً: حذف ملفه لا يعطّل المالك
        var ownerEmployee = await EmployeeAsync(api, "E5", "المالك", api.Email);
        Assert.Equal(200, (await api.Delete($"/employees/{ownerEmployee}")).Status);
        var owner = (await api.Get("/users")).Data!.AsArray().Single(u => u!["email"].S() == api.Email.ToLower())!;
        Assert.True((bool)owner["isActive"]!);
    }

    [Fact]
    public async Task Terminating_the_employee_deactivates_the_user_once()
    {
        var api = await NewTenantWithModulesAsync("hr");
        var (employeeId, userId, _) = await LinkedSystemUserAsync(api, "E6");

        // «غير نشط» لا يعطّل المستخدم
        Assert.Equal(200, await RenameAsync(api, employeeId, "أحمد علي", "inactive"));
        Assert.True((bool)(await UserAsync(api, userId))["isActive"]!);

        Assert.Equal(200, await RenameAsync(api, employeeId, "أحمد علي", "terminated"));
        Assert.False((bool)(await UserAsync(api, userId))["isActive"]!);

        // أعاده المدير يدوياً: حفظ ملف الموظف المنتهي مجدداً لا يعطّله ثانية
        Assert.Equal(200, (await api.Post($"/users/{userId}/activate")).Status);
        Assert.Equal(200, await RenameAsync(api, employeeId, "أحمد علي"));
        Assert.True((bool)(await UserAsync(api, userId))["isActive"]!);
    }

    [Fact]
    public async Task End_of_service_settlement_deactivates_the_linked_user()
    {
        var api = await NewTenantWithModulesAsync("hr");
        var (employeeId, userId, _) = await LinkedSystemUserAsync(api, "E7");
        var posted = await api.Post("/endofservice", new { employeeId, lastWorkingDay = Day(0), reason = "resignation" });
        Assert.True(posted.Status == 200, posted.Body?.ToJsonString());
        Assert.False((bool)(await UserAsync(api, userId))["isActive"]!);
    }
}
