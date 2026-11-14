using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// الحضور الميداني (المرحلة B): استثناء خارج النطاق بسبب ومراجعة المشرف وأثرها على سجل الحضور، ولوحة حضور فريق المشروع،
/// وصلاحية الشاشة المستقلة ونطاق المشروع، وقاعدة الكشف والاستيراد مع اليوم الميداني.
/// </summary>
public partial class FieldGpsAttendanceTests
{
    private static Task<Res> DashboardAsync(Client api, string projectId) => api.Get($"/commercialcontracts/{projectId}/project/attendance");

    private static Task<Res> ReviewAsync(Client api, string projectId, string punchId, bool approve, string? note = null)
        => api.Post($"/commercialcontracts/{projectId}/project/attendance/punches/{punchId}/{(approve ? "approve" : "reject")}", new { note });

    private static JsonNode Row(Res dashboard, string employeeId) => dashboard.Data!["rows"]!.AsArray().Single(r => r!["employeeId"].S() == employeeId)!;

    private static async Task<string> PendingPunchAsync(Client api, string projectId)
        => Assert.Single((await api.Get($"/commercialcontracts/{projectId}/project/attendance/exceptions")).Data!.AsArray())!["punchId"].S();

    // ---------- الاستثناء ومراجعته ----------

    [Fact]
    public async Task Out_of_range_punch_is_a_pending_exception_until_the_supervisor_approves_it()
    {
        var s = await SiteAsync();
        Ok(await GeofenceAsync(s.Hr, s.ProjectId, policy: "exception"), "exception policy");
        using var clock = TestClock.Instance.Freeze(Riyadh(7, 20));

        Assert.True(Assert.Single((await s.Me.Get("/me/attendance")).Data!["projects"]!.AsArray())!["allowsOutOfRangeException"]!.GetValue<bool>());
        Status(400, await CheckInAsync(s.Me, lat: OutsideLat), "outside without a reason");
        var checkIn = await CheckInAsync(s.Me, lat: OutsideLat, reason: "استلام مواد من المورد");
        Ok(checkIn, "outside with a reason");
        var record = checkIn.Data!["record"]!;
        Assert.Equal("exception_pending", record["gpsStatus"].S()); Assert.Equal("present", record["status"].S());
        var punch = Assert.Single(record["punches"]!.AsArray())!;
        Assert.False(punch["withinGeofence"]!.GetValue<bool>()); Assert.Equal("pending", punch["reviewStatus"].S());

        // شؤون الموظفين ترى حالة التحقق بلا إحداثيات، واللوحة ترى إحداثيات الاستثناء وحده
        var hrRow = await HrDayRowAsync(s.Hr, s.EmployeeId);
        Assert.Equal("exception_pending", hrRow["gpsStatus"].S()); Assert.Equal("mobile_gps", hrRow["source"].S());
        var exceptions = (await s.Hr.Get($"/commercialcontracts/{s.ProjectId}/project/attendance/exceptions")).Data!.AsArray();
        var pending = Assert.Single(exceptions)!;
        Assert.Equal("استلام مواد من المورد", pending["exceptionReason"].S()); Assert.Equal("07:20", pending["localTime"].S());
        Assert.Equal(OutsideLat, pending["latitude"].D());

        TestClock.Instance.Set(Riyadh(16, 5));
        Ok(await CheckOutAsync(s.Me), "check-out inside");
        var day = await DashboardAsync(s.Hr, s.ProjectId);
        Ok(day, "dashboard");
        var row = Row(day, s.EmployeeId);
        Assert.Equal(465, row["workedMinutes"]!.GetValue<int>());
        var punches = row["punches"]!.AsArray();
        Assert.Equal(OutsideLat, punches.Single(p => p!["kind"].S() == "check_in")!["latitude"].D());
        Assert.Null(punches.Single(p => p!["kind"].S() == "check_out")!["latitude"]); // داخل النطاق: البعد يكفي
        var totals = day.Data!["totals"]!;
        Assert.Equal(1, totals["assigned"]!.GetValue<int>()); Assert.Equal(1, totals["checkedOut"]!.GetValue<int>());
        Assert.Equal(1, totals["pendingExceptions"]!.GetValue<int>());
        Assert.Equal(0, totals["approvedWorkedMinutes"]!.GetValue<int>()); // معلّق: لا ساعات معتمدة

        var approved = await ReviewAsync(s.Hr, s.ProjectId, pending["punchId"].S(), approve: true);
        Ok(approved, "approve");
        Assert.Equal("exception_approved", Row(approved, s.EmployeeId)["gpsStatus"].S());
        Assert.Equal(465, approved.Data!["totals"]!["approvedWorkedMinutes"]!.GetValue<int>());
        Assert.Equal(0, approved.Data!["totals"]!["pendingExceptions"]!.GetValue<int>());
        Status(409, await ReviewAsync(s.Hr, s.ProjectId, pending["punchId"].S(), approve: true), "already reviewed");
        Assert.Equal("approved", (await s.Me.Get("/me/attendance")).Data!["record"]!["punches"]![0]!["reviewStatus"].S());
    }

