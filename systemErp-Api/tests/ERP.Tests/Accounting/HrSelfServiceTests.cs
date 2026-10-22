using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// بوابة الموظف الذاتية: كشوف الرواتب (سطره وحده في المسيرات المعتمدة)، طلب استكمال الإجازة وإعادة إرسالها دون
/// احتساب أيامها مرتين، اشتراط التقرير الطبي للإجازة المرضية، سجل حالات الطلب، إشعارات الموظف، وتسجيل الاطلاع على التقارير.
/// </summary>
[Collection("api")]
public class HrSelfServiceTests : TestBase
{
    public HrSelfServiceTests(ErpFactory f) : base(f) { }

    private const string Password = "Passw0rd!";
    private static string Day(DateTime d) => d.ToString("yyyy-MM-dd");
    private static readonly DateTime Today = DateTime.UtcNow.Date;

    private static async Task<(string Id, string Email)> EmployeeAsync(Client hr, string code, decimal basic = 6000, string? iban = null)
    {
        var email = $"e{Guid.NewGuid():N}@test.com";
        var r = await hr.Post("/employees", new
        {
            code, nameAr = "موظف " + code, hireDate = Day(Today.AddYears(-2)), basicSalary = basic, housingAllowance = 1500, email, iban,
            employeeGosiRate = 9.75, employerGosiRate = 11.75,
        });
        Assert.Equal(201, r.Status);
        return (r.Data!["id"].S(), email);
    }

    private async Task<Client> PortalAsync(Client hr, string employeeId, string email)
    {
        var invite = await hr.Post($"/employees/{employeeId}/portal-access/invite");
        Assert.Equal(200, invite.Status);
        Assert.Equal(200, (await hr.SendAsync(HttpMethod.Post, "/auth/Invite/Accept", new { token = invite.Data!["inviteToken"].S(), password = Password }, anonymous: true)).Status);
        return await hr.LoginAsAsync(NewHttp(), email, Password);
    }

    private static async Task<string> TypeAsync(Client portal, string code)
        => (await portal.Get("/me/leaves")).Data!["leaveTypes"]!.AsArray().Single(t => t!["code"].S() == code)!["id"].S();

    private static JsonNode Request(JsonNode overview, string id) => overview["requests"]!.AsArray().Single(r => r!["id"].S() == id)!;

    [Fact]
    public async Task Employee_sees_only_his_own_payslips_from_approved_payroll_runs()
    {
        var hr = await NewTenantWithModulesAsync("hr");
        var (one, email) = await EmployeeAsync(hr, "E1", 8000, "SA0380000000608010167519");
        await EmployeeAsync(hr, "E2", 4000);
        var me = await PortalAsync(hr, one, email);

        // مسودة مسير: لا تظهر للموظف قبل اعتمادها
        var draftPeriod = DateTime.UtcNow.AddMonths(-2).ToString("yyyy-MM");
        Assert.Equal(200, (await hr.Post("/payroll/draft", new { period = draftPeriod })).Status);
        Assert.Empty((await me.Get("/me/payroll")).Data!["payslips"]!.AsArray());

        var period = DateTime.UtcNow.AddMonths(-1).ToString("yyyy-MM");
        Assert.Equal(200, (await hr.Post("/payroll/post", new { period })).Status);
        var payroll = await me.Get("/me/payroll");
        Assert.Equal(200, payroll.Status);
        var p = payroll.Data!;
        Assert.Equal(9500, p["totalSalary"].D());
        Assert.EndsWith("7519", p["maskedIban"].S()); Assert.DoesNotContain("0380000", p["maskedIban"].S());
        var slips = p["payslips"]!.AsArray();
        var slip = Assert.Single(slips)!;
        Assert.Equal(period, slip["period"].S()); Assert.Equal("posted", slip["status"].S());
        Assert.Equal("E1", slip["employeeCode"].S()); // سطره وحده، لا E2
        Assert.Equal(9500 - 926.25m, slip["net"].D()); // التأمينات 9.75% من الأساسي والسكن (9500)

        // مسير معكوس يبقى في سجله بحالته
        var runId = slip["runId"].S();
        Assert.Equal(200, (await hr.Post($"/payroll/{runId}/reverse")).Status);
        Assert.Equal("reversed", Assert.Single((await me.Get("/me/payroll")).Data!["payslips"]!.AsArray())!["status"].S());
    }

