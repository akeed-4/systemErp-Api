using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// الحضور الميداني بالموقع: الموظف من الجلسة والمشروع من إسناده، والنطاق والدقة يتحقق منهما الخادم، والتسجيل في سجل الحضور
/// اليومي نفسه (كشف اليوم والملخص)، وسجل واحد وحضور وانصراف واحد في اليوم، وتوافقه مع حضور شؤون الموظفين واستيراد البصمة،
/// وعزل المنشآت، والمنطقة الزمنية للمنشأة.
/// </summary>
[Collection("api")]
public partial class FieldGpsAttendanceTests : TestBase
{
    public FieldGpsAttendanceTests(ErpFactory f) : base(f) { }

    private const string Password = "Passw0rd!";
    // موقع المشروع (الرياض)، ونقطة على بعد ~50 م شمالاً، وأخرى على بعد ~330 م
    private const decimal SiteLat = 24.713600m, SiteLon = 46.675300m;
    private const decimal InsideLat = 24.714050m, OutsideLat = 24.716600m;

    private static readonly DateTime Today = DateTime.UtcNow.Date;
    private static string Day(DateTime d) => d.ToString("yyyy-MM-dd");
    /// <summary>وقت بتوقيت الرياض (UTC+3) اليوم، بالـ UTC للساعة.</summary>
    private static DateTime Riyadh(int hour, int minute) => Today.AddHours(hour - 3).AddMinutes(minute);

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");
    private static void Status(int expected, Res r, string step) => Assert.True(r.Status == expected, $"{step}: expected {expected}, got {r.Status} {r.Body?.ToJsonString()}");

    private sealed record Site(Client Hr, string ProjectId, string EmployeeId, Client Me);

    // ---------- الإعداد ----------

    private async Task<Client> TenantAsync() => await NewTenantWithModulesAsync("accounting", "car_showroom", "pos", "construction", "hr");

    private static async Task<(string Id, string Email)> EmployeeAsync(Client hr, string code)
    {
        var email = $"e{Guid.NewGuid():N}@test.com";
        var r = await hr.Post("/employees", new { code, nameAr = "موظف " + code, hireDate = Day(Today.AddYears(-1)), basicSalary = 4800, housingAllowance = 1200, email });
        Ok(r, "employee " + code);
        return (r.Data!["id"].S(), email);
    }

    private async Task<Client> PortalAsync(Client hr, string employeeId, string email)
    {
        var invite = await hr.Post($"/employees/{employeeId}/portal-access/invite");
        Ok(invite, "invite");
        Ok(await hr.SendAsync(HttpMethod.Post, "/auth/Invite/Accept", new { token = invite.Data!["inviteToken"].S(), password = Password }, anonymous: true), "accept");
        return await hr.LoginAsAsync(NewHttp(), email, Password);
    }

    /// <summary>مشروع مقاولات مباشر؛ active = ساري (في التنفيذ)، ودوام 07:00–16:00 باستراحة 12:00–13:00.</summary>
    private static async Task<string> ProjectAsync(Client hr, string name, bool active = true)
    {
        var (party, _) = await SeedCustomerAsync(hr);
        var c = await hr.Post("/commercialcontracts", new
        {
            title = name, projectName = name, contractType = "construction_boq", workflow = "simple", requiresGuarantee = false, partyName = "عميل", partyId = party,
            startDate = Today.AddDays(-10), endDate = Today.AddMonths(3), vatRate = 15,
            items = new object[] { new { description = "حفر", unit = "م3", quantity = 10, unitPrice = 100, vatRate = 15, lineKind = "measured", sortOrder = 1 } },
        });
        Ok(c, "project " + name);
        var id = c.Data!["id"].S();
        Ok(await hr.Put($"/commercialcontracts/{id}/project/profile", new { workdayStart = "07:00", workdayEnd = "16:00", breakStart = "12:00", breakEnd = "13:00" }), "hours");
        if (active) Ok(await hr.Post($"/commercialcontracts/{id}/move", new { toStatus = "active" }), "activate");
        return id;
    }

    private static Task<Res> GeofenceAsync(Client hr, string projectId, bool enabled = true, decimal? lat = SiteLat, decimal? lon = SiteLon, int radius = 150, int accuracy = 30,
        string? policy = null)
        => hr.Put($"/commercialcontracts/{projectId}/project/geofence", new
        {
            latitude = lat, longitude = lon, radiusMeters = radius, maxAccuracyMeters = accuracy, gpsAttendanceEnabled = enabled, outOfRangePolicy = policy,
        });

