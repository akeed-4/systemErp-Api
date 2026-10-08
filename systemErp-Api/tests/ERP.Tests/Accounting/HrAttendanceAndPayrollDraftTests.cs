using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>الحضور والإضافي وأثرهما على المسير، ودورة المسودة ثم الاعتماد، وملف حماية الأجور.</summary>
[Collection("api")]
public class HrAttendanceAndPayrollDraftTests : TestBase
{
    public HrAttendanceAndPayrollDraftTests(ErpFactory f) : base(f) { }

    private static readonly DateTime Today = DateTime.UtcNow.Date;
    private static readonly DateTime Month = new DateTime(Today.Year, Today.Month, 1).AddMonths(-1);
    private static readonly string Period = Month.ToString("yyyy-MM");
    private static string Day(DateTime d) => d.ToString("yyyy-MM-dd");

    /// <summary>راتب ثابت 7200: أجر اليوم 240 وأجر الساعة 30؛ الأساسي 4800: ساعته 20.</summary>
    private static async Task<string> EmployeeAsync(Client api, string code, object? extra = null)
    {
        var body = System.Text.Json.JsonSerializer.SerializeToNode(new { code, nameAr = "موظف " + code, hireDate = Day(Today.AddYears(-1)), basicSalary = 4800, housingAllowance = 1200, transportAllowance = 1200 })!.AsObject();
        if (extra != null)
            foreach (var (k, v) in System.Text.Json.JsonSerializer.SerializeToNode(extra)!.AsObject().ToList()) body[k] = v?.DeepClone();
        var r = await api.Post("/employees", body);
        Assert.Equal(201, r.Status);
        return r.Data!["id"].S();
    }

    private static Task<Res> SaveDayAsync(Client api, DateTime date, params object[] entries)
        => api.Put("/attendance/day", new { date = Day(date), entries });