    [Fact]
    public async Task Returned_sick_leave_is_completed_and_resubmitted_without_counting_its_days_twice()
    {
        var hr = await NewTenantWithModulesAsync("hr");
        var (id, email) = await EmployeeAsync(hr, "E1");
        var me = await PortalAsync(hr, id, email);
        var sick = await TypeAsync(me, "sick");
        var start = Today.AddDays(10);

        var created = await me.Post("/me/leaves", new { leaveTypeId = sick, startDate = Day(start), endDate = Day(start.AddDays(4)), reason = "حمى" });
        Assert.Equal(201, created.Status);
        var leaveId = created.Data!["id"].S();

        // بلا تقرير طبي لا تُعتمد؛ تُعاد للموظف بسبب مكتوب
        Assert.Equal(400, (await hr.Post($"/leaverequests/{leaveId}/approve", new { })).Status);
        Assert.Equal(400, (await hr.Post($"/leaverequests/{leaveId}/return", new { note = "" })).Status);
        Assert.Equal(200, (await hr.Post($"/leaverequests/{leaveId}/return", new { note = "أرفق التقرير الطبي" })).Status);

        var overview = (await me.Get("/me/leaves")).Data!;
        var returned = Request(overview, leaveId);
        Assert.Equal("needs_info", returned["status"].S());
        Assert.Equal("أرفق التقرير الطبي", returned["decisionNote"].S());
        // أيامه محجوزة وهو بانتظار الاستكمال: المتبقي 30 − 5
        var usage = overview["typeUsage"]!.AsArray().Single(u => u!["leaveTypeId"].S() == sick)!;
        Assert.Equal(5, usage["pending"].D()); Assert.Equal(25, usage["remaining"].D());
        // ولا تُقدَّم إجازة متداخلة معه
        Assert.Equal(409, (await me.Post("/me/leaves", new { leaveTypeId = sick, startDate = Day(start), endDate = Day(start) })).Status);

        // الاستكمال: رفع التقرير ثم إعادة الإرسال (الطلب نفسه)، فيعود معلّقاً بالأيام نفسها
        Assert.Equal(200, await UploadFileAsync(me, $"/me/leaves/{leaveId}/attachments", "report.pdf", SmallPdf));
        var resent = await me.Put($"/me/leaves/{leaveId}", new { leaveTypeId = sick, startDate = Day(start), endDate = Day(start.AddDays(4)), reason = "حمى - مرفق التقرير" });
        Assert.Equal(200, resent.Status);
        Assert.Equal("pending", resent.Data!["status"].S());
        overview = (await me.Get("/me/leaves")).Data!;
        usage = overview["typeUsage"]!.AsArray().Single(u => u!["leaveTypeId"].S() == sick)!;
        Assert.Equal(5, usage["pending"].D()); Assert.Equal(25, usage["remaining"].D()); // لم تُحتسب مرتين

        Assert.Equal(200, (await hr.Post($"/leaverequests/{leaveId}/approve", new { note = "مقبول" })).Status);
        overview = (await me.Get("/me/leaves")).Data!;
        usage = overview["typeUsage"]!.AsArray().Single(u => u!["leaveTypeId"].S() == sick)!;
        Assert.Equal(5, usage["used"].D()); Assert.Equal(0, usage["pending"].D()); Assert.Equal(25, usage["remaining"].D());

        // سجل الحالات بالترتيب
        var history = Request(overview, leaveId)["history"]!.AsArray().Select(h => h!["action"].S()).ToList();
        Assert.Equal(new[] { "submit", "return", "attachment_add", "resubmit", "approve" }, history);
        Assert.Equal("أرفق التقرير الطبي", Request(overview, leaveId)["history"]![1]!["note"].S());
    }

    [Fact]
    public async Task Employee_is_notified_of_decisions_and_hr_access_to_medical_reports_is_audited()
    {
        var hr = await NewTenantWithModulesAsync("hr");
        var (id, email) = await EmployeeAsync(hr, "E1");
        var me = await PortalAsync(hr, id, email);
        var annual = await TypeAsync(me, "annual");
        var leave = await me.Post("/me/leaves", new { leaveTypeId = annual, startDate = Day(Today.AddDays(20)), endDate = Day(Today.AddDays(21)) });
        Assert.Equal(201, leave.Status);
        var leaveId = leave.Data!["id"].S();
        Assert.Equal(200, await UploadFileAsync(me, $"/me/leaves/{leaveId}/attachments", "report.pdf", SmallPdf));

        Assert.Equal(200, (await hr.Post($"/leaverequests/{leaveId}/return", new { note = "حدد سبب الإجازة" })).Status);
        Assert.Equal(200, (await hr.Post($"/leaverequests/{leaveId}/reject", new { note = "تعارض مع الجرد" })).Status);

        var titles = (await me.Get("/notifications")).Data!.AsArray().Select(n => n!["title"].S()).ToList();
        Assert.Contains(titles, t => t.Contains("استكمال") || t.Contains("needs more information"));
        Assert.Contains(titles, t => t.Contains("رُفض") || t.Contains("rejected"));

        // اطلاع شؤون الموظفين على المرفق يُسجَّل في سجل التدقيق، ولا يظهر للموظف في سجل طلبه
        var attachmentId = (await hr.Get($"/leaverequests/{leaveId}/attachments")).Data!.AsArray()[0]!["id"].S();
        var download = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/leaverequests/{leaveId}/attachments/{attachmentId}");
        download.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", hr.Token);
        Assert.Equal(200, (int)(await NewHttp().SendAsync(download)).StatusCode);
        var hrHistory = (await hr.Get($"/leaverequests/{leaveId}/history")).Data!.AsArray().Select(h => h!["action"].S()).ToList();
        Assert.Contains("attachment_view", hrHistory);
        var myHistory = Request((await me.Get("/me/leaves")).Data!, leaveId)["history"]!.AsArray().Select(h => h!["action"].S()).ToList();
        Assert.DoesNotContain("attachment_view", myHistory);
        Assert.Contains("reject", myHistory);

        // الموظف لا يرى سجل طلب غيره ولا كشوف الرواتب من شاشات شؤون الموظفين
        Assert.Equal(403, (await me.Get($"/leaverequests/{leaveId}/history")).Status);
        Assert.Equal(403, (await me.Get("/payroll")).Status);
    }
}