    [Fact]
    public async Task Rejected_check_in_makes_the_day_absent_and_rejected_check_out_drops_worked_hours()
    {
        var s = await SiteAsync();
        Ok(await GeofenceAsync(s.Hr, s.ProjectId, policy: "exception"), "exception policy");
        var (second, email) = await EmployeeAsync(s.Hr, "F2");
        await AssignAsync(s.Hr, s.ProjectId, second);
        var other = await PortalAsync(s.Hr, second, email);
        using var clock = TestClock.Instance.Freeze(Riyadh(7, 30));

        // F1: حضور من خارج النطاق يُرفض
        Ok(await CheckInAsync(s.Me, lat: OutsideLat, reason: "زحام"), "F1 outside");
        var punch = await PendingPunchAsync(s.Hr, s.ProjectId);
        Status(400, await ReviewAsync(s.Hr, s.ProjectId, punch, approve: false), "reject without a note");
        var rejected = await ReviewAsync(s.Hr, s.ProjectId, punch, approve: false, note: "لم يكن في الموقع");
        Ok(rejected, "reject check-in");
        var row = Row(rejected, s.EmployeeId);
        Assert.Equal("absent", row["status"].S()); Assert.Equal(0, row["lateMinutes"]!.GetValue<int>());
        Assert.Equal("exception_rejected", row["gpsStatus"].S());
        Assert.Equal("لم يكن في الموقع", row["punches"]![0]!["reviewNote"].S());
        var summary = (await s.Hr.Get($"/attendance/summary?period={Today:yyyy-MM}")).Data!.AsArray().Single(r => r!["employeeId"].S() == s.EmployeeId)!;
        Assert.Equal(1, summary["absentDays"]!.GetValue<int>());

        // F2: حضور داخل النطاق، وانصراف من خارجه يُرفض: يبقى حاضراً بلا ساعات معتمدة
        Ok(await CheckInAsync(other), "F2 inside");
        TestClock.Instance.Set(Riyadh(15, 0));
        Ok(await CheckOutAsync(other, lat: OutsideLat, reason: "غادرت مبكراً"), "F2 outside check-out");
        rejected = await ReviewAsync(s.Hr, s.ProjectId, await PendingPunchAsync(s.Hr, s.ProjectId), approve: false, note: "انصراف مبكر غير معتمد");
        Ok(rejected, "reject check-out");
        row = Row(rejected, second);
        Assert.Equal("present", row["status"].S()); Assert.Null(row["workedMinutes"]);
        Assert.Equal(0, rejected.Data!["totals"]!["approvedWorkedMinutes"]!.GetValue<int>());
    }

    // ---------- الصلاحية والنطاق ----------

    [Fact]
    public async Task Team_attendance_needs_its_own_screen_the_project_scope_and_no_self_review()
    {
        var s = await SiteAsync();
        Ok(await GeofenceAsync(s.Hr, s.ProjectId, policy: "exception"), "exception policy");
        Status(400, await GeofenceAsync(s.Hr, s.ProjectId, policy: "ignore"), "unknown policy");
        Ok(await s.Hr.Post("/construction-projects/role-templates"), "templates");
        using var clock = TestClock.Instance.Freeze(Riyadh(7, 0));
        Ok(await CheckInAsync(s.Me, lat: OutsideLat, reason: "سبب"), "pending exception");
        var punch = await PendingPunchAsync(s.Hr, s.ProjectId);

        // الموظف نفسه (دور البوابة) لا يصل للوحة
        Status(403, await DashboardAsync(s.Me, s.ProjectId), "portal");
        // مراقب التكاليف يرى المشروع وتقاريره لكن لا يُمنح حضور الفريق (بيانات موقع)
        var (cost, _) = await UserWithRoleAsync(s.Hr, "مراقب التكاليف");
        Ok(await cost.Get($"/commercialcontracts/{s.ProjectId}/project"), "cost controller sees the project");
        Status(403, await DashboardAsync(cost, s.ProjectId), "cost controller");
        // مشرف الموقع: عرض بلا اعتماد، وعلى مشاريعه وحدها
        var (site, siteId) = await UserWithRoleAsync(s.Hr, "مشرف الموقع");
        Assert.True((await DashboardAsync(site, s.ProjectId)).Status is 403 or 404, "not a member yet");
        Ok(await s.Hr.Post($"/commercialcontracts/{s.ProjectId}/project/members", new { userId = siteId, role = "site_supervisor" }), "member");
        Ok(await DashboardAsync(site, s.ProjectId), "member sees the team");
        Status(403, await ReviewAsync(site, s.ProjectId, punch, approve: true), "site supervisor cannot approve");
        // المهندس المشرف العضو يعتمد
        var (engineer, engineerId) = await UserWithRoleAsync(s.Hr, "المهندس المشرف");
        Ok(await s.Hr.Post($"/commercialcontracts/{s.ProjectId}/project/members", new { userId = engineerId, role = "engineer" }), "engineer member");
        Ok(await ReviewAsync(engineer, s.ProjectId, punch, approve: true), "engineer approves");

        // منشأة أخرى لا ترى اللوحة ولا تراجع
        var b = await SiteAsync();
        Status(404, await DashboardAsync(b.Hr, s.ProjectId), "foreign dashboard");
        Status(404, await ReviewAsync(b.Hr, s.ProjectId, punch, approve: false, note: "x"), "foreign review");

        // مستخدم النظام وهو موظف مُسند لا يراجع استثناءه
        var owner = s.Hr;
        var ownerEmployee = await owner.Post("/employees", new { code = "OWN", nameAr = "المالك", hireDate = Day(Today.AddYears(-1)), basicSalary = 9000, email = owner.Email });
        Ok(ownerEmployee, "owner as employee");
        var ownerEmployeeId = ownerEmployee.Data!["id"].S();
        Ok(await owner.Post($"/employees/{ownerEmployeeId}/portal-access/invite"), "link owner");
        await AssignAsync(owner, s.ProjectId, ownerEmployeeId, kind: "engineer");
        Ok(await CheckInAsync(owner, lat: OutsideLat, reason: "اجتماع"), "owner outside");
        Status(403, await ReviewAsync(owner, s.ProjectId, await PendingPunchAsync(owner, s.ProjectId), approve: true), "self review");
    }