    private static async Task AssignAsync(Client hr, string projectId, string employeeId, string kind = "labor")
        => Ok(await hr.Post($"/commercialcontracts/{projectId}/project/resources", new
        {
            kind, name = "عامل " + employeeId[..4], rateBasis = "hour", rate = 20, hoursPerDay = 8, daysPerMonth = 26, overtimeMultiplier = 1.5, employeeId,
        }), "assign");

    /// <summary>منشأة بموظف مُسند لمشروع ساري نطاقه مفعّل، وجلسة بوابته.</summary>
    private async Task<Site> SiteAsync()
    {
        var hr = await TenantAsync();
        var project = await ProjectAsync(hr, "برج");
        Ok(await GeofenceAsync(hr, project), "geofence");
        var (employee, email) = await EmployeeAsync(hr, "F1");
        await AssignAsync(hr, project, employee);
        return new Site(hr, project, employee, await PortalAsync(hr, employee, email));
    }

    private static Task<Res> CheckInAsync(Client me, decimal lat = InsideLat, decimal lon = SiteLon, decimal accuracy = 12, string? contractId = null, string? reason = null)
        => me.Post("/me/attendance/check-in", new { latitude = lat, longitude = lon, accuracyMeters = accuracy, contractId, exceptionReason = reason });

    private static Task<Res> CheckOutAsync(Client me, decimal lat = InsideLat, decimal lon = SiteLon, decimal accuracy = 12, string? reason = null)
        => me.Post("/me/attendance/check-out", new { latitude = lat, longitude = lon, accuracyMeters = accuracy, exceptionReason = reason });

    private static async Task<JsonNode> HrDayRowAsync(Client hr, string employeeId)
        => (await hr.Get($"/attendance/day?date={Day(Today)}")).Data!.AsArray().Single(r => r!["employeeId"].S() == employeeId)!;

    // ---------- الحضور والانصراف ----------

    [Fact]
    public async Task Check_in_and_out_inside_the_geofence_are_recorded_in_the_hr_daily_attendance()
    {
        var s = await SiteAsync();
        using var clock = TestClock.Instance.Freeze(Riyadh(7, 20));

        var today = (await s.Me.Get("/me/attendance")).Data!;
        Assert.Equal(Day(Today), today["date"].S()[..10]);
        Assert.True(today["canCheckIn"]!.GetValue<bool>()); Assert.False(today["canCheckOut"]!.GetValue<bool>());
        var project = Assert.Single(today["projects"]!.AsArray())!;
        Assert.Equal(s.ProjectId, project["contractId"].S());
        Assert.Null(project["latitude"]); // إحداثيات الموقع لا تُكشف للموظف
        Assert.Null(today["record"]);

        var checkIn = await CheckInAsync(s.Me);
        Ok(checkIn, "check-in");
        var record = checkIn.Data!["record"]!;
        Assert.Equal("present", record["status"].S()); Assert.Equal("mobile_gps", record["source"].S());
        Assert.Equal(s.ProjectId, record["contractId"].S());
        Assert.Equal("07:20", record["checkIn"].S()); Assert.Equal(20, record["lateMinutes"]!.GetValue<int>()); // الدوام 07:00
        var punch = Assert.Single(record["punches"]!.AsArray())!;
        Assert.Equal("check_in", punch["kind"].S());
        Assert.InRange(punch["distanceMeters"].D(), 45m, 55m); Assert.Equal(150, punch["radiusMeters"]!.GetValue<int>());
        Assert.True(checkIn.Data!["canCheckOut"]!.GetValue<bool>());

        // يظهر في كشف يوم شؤون الموظفين كأي سجل
        var row = await HrDayRowAsync(s.Hr, s.EmployeeId);
        Assert.Equal("present", row["status"].S()); Assert.Equal("07:20", row["checkIn"].S()); Assert.Equal(20, row["lateMinutes"]!.GetValue<int>());

        Status(409, await CheckInAsync(s.Me), "duplicate check-in");

        TestClock.Instance.Set(Riyadh(16, 5));
        var checkOut = await CheckOutAsync(s.Me);
        Ok(checkOut, "check-out");
        record = checkOut.Data!["record"]!;
        Assert.Equal("16:05", record["checkOut"].S());
        Assert.Equal(8 * 60 + 45 - 60, record["workedMinutes"]!.GetValue<int>()); // 07:20–16:05 ناقص ساعة الاستراحة
        Assert.Equal(2, record["punches"]!.AsArray().Count);
        Assert.False(checkOut.Data!["canCheckOut"]!.GetValue<bool>());
        Status(409, await CheckOutAsync(s.Me), "duplicate check-out");

        Assert.Equal("16:05", (await HrDayRowAsync(s.Hr, s.EmployeeId))["checkOut"].S());
        var summary = (await s.Hr.Get($"/attendance/summary?period={Today:yyyy-MM}")).Data!.AsArray().Single(r => r!["employeeId"].S() == s.EmployeeId)!;
        Assert.Equal(1, summary["presentDays"]!.GetValue<int>()); Assert.Equal(20, summary["lateMinutes"]!.GetValue<int>());
    }

