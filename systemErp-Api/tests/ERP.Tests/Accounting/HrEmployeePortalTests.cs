using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;

namespace ERP.Tests;

/// <summary>بوابة الموظف: الدعوة وتفعيل الحساب، حصر دور الموظف في مساراته، طلباته هو فقط، والمرفقات.</summary>
[Collection("api")]
public class HrEmployeePortalTests : TestBase
{
    public HrEmployeePortalTests(ErpFactory f) : base(f) { }

    private const string Password = "Passw0rd!";
    private static readonly byte[] Pdf = "%PDF-1.4 medical report"u8.ToArray();
    private static string Day(DateTime d) => d.ToString("yyyy-MM-dd");
    private static readonly DateTime Today = DateTime.UtcNow.Date;

    /// <summary>منشأة يشمل اشتراكها وحدة شؤون الموظفين (يمنحها مدير المنصة؛ الباقات الافتراضية لا تشملها).</summary>
    private async Task<Client> HrTenantAsync()
    {
        var admin = await NewTenantAsync("شركة مدير المنصة");
        var host = Factory.WithWebHostBuilder(b => b.UseSetting("Platform:AdminEmails:0", admin.Email));
        var root = await admin.LoginAsAsync(host.CreateClient(), admin.Email, Password);
        var tenant = await NewTenantAsync("شركة الموظفين");
        Assert.Equal(200, (await root.Post($"/platform/tenants/{tenant.TenantId}/subscriptions/modules", new { modules = new[] { "accounting", "hr" } })).Status);
        return tenant;
    }

    private static async Task<(string Id, string Email)> EmployeeAsync(Client api, string code, bool withEmail = true)
    {
        var email = $"e{Guid.NewGuid():N}@test.com";
        var r = await api.Post("/employees", new
        {
            code, nameAr = "موظف " + code, hireDate = Day(Today.AddYears(-2)), basicSalary = 6000, housingAllowance = 1500,
            email = withEmail ? email : null,
        });
        Assert.Equal(201, r.Status);
        return (r.Data!["id"].S(), email);
    }

    /// <summary>يدعو الموظف ويفعّل حسابه من الرابط، ويعيد عميلاً يحمل رمز دخوله.</summary>
    private async Task<Client> ActivateAsync(Client hr, string employeeId, string email)
    {
        var invite = await hr.Post($"/employees/{employeeId}/portal-access/invite");
        Assert.Equal(200, invite.Status);
        var accept = await hr.SendAsync(HttpMethod.Post, "/auth/Invite/Accept", new { token = invite.Data!["inviteToken"].S(), password = Password }, anonymous: true);
        Assert.Equal(200, accept.Status);
        return await hr.LoginAsAsync(NewHttp(), email, Password);
    }