    private async Task<(Client Client, Guid Id)> UserWithRoleAsync(Client owner, string roleNameAr)
    {
        var roleId = (await owner.Get("/roles")).Data!.AsArray().First(r => r!["nameAr"].S() == roleNameAr)!["id"].G();
        var email = $"u{Guid.NewGuid():N}@test.com";
        var created = await owner.Post("/users", new { name = "مستخدم " + email[..6], email, phone = "1", password = Password, role = "sales_rep", customRoleId = roleId });
        Ok(created, "user " + roleNameAr);
        return (await owner.LoginAsAsync(NewHttp(), email, Password), created.Data!["id"].G());
    }

    // ---------- كشف شؤون الموظفين والاستيراد مع اليوم الميداني ----------

    [Fact]
    public async Task Hr_sheet_and_fingerprint_import_change_hr_fields_of_a_field_day_but_keep_its_times_and_location()
    {
        var s = await SiteAsync();
        using var clock = TestClock.Instance.Freeze(Riyadh(7, 20));
        Ok(await CheckInAsync(s.Me), "check-in");
        TestClock.Instance.Set(Riyadh(16, 5));
        Ok(await CheckOutAsync(s.Me), "check-out");

        // استيراد البصمة لليوم نفسه: يعدّل التأخير والإضافي، ولا يمسّ الأوقات ولا الموقع ولا المصدر
        var import = await s.Hr.Post("/attendance/import", new[] { new { employeeCode = "F1", date = Day(Today), status = "present", lateMinutes = 0, overtimeHours = 1m, checkIn = "06:00", checkOut = "18:00" } });
        Ok(import, "import");
        Assert.Equal(1, import.Data!["successCount"]!.GetValue<int>());
        var row = await HrDayRowAsync(s.Hr, s.EmployeeId);
        Assert.Equal("07:20", row["checkIn"].S()); Assert.Equal("16:05", row["checkOut"].S());
        Assert.Equal(0, row["lateMinutes"]!.GetValue<int>()); Assert.Equal(1m, row["overtimeHours"].D());
        Assert.Equal("mobile_gps", row["source"].S()); Assert.Equal(465, row["workedMinutes"]!.GetValue<int>());
        Assert.Equal(2, Row(await DashboardAsync(s.Hr, s.ProjectId), s.EmployeeId)["punches"]!.AsArray().Count);

        // لا يُحذف من الكشف ولا من الاستيراد، وتغيير الحالة مسموح
        Status(409, await s.Hr.Put("/attendance/day", new { date = Day(Today), entries = new[] { new { employeeId = s.EmployeeId, status = (string?)null } } }), "clear GPS day");
        Ok(await s.Hr.Put("/attendance/day", new { date = Day(Today), entries = new[] { new { employeeId = s.EmployeeId, status = "leave" } } }), "HR marks leave");
        row = await HrDayRowAsync(s.Hr, s.EmployeeId);
        Assert.Equal("leave", row["status"].S()); Assert.Equal("07:20", row["checkIn"].S());

        // السجلات الأخرى تأخذ مصدر كاتبها
        var (other, _) = await EmployeeAsync(s.Hr, "F2");
        Ok(await s.Hr.Put("/attendance/day", new { date = Day(Today), entries = new[] { new { employeeId = other, status = "present" } } }), "manual");
        Assert.Equal("manual", (await HrDayRowAsync(s.Hr, other))["source"].S());
        Ok(await s.Hr.Post("/attendance/import", new[] { new { employeeCode = "F2", date = Day(Today), status = "absent" } }), "re-import");
        Assert.Equal("import", (await HrDayRowAsync(s.Hr, other))["source"].S());
    }
}