    [Fact]
    public async Task Location_outside_the_geofence_poor_accuracy_or_invalid_is_rejected_without_a_record()
    {
        var s = await SiteAsync();
        using var clock = TestClock.Instance.Freeze(Riyadh(7, 0));

        Status(400, await CheckInAsync(s.Me, lat: OutsideLat), "outside the geofence");
        Status(400, await CheckInAsync(s.Me, accuracy: 80), "accuracy weaker than 30 m");
        Status(400, await CheckInAsync(s.Me, lat: 95), "invalid latitude");
        Status(400, await CheckInAsync(s.Me, accuracy: 0), "missing accuracy");
        Status(409, await CheckOutAsync(s.Me), "check-out without check-in");

        Assert.Null((await s.Me.Get("/me/attendance")).Data!["record"]);
        Assert.Null((await HrDayRowAsync(s.Hr, s.EmployeeId))["status"]);

        // الحضور ثم انصراف من خارج النطاق: يُرفض ويبقى الحضور مفتوحاً
        Ok(await CheckInAsync(s.Me), "check-in");
        TestClock.Instance.Set(Riyadh(15, 0));
        Status(400, await CheckOutAsync(s.Me, lat: OutsideLat), "check-out outside");
        Assert.True((await s.Me.Get("/me/attendance")).Data!["canCheckOut"]!.GetValue<bool>());
    }

    // ---------- الإسناد والتفعيل ----------

    [Fact]
    public async Task Only_an_assigned_employee_on_an_executing_project_with_gps_enabled_can_check_in()
    {
        var s = await SiteAsync();
        using var clock = TestClock.Instance.Freeze(Riyadh(7, 0));

        // موظف غير مُسند
        var (other, otherEmail) = await EmployeeAsync(s.Hr, "F2");
        var stranger = await PortalAsync(s.Hr, other, otherEmail);
        Status(403, await CheckInAsync(stranger), "not assigned");
        Assert.Empty((await stranger.Get("/me/attendance")).Data!["projects"]!.AsArray());

        // مشروع آخر ساري لم يُسند إليه: لا يُقبل بمعرّفه
        var second = await ProjectAsync(s.Hr, "مستودع");
        Ok(await GeofenceAsync(s.Hr, second), "second geofence");
        Status(403, await CheckInAsync(s.Me, contractId: second), "foreign project id");

        // مشروع مسند غير ساري (مسودة)
        var draft = await ProjectAsync(s.Hr, "مسودة", active: false);
        Ok(await GeofenceAsync(s.Hr, draft), "draft geofence");
        await AssignAsync(s.Hr, draft, other);
        Status(409, await CheckInAsync(stranger), "draft project");

        // مُسند لمشروعين ساريين: يختار
        await AssignAsync(s.Hr, second, s.EmployeeId, kind: "engineer");
        Assert.Equal(2, (await s.Me.Get("/me/attendance")).Data!["projects"]!.AsArray().Count);
        Status(400, await CheckInAsync(s.Me), "must choose a project");

        // النطاق معطّل على المشروع المختار
        Ok(await GeofenceAsync(s.Hr, second, enabled: false), "disable");
        Status(409, await CheckInAsync(s.Me, contractId: second), "gps disabled");

        var ok = await CheckInAsync(s.Me, contractId: s.ProjectId);
        Ok(ok, "check-in on the chosen project");
        Assert.Equal(s.ProjectId, ok.Data!["record"]!["contractId"].S());
    }

    // ---------- التوافق مع حضور شؤون الموظفين والبصمة ----------

    [Fact]
    public async Task Day_recorded_by_hr_or_fingerprint_import_blocks_field_check_in_and_hr_flows_are_unchanged()
    {
        var s = await SiteAsync();
        using var clock = TestClock.Instance.Freeze(Riyadh(7, 10));

        // استيراد ملف البصمة لليوم يعمل كما هو
        var import = await s.Hr.Post("/attendance/import", new[] { new { employeeCode = "F1", date = Day(Today), status = "present", checkIn = "07:02", checkOut = "16:00" } });
        Ok(import, "fingerprint import");
        Assert.Equal(1, import.Data!["successCount"]!.GetValue<int>());

        Status(409, await CheckInAsync(s.Me), "day already recorded by HR");
        var row = await HrDayRowAsync(s.Hr, s.EmployeeId);
        Assert.Equal("07:02", row["checkIn"].S()); // لم يتغير
        var record = (await s.Me.Get("/me/attendance")).Data!;
        Assert.Equal("import", record["record"]!["source"].S());
        Assert.False(record["canCheckIn"]!.GetValue<bool>());

        // كشف اليوم اليدوي لموظف آخر بلا تغيير
        var (other, _) = await EmployeeAsync(s.Hr, "F2");
        Ok(await s.Hr.Put("/attendance/day", new { date = Day(Today), entries = new[] { new { employeeId = other, status = "absent" } } }), "manual sheet");
        Assert.Equal("absent", (await HrDayRowAsync(s.Hr, other))["status"].S());
    }