    [Fact]
    public async Task Daily_sheet_is_saved_validated_and_summarised()
    {
        var api = await NewTenantAsync();
        var e1 = await EmployeeAsync(api, "E1");
        var e2 = await EmployeeAsync(api, "E2");

        var sheet = (await api.Get($"/attendance/day?date={Day(Month)}")).Data!.AsArray();
        Assert.Equal(2, sheet.Count);
        Assert.Null(sheet[0]!["status"]); // لم يُسجَّل بعد

        Assert.Equal(400, (await SaveDayAsync(api, Month, new { employeeId = e1, status = "sick" })).Status);
        Assert.Equal(400, (await SaveDayAsync(api, Month, new { employeeId = e1, status = "present", lateMinutes = -5 })).Status);
        Assert.Equal(400, (await SaveDayAsync(api, Month, new { employeeId = e1, status = "present", checkIn = "8am" })).Status);

        var saved = await SaveDayAsync(api, Month,
            new { employeeId = e1, status = "present", lateMinutes = 30, overtimeHours = 2m, checkIn = "08:30", checkOut = "19:00" },
            new { employeeId = e2, status = "absent", lateMinutes = 45, overtimeHours = 3m }); // الغائب بلا تأخير ولا إضافي
        Assert.Equal(200, saved.Status);
        var absent = saved.Data!.AsArray().First(r => r!["employeeCode"].S() == "E2")!;
        Assert.Equal("absent", absent["status"].S()); Assert.Equal(0, absent["lateMinutes"]!.GetValue<int>()); Assert.Equal(0, absent["overtimeHours"].D());

        // تعديل اليوم نفسه يحدّث السجل، وحالة فارغة تحذفه
        await SaveDayAsync(api, Month, new { employeeId = e1, status = "present", lateMinutes = 60, overtimeHours = 2m }, new { employeeId = e2, status = (string?)null });
        await SaveDayAsync(api, Month.AddDays(1), new { employeeId = e1, status = "absent" });

        var summary = (await api.Get($"/attendance/summary?period={Period}")).Data!.AsArray();
        var row = Assert.Single(summary)!;
        Assert.Equal(1, row["presentDays"]!.GetValue<int>()); Assert.Equal(1, row["absentDays"]!.GetValue<int>());
        Assert.Equal(60, row["lateMinutes"]!.GetValue<int>());
        Assert.Equal(270, row["attendanceDeduction"].D()); // يوم 240 + ساعة تأخير 30
        Assert.Equal(80, row["overtimePay"].D());          // ساعتان × (30 + 50% × 20)

        var import = await api.Post("/attendance/import", new object[]
        {
            new { employeeCode = "E2", date = Day(Month.AddDays(2)), status = "present", overtimeHours = 1m },
            new { employeeCode = "NOPE", date = Day(Month.AddDays(2)), status = "present" },
            new { employeeCode = "E2", date = Day(Month.AddDays(3)), status = "weird" },
        });
        Assert.Equal(1, import.Data!["successCount"]!.GetValue<int>());
        Assert.Equal(2, import.Data["failedCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task Attendance_feeds_payroll_and_is_locked_after_posting()
    {
        var api = await NewTenantAsync();
        var e1 = await EmployeeAsync(api, "E1");
        await SaveDayAsync(api, Month, new { employeeId = e1, status = "absent" });
        await SaveDayAsync(api, Month.AddDays(1), new { employeeId = e1, status = "present", lateMinutes = 120, overtimeHours = 4m });

        var line = (await api.Post("/payroll/preview", new { period = Period })).Data!["lines"]![0]!;
        Assert.Equal(1, line["absentDays"].D());
        Assert.Equal(300, line["attendanceDeduction"].D()); // 240 + ساعتان × 30
        Assert.Equal(160, line["overtimePay"].D());         // 4 × 40
        Assert.Equal(7360, line["gross"].D());              // 7200 + 160
        Assert.Equal(300, line["deductions"].D());
        Assert.Equal(7060, line["net"].D());

        Assert.Equal(200, (await api.Post("/payroll/post", new { period = Period })).Status);
        Assert.Equal(409, (await SaveDayAsync(api, Month.AddDays(5), new { employeeId = e1, status = "absent" })).Status); // الشهر مرحّل
    }

    [Fact]
    public async Task Draft_is_reviewed_then_approved_and_rejected_when_data_changed()
    {
        var api = await NewTenantAsync();
        var e1 = await EmployeeAsync(api, "E1");
        object Request(decimal deductions = 0) => new { period = Period, adjustments = new[] { new { employeeId = e1, additions = 500m, deductions } } };

        var draft = await api.Post("/payroll/draft", Request(100));
        Assert.Equal(200, draft.Status);
        Assert.Equal("draft", draft.Data!["status"].S());
        var number = draft.Data["runNumber"].S();
        Assert.Equal(7600, draft.Data["totalNet"].D()); // 7200 + 500 − 100
        Assert.Null(draft.Data["journalEntryId"]); // المسودة بلا قيد

        // إعادة الحفظ تستبدل المسودة وتحتفظ برقمها
        var again = await api.Post("/payroll/draft", Request(200));
        Assert.Equal(number, again.Data!["runNumber"].S());
        Assert.Equal(7500, again.Data["totalNet"].D());
        var id = again.Data["id"].S();
        Assert.Single((await api.Get("/payroll")).Data!.AsArray());

        // حضور سُجِّل بعد حفظ المسودة: الاعتماد يُرفض حتى يُعاد الحساب
        await SaveDayAsync(api, Month, new { employeeId = e1, status = "absent" });
        Assert.Equal(409, (await api.Post($"/payroll/{id}/approve")).Status);
        var recalculated = await api.Post("/payroll/draft", Request(200));
        Assert.Equal(7260, recalculated.Data!["totalNet"].D()); // − يوم غياب 240
        id = recalculated.Data["id"].S();

        Assert.Equal(409, (await api.Get($"/payroll/{id}/wagefile")).Status); // الملف لمسير مرحّل فقط
        var approved = await api.Post($"/payroll/{id}/approve");
        Assert.Equal(200, approved.Status);
        Assert.Equal("posted", approved.Data!["status"].S());
        Assert.Equal(number, approved.Data["runNumber"].S());
        Assert.NotNull(approved.Data["journalEntryId"]);
        Assert.Equal(7260, Math.Abs((await api.Get("/accounts/ByCode/214")).Data!["balance"].D()));
        var line = approved.Data["lines"]![0]!;
        Assert.Equal(200, line["manualDeductions"].D()); Assert.Equal(440, line["deductions"].D());

        Assert.Equal(409, (await api.Post("/payroll/draft", Request())).Status);          // الشهر مرحّل
        Assert.Equal(409, (await api.Delete($"/payroll/{approved.Data["id"].S()}")).Status); // ليس مسودة
    }

    [Fact]
    public async Task Wage_file_lists_posted_salaries_and_requires_bank_details()
    {
        var api = await NewTenantAsync();
        await EmployeeAsync(api, "E1", new { nationalId = "1012345678", iban = "SA0380000000608010167519", bankName = "الراجحي", employeeGosiRate = 10 });
        var missing = await EmployeeAsync(api, "E2");
        var posted = await api.Post("/payroll/post", new { period = Period });
        var id = posted.Data!["id"].S();

        var refused = await api.Get($"/payroll/{id}/wagefile");
        Assert.Equal(400, refused.Status); // E2 بلا هوية ولا آيبان

        Assert.Equal(200, (await api.Put($"/employees/{missing}", new
        {
            code = "E2", nameAr = "موظف E2", hireDate = Day(Today.AddYears(-1)), basicSalary = 4800, housingAllowance = 1200, transportAllowance = 1200,
            nationalId = "2098765432", idType = "iqama", iban = "SA4420000001234567891234",
        })).Status);
        var file = (await api.Get($"/payroll/{id}/wagefile")).Data!;
        Assert.EndsWith(".csv", file["fileName"].S());
        var rows = file["content"].S().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(r => r.Trim()).ToList();
        Assert.Equal(3, rows.Count);
        Assert.StartsWith("EmployeeId,EmployeeName,BankName,IBAN", rows[0]);
        // E1: أساسي 4800، سكن 1200، مكتسبات أخرى 1200، استقطاع تأمينات 600، صافي 6600
        Assert.Equal("\"1012345678\",\"موظف E1\",\"الراجحي\",\"SA0380000000608010167519\",4800.00,1200.00,1200.00,600.00,6600.00", rows[1]);
    }
}