    private async Task<(int Status, JsonNode? Body)> UploadAsync(Client api, string path, string fileName, byte[] content)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1" + path) { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", api.Token);
        var response = await NewHttp().SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return ((int)response.StatusCode, string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text));
    }

    private async Task<(int Status, byte[] Content)> DownloadAsync(Client api, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", api.Token);
        var response = await NewHttp().SendAsync(request);
        return ((int)response.StatusCode, await response.Content.ReadAsByteArrayAsync());
    }

    private static async Task<string> AnnualTypeAsync(Client api)
        => (await api.Get("/leavetypes?pageSize=100")).Data!["items"]!.AsArray().First(t => t!["code"].S() == "annual")!["id"].S();

    [Fact]
    public async Task Invite_link_activates_the_account_once_and_needs_an_email()
    {
        var hr = await HrTenantAsync();
        var (noEmail, _) = await EmployeeAsync(hr, "E0", withEmail: false);
        Assert.Equal(400, (await hr.Post($"/employees/{noEmail}/portal-access/invite")).Status);

        var (id, email) = await EmployeeAsync(hr, "E1");
        Assert.Equal("none", (await hr.Get($"/employees/{id}/portal-access")).Data!["status"].S());

        var invite = await hr.Post($"/employees/{id}/portal-access/invite");
        Assert.Equal("invited", invite.Data!["status"].S());
        Assert.True(invite.Data["emailSent"]!.GetValue<bool>());
        var token = invite.Data["inviteToken"].S();
        Assert.Contains(FakeEmailSender.Instance.SentTo(email), m => m.Body.Contains($"/activate/{token}"));

        // الرابط يعرّف صاحبه قبل الدخول، وكلمة مرور قصيرة تُرفض
        var info = await hr.SendAsync(HttpMethod.Get, $"/auth/Invite/{token}", anonymous: true);
        Assert.Equal(email, info.Data!["email"].S());
        Assert.Equal(400, (await hr.SendAsync(HttpMethod.Post, "/auth/Invite/Accept", new { token, password = "123" }, anonymous: true)).Status);

        var accepted = await hr.SendAsync(HttpMethod.Post, "/auth/Invite/Accept", new { token, password = Password }, anonymous: true);
        Assert.Equal(200, accepted.Status);
        Assert.Equal("employee", accepted.Body!["user"]!["role"].S());
        Assert.False(string.IsNullOrEmpty(accepted.Body["token"].S()));

        // الرابط لمرة واحدة، ودعوة جديدة لحساب مفعَّل لا تصدر رابطاً
        Assert.Equal(400, (await hr.SendAsync(HttpMethod.Post, "/auth/Invite/Accept", new { token, password = Password }, anonymous: true)).Status);
        Assert.Equal(400, (await hr.SendAsync(HttpMethod.Get, $"/auth/Invite/{token}", anonymous: true)).Status);
        var again = await hr.Post($"/employees/{id}/portal-access/invite");
        Assert.Equal("active", again.Data!["status"].S());
        Assert.Null(again.Data["inviteToken"]);

        // دعوة ثانية قبل التفعيل تبطل الأولى
        var (second, _) = await EmployeeAsync(hr, "E2");
        var first = (await hr.Post($"/employees/{second}/portal-access/invite")).Data!["inviteToken"].S();
        var renewed = (await hr.Post($"/employees/{second}/portal-access/invite")).Data!["inviteToken"].S();
        Assert.Equal(400, (await hr.SendAsync(HttpMethod.Get, $"/auth/Invite/{first}", anonymous: true)).Status);
        Assert.Equal(200, (await hr.SendAsync(HttpMethod.Get, $"/auth/Invite/{renewed}", anonymous: true)).Status);
    }

    [Fact]
    public async Task Employee_role_reaches_only_the_self_service_endpoints()
    {
        var hr = await HrTenantAsync();
        var (id, email) = await EmployeeAsync(hr, "E1");
        var me = await ActivateAsync(hr, id, email);

        Assert.Equal(200, (await me.Get("/me/leaves")).Status);
        Assert.Equal(200, (await me.Get("/auth/me")).Status);

        // شاشات النظام مغلقة كلها: ما يحمل صلاحية شاشة وما لا يحملها
        foreach (var path in new[] { "/employees", "/leaverequests/balances", $"/leaverequests/balance/{id}", "/leavetypes", "/customers", "/auth/users", "/notifications", "/company", "/invoices" })
            Assert.Equal(403, (await me.Get(path)).Status);
        Assert.Equal(403, (await me.Post("/leaverequests", new { employeeId = id })).Status);
        Assert.Equal(403, (await me.Post($"/employees/{id}/portal-access/invite")).Status);

        // إيقاف الدخول يمنع تسجيل الدخول، وإعادة الدعوة تعيده بكلمة مروره
        Assert.Equal("disabled", (await hr.Post($"/employees/{id}/portal-access/revoke")).Data!["status"].S());
        Assert.Null((await hr.LoginAsAsync(NewHttp(), email, Password)).Token);
        Assert.Equal("active", (await hr.Post($"/employees/{id}/portal-access/invite")).Data!["status"].S());
        Assert.NotNull((await hr.LoginAsAsync(NewHttp(), email, Password)).Token);
    }

    [Fact]
    public async Task Employee_requests_leave_for_himself_only_and_hr_decides()
    {
        var hr = await HrTenantAsync();
        var annual = await AnnualTypeAsync(hr);
        var (id1, email1) = await EmployeeAsync(hr, "E1");
        var (id2, email2) = await EmployeeAsync(hr, "E2");
        var one = await ActivateAsync(hr, id1, email1);
        var two = await ActivateAsync(hr, id2, email2);

        var overview = (await one.Get("/me/leaves")).Data!;
        Assert.Equal("E1", overview["employeeCode"].S());
        Assert.Contains(overview["leaveTypes"]!.AsArray(), t => t!["code"].S() == "annual");
        Assert.Empty(overview["requests"]!.AsArray());

        // معرّف موظف آخر في الطلب يُتجاهل: الطلب لصاحب الجلسة
        var created = await one.Post("/me/leaves", new { employeeId = id2, leaveTypeId = annual, startDate = Day(Today.AddDays(10)), endDate = Day(Today.AddDays(12)), reason = "سفر" });
        Assert.Equal(201, created.Status);
        Assert.Equal(id1, created.Data!["employeeId"].S());
        var leaveId = created.Data["id"].S();

        // يراه صاحبه وشؤون الموظفين، ولا يراه ولا يلغيه موظف آخر
        Assert.Single((await one.Get("/me/leaves")).Data!["requests"]!.AsArray());
        Assert.Empty((await two.Get("/me/leaves")).Data!["requests"]!.AsArray());
        Assert.Equal(404, (await two.Post($"/me/leaves/{leaveId}/cancel")).Status);
        Assert.Equal("pending", (await hr.Get($"/leaverequests/{leaveId}")).Data!["status"].S());

        // إشعار للمعتمد بالطلب الجديد
        Assert.Contains((await hr.Get("/notifications")).Data!.AsArray(), n => n!["relatedDocId"].S() == leaveId);

        // بعد الاعتماد لا يلغيه الموظف بنفسه
        Assert.Equal(200, (await hr.Post($"/leaverequests/{leaveId}/approve")).Status);
        Assert.Equal("approved", (await one.Get("/me/leaves")).Data!["requests"]![0]!["status"].S());
        Assert.Equal(409, (await one.Post($"/me/leaves/{leaveId}/cancel")).Status);

        // طلب معلّق يلغيه صاحبه
        var pending = (await one.Post("/me/leaves", new { leaveTypeId = annual, startDate = Day(Today.AddDays(30)), endDate = Day(Today.AddDays(31)) })).Data!["id"].S();
        Assert.Equal("cancelled", (await one.Post($"/me/leaves/{pending}/cancel")).Data!["status"].S());
    }

    [Fact]
    public async Task Attachments_are_validated_and_visible_to_the_owner_and_hr_only()
    {
        var hr = await HrTenantAsync();
        var annual = await AnnualTypeAsync(hr);
        var (id1, email1) = await EmployeeAsync(hr, "E1");
        var (id2, email2) = await EmployeeAsync(hr, "E2");
        var one = await ActivateAsync(hr, id1, email1);
        var two = await ActivateAsync(hr, id2, email2);
        var leaveId = (await one.Post("/me/leaves", new { leaveTypeId = annual, startDate = Day(Today.AddDays(10)), endDate = Day(Today.AddDays(12)) })).Data!["id"].S();

        // النوع يُتحقق من الامتداد والمحتوى معاً
        Assert.Equal(400, (await UploadAsync(one, $"/me/leaves/{leaveId}/attachments", "virus.exe", Pdf)).Status);
        Assert.Equal(400, (await UploadAsync(one, $"/me/leaves/{leaveId}/attachments", "fake.pdf", "MZ not a pdf"u8.ToArray())).Status);
        Assert.Equal(400, (await UploadAsync(one, $"/me/leaves/{leaveId}/attachments", "big.pdf", Pdf.Concat(new byte[5 * 1024 * 1024]).ToArray())).Status);

        var uploaded = await UploadAsync(one, $"/me/leaves/{leaveId}/attachments", "report.pdf", Pdf);
        Assert.Equal(200, uploaded.Status);
        var attachmentId = uploaded.Body!["data"]!["id"].S();

        var mine = (await one.Get("/me/leaves")).Data!["requests"]![0]!;
        Assert.Equal(1, mine["attachmentCount"]!.GetValue<int>());
        Assert.Equal("report.pdf", mine["attachments"]![0]!["fileName"].S());
        Assert.Equal(Pdf, (await DownloadAsync(one, $"/me/leaves/{leaveId}/attachments/{attachmentId}")).Content);

        // موظف آخر لا يصل لمرفق غيره ولا يرفع على طلبه
        Assert.Equal(404, (await DownloadAsync(two, $"/me/leaves/{leaveId}/attachments/{attachmentId}")).Status);
        Assert.Equal(404, (await UploadAsync(two, $"/me/leaves/{leaveId}/attachments", "x.pdf", Pdf)).Status);
        Assert.Equal(403, (await DownloadAsync(two, $"/leaverequests/{leaveId}/attachments/{attachmentId}")).Status);

        // شؤون الموظفين ترى المرفق وتنزّله
        Assert.Single((await hr.Get($"/leaverequests/{leaveId}/attachments")).Data!.AsArray());
        Assert.Equal(Pdf, (await DownloadAsync(hr, $"/leaverequests/{leaveId}/attachments/{attachmentId}")).Content);
        Assert.Equal(1, (await hr.Get($"/leaverequests/{leaveId}")).Data!["attachmentCount"]!.GetValue<int>());

        // حد العدد، ثم بعد البتّ في الطلب لا يغيّر الموظف مرفقاته
        for (var i = 0; i < 4; i++) Assert.Equal(200, (await UploadAsync(one, $"/me/leaves/{leaveId}/attachments", $"p{i}.png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 1 })).Status);
        Assert.Equal(400, (await UploadAsync(one, $"/me/leaves/{leaveId}/attachments", "sixth.pdf", Pdf)).Status);
        Assert.Equal(200, (await hr.Post($"/leaverequests/{leaveId}/reject")).Status);
        Assert.Equal(409, (await one.Delete($"/me/leaves/{leaveId}/attachments/{attachmentId}")).Status);
        Assert.Equal(409, (await UploadAsync(one, $"/me/leaves/{leaveId}/attachments", "late.pdf", Pdf)).Status);

        // حذف الطلب يحذف مرفقاته معه
        Assert.Equal(200, (await hr.Delete($"/leaverequests/{leaveId}")).Status);
        Assert.Equal(404, (await hr.Get($"/leaverequests/{leaveId}/attachments")).Status);
    }
}