    [Fact]
    public async Task Field_attendance_is_locked_once_the_month_payroll_is_posted()
    {
        var s = await SiteAsync();
        using var clock = TestClock.Instance.Freeze(Riyadh(7, 0));
        Ok(await s.Hr.Post("/payroll/post", new { period = Today.ToString("yyyy-MM") }), "post payroll");
        Status(409, await CheckInAsync(s.Me), "posted month");
    }

    // ---------- العزل والنطاق والمنطقة الزمنية ----------

    [Fact]
    public async Task Another_tenant_can_neither_read_the_geofence_nor_check_in_on_the_project()
    {
        var a = await SiteAsync();
        var b = await SiteAsync();
        using var clock = TestClock.Instance.Freeze(Riyadh(7, 0));

        Status(404, await b.Hr.Get($"/commercialcontracts/{a.ProjectId}/project/geofence"), "foreign geofence read");
        Status(404, await GeofenceAsync(b.Hr, a.ProjectId, radius: 5000), "foreign geofence write");
        Status(403, await CheckInAsync(b.Me, contractId: a.ProjectId), "foreign project check-in");
        Assert.Null((await a.Me.Get("/me/attendance")).Data!["record"]);
        Assert.Equal(150, (await a.Hr.Get($"/commercialcontracts/{a.ProjectId}/project/geofence")).Data!["radiusMeters"]!.GetValue<int>());
    }

    [Fact]
    public async Task Geofence_settings_are_validated_and_the_employee_portal_cannot_change_them()
    {
        var s = await SiteAsync();

        var fence = (await s.Hr.Get($"/commercialcontracts/{s.ProjectId}/project/geofence")).Data!;
        Assert.Equal(SiteLat, fence["latitude"].D()); Assert.True(fence["gpsAttendanceEnabled"]!.GetValue<bool>());

        Status(400, await GeofenceAsync(s.Hr, s.ProjectId, lat: null, lon: null), "enable without a location");
        Status(400, await GeofenceAsync(s.Hr, s.ProjectId, lat: SiteLat, lon: null), "half a coordinate");
        Status(400, await GeofenceAsync(s.Hr, s.ProjectId, lat: 91), "latitude out of range");
        Status(400, await GeofenceAsync(s.Hr, s.ProjectId, radius: 10), "radius too small");
        Status(400, await GeofenceAsync(s.Hr, s.ProjectId, accuracy: 1000), "accuracy too loose");
        Ok(await GeofenceAsync(s.Hr, s.ProjectId, enabled: false, lat: null, lon: null), "clear and disable");

        // الموظف (دور البوابة) لا يصل لإعداد المشروع
        Status(403, await s.Me.Get($"/commercialcontracts/{s.ProjectId}/project/geofence"), "portal read");
        Status(403, await GeofenceAsync(s.Me, s.ProjectId), "portal write");
    }

    [Fact]
    public async Task Local_day_and_lateness_follow_the_company_time_zone()
    {
        var s = await SiteAsync();
        var company = (await s.Hr.Get("/company")).Data!.AsObject();
        Assert.Equal("Asia/Riyadh", company["timeZoneId"].S());

        var body = company.DeepClone().AsObject();
        body["timeZoneId"] = "Mars/Olympus";
        Status(400, await s.Hr.Put("/company", body), "unknown zone");
        body["timeZoneId"] = "Asia/Dubai"; // UTC+4
        Ok(await s.Hr.Put("/company", body), "set zone");
        body.Remove("timeZoneId"); // واجهة لا ترسلها: تبقى
        Ok(await s.Hr.Put("/company", body), "keep zone");
        Assert.Equal("Asia/Dubai", (await s.Hr.Get("/company")).Data!["timeZoneId"].S());

        using var clock = TestClock.Instance.Freeze(Today.AddHours(3).AddMinutes(30)); // 07:30 دبي
        var record = (await CheckInAsync(s.Me)).Data!["record"]!;
        Assert.Equal("07:30", record["checkIn"].S()); Assert.Equal(30, record["lateMinutes"]!.GetValue<int>());
    }
}
