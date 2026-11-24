using ERP.Core.Models.Accounting;
using ERP.Core.Models.Shared;
using ERP.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Tests;

/// <summary>
/// حذف ملف الموظف بعد فحص ارتباطاته في معاملة واحدة: مسيرات الرواتب تمنعه برسالة تسمّي المسيرات، والسجلات التشغيلية تمنعه برسالة تسمّي نوعها؛
/// حساب البوابة المرتبط يُحذف من المستخدمين (مستخدم النظام يُعطَّل، في <see cref="HrUserSyncTests"/>)، وعضويات المشاريع تُزال
/// (<see cref="ProjectStockReservationTests"/>)، وما رُفض لا يُغيّر شيئاً.
/// </summary>
[Collection("api")]
public class HrEmployeeDeleteTests : TestBase
{
    public HrEmployeeDeleteTests(ErpFactory f) : base(f) { }

    private const string Password = "Passw0rd!";
    private static readonly DateTime Today = DateTime.UtcNow.AddHours(3).Date;
    private static readonly DateTime Month = new DateTime(Today.Year, Today.Month, 1).AddMonths(-1);

    private static async Task<string> EmployeeAsync(Client api, string code, string? email = null)
    {
        var r = await api.Post("/employees", new
        {
            code, nameAr = "موظف " + code, hireDate = Today.AddYears(-1).ToString("yyyy-MM-dd"), basicSalary = 4800, housingAllowance = 1200, email,
        });
        Assert.True(r.Status == 201, r.Body?.ToJsonString());
        return r.Data!["id"].S();
    }

    private async Task<T> WithDb<T>(Func<ERP.Service.Data.ErpDbContext, Task<T>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>());
    }

    [Fact]
    public async Task Deleting_the_employee_deletes_the_linked_portal_user_from_the_users_list()
    {
        var api = await NewTenantWithModulesAsync("hr");
        var email = $"p{Guid.NewGuid():N}@test.com";
        var id = await EmployeeAsync(api, "P1", email);
        var invite = await api.Post($"/employees/{id}/portal-access/invite");
        Assert.True(invite.Status == 200, invite.Body?.ToJsonString());
        var accept = await api.SendAsync(HttpMethod.Post, "/auth/Invite/Accept", new { token = invite.Data!["inviteToken"].S(), password = Password }, anonymous: true);
        Assert.Equal(200, accept.Status);
        var userId = (await api.Get($"/employees/{id}")).Data!["userId"].S();
        Assert.False(string.IsNullOrEmpty(userId));

        Assert.Equal(200, (await api.Delete($"/employees/{id}")).Status);

        Assert.Equal(404, (await api.Get($"/users/{userId}")).Status);
        Assert.DoesNotContain((await api.Get("/users")).Data!.AsArray(), u => u!["email"].S() == email);
        var leftovers = await WithDb(db => db.Set<UserInvite>().IgnoreQueryFilters().CountAsync(i => i.UserId == Guid.Parse(userId)));
        Assert.Equal(0, leftovers);
        // حساب البوابة المحذوف لا يدخل
        var login = await api.SendAsync(HttpMethod.Post, "/auth/login", new { email, password = Password }, anonymous: true);
        Assert.NotEqual(200, login.Status);
    }

    [Fact]
    public async Task Payroll_runs_block_the_delete_with_a_message_naming_them_and_nothing_changes()
    {
        var api = await NewTenantWithModulesAsync("hr");
        var id = await EmployeeAsync(api, "R1");
        var draft = await api.Post("/payroll/draft", new { period = Month.ToString("yyyy-MM"), adjustments = Array.Empty<object>() });
        Assert.True(draft.Status == 200, draft.Body?.ToJsonString());

        var rejected = await api.Delete($"/employees/{id}");
        Assert.Equal(409, rejected.Status);
        Assert.Contains(draft.Data!["runNumber"].S(), rejected.Body!["message"].S());
        Assert.Equal(200, (await api.Get($"/employees/{id}")).Status);
    }

    [Fact]
    public async Task Operational_links_block_the_delete_and_the_transaction_leaves_memberships_untouched()
    {
        var api = await NewTenantWithModulesAsync("accounting", "hr");
        var id = await EmployeeAsync(api, "O1");
        var contract = await api.Post("/commercialcontracts", new
        {
            title = "عقد", contractType = "maintenance", partyName = "عميل", contractValue = 1000, vatRate = 15,
            startDate = DateTime.UtcNow.Date, endDate = DateTime.UtcNow.Date.AddMonths(12), milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
        });
        Assert.True(contract.Status is 200 or 201, contract.Body?.ToJsonString());
        var contractId = contract.Data!["id"].G();
        var employeeId = Guid.Parse(id);
        await WithDb(async db =>
        {
            db.Add(new ProjectMember { TenantId = api.TenantId, ContractId = contractId, EmployeeId = employeeId });
            db.Add(new ProjectResource { TenantId = api.TenantId, ContractId = contractId, Name = "عامل", EmployeeId = employeeId });
            await db.SaveChangesAsync();
            return 0;
        });

        var rejected = await api.Delete($"/employees/{id}");
        Assert.Equal(409, rejected.Status);
        Assert.Contains("(1)", rejected.Body!["message"].S()); // نوع السجل وعدده
        // الرفض قبل أي حذف: العضوية باقية
        Assert.Equal(1, await WithDb(db => db.Set<ProjectMember>().IgnoreQueryFilters().CountAsync(m => m.EmployeeId == employeeId)));

        await WithDb(async db =>
        {
            db.RemoveRange(await db.Set<ProjectResource>().IgnoreQueryFilters().Where(r => r.EmployeeId == employeeId).ToListAsync());
            await db.SaveChangesAsync();
            return 0;
        });
        Assert.Equal(200, (await api.Delete($"/employees/{id}")).Status);
        Assert.Equal(0, await WithDb(db => db.Set<ProjectMember>().IgnoreQueryFilters().CountAsync(m => m.EmployeeId == employeeId)));
    }
}
