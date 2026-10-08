using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>الإجازات: الأنواع الافتراضية، الرصيد السنوي المتراكم، دورة الطلب والاعتماد، وخصم غير المدفوعة في المسير.</summary>
[Collection("api")]
public class HrLeaveTests : TestBase
{
    public HrLeaveTests(ErpFactory f) : base(f) { }

    private static string Day(DateTime d) => d.ToString("yyyy-MM-dd");
    private static readonly DateTime Today = DateTime.UtcNow.Date;

    private static async Task<Dictionary<string, string>> TypesAsync(Client api)
        => (await api.Get("/leavetypes?pageSize=100")).Data!["items"]!.AsArray().ToDictionary(t => t!["code"].S(), t => t!["id"].S());

    private static async Task<string> EmployeeAsync(Client api, string code, object? extra = null)
    {
        var body = System.Text.Json.JsonSerializer.SerializeToNode(new { code, nameAr = "موظف " + code, hireDate = Day(Today.AddYears(-2)), basicSalary = 6000, housingAllowance = 1500, transportAllowance = 0 })!.AsObject();
        if (extra != null)
            foreach (var (k, v) in System.Text.Json.JsonSerializer.SerializeToNode(extra)!.AsObject().ToList()) body[k] = v?.DeepClone();
        var r = await api.Post("/employees", body);
        Assert.Equal(201, r.Status);
        return r.Data!["id"].S();
    }

    private static Task<Res> RequestAsync(Client api, string employeeId, string typeId, DateTime start, DateTime end)
        => api.Post("/leaverequests", new { employeeId, leaveTypeId = typeId, startDate = Day(start), endDate = Day(end), reason = "اختبار" });

    [Fact]
    public async Task Default_leave_types_are_created_on_first_read_and_validated()
    {
        var api = await NewTenantAsync();
        var types = await TypesAsync(api);
        Assert.Contains("annual", types.Keys); Assert.Contains("sick_75", types.Keys); Assert.Contains("unpaid", types.Keys);
        Assert.Equal(types.Count, (await TypesAsync(api)).Count); // لا تتكرر في القراءة الثانية

        Assert.Equal(409, (await api.Post("/leavetypes", new { code = "annual", nameAr = "مكرر" })).Status);
        Assert.Equal(400, (await api.Post("/leavetypes", new { code = "x1", nameAr = "نسبة خاطئة", payPercent = 120 })).Status);
        Assert.Equal(400, (await api.Post("/leavetypes", new { code = "x2", nameAr = "سنوية بنصف أجر", isAnnual = true, payPercent = 50 })).Status);
        Assert.Equal(201, (await api.Post("/leavetypes", new { code = "study", nameAr = "إجازة دراسية", payPercent = 100, maxDaysPerYear = 10 })).Status);
    }

    [Fact]
    public async Task Annual_balance_accrues_daily_and_requests_cannot_exceed_it()
    {
        var api = await NewTenantAsync();
        var types = await TypesAsync(api);
        // سنتا خدمة بالاستحقاق الأساسي: نحو 42 يوماً
        var employee = await EmployeeAsync(api, "E1");
        var balance = (await api.Get($"/leaverequests/balance/{employee}")).Data!;
        Assert.Equal(21, balance["annualEntitlement"]!.GetValue<int>());
        Assert.InRange(balance["accrued"].D(), 41.9m, 42.2m);
        Assert.Equal(balance["accrued"].D(), balance["available"].D());

        // رصيد افتتاحي يبدأ التراكم من تاريخه، واستحقاق تعاقدي 30 يوماً
        var migrated = await EmployeeAsync(api, "E2", new { annualLeaveDays = 30, openingLeaveBalance = 10, openingLeaveBalanceDate = Day(Today.AddDays(-73)) });
        var b2 = (await api.Get($"/leaverequests/balance/{migrated}")).Data!;
        Assert.Equal(30, b2["annualEntitlement"]!.GetValue<int>());
        Assert.Equal(10, b2["opening"].D());
        Assert.InRange(b2["accrued"].D(), 6.0m, 6.2m); // 74 يوماً × 30 ÷ 365
        Assert.InRange(b2["balance"].D(), 16.0m, 16.2m);

        // طلب أكبر من الرصيد يُرفض، وضمنه يُقبل ويحجز الرصيد وهو معلّق
        Assert.Equal(400, (await RequestAsync(api, employee, types["annual"], Today.AddDays(10), Today.AddDays(70))).Status);
        var ok = await RequestAsync(api, employee, types["annual"], Today.AddDays(10), Today.AddDays(19));
        Assert.Equal(201, ok.Status);
        Assert.Equal(10, ok.Data!["days"]!.GetValue<int>());
        Assert.StartsWith("LV-", ok.Data["requestNumber"].S());
        var after = (await api.Get($"/leaverequests/balance/{employee}")).Data!;
        Assert.Equal(10, after["pending"].D()); Assert.Equal(0, after["taken"].D());
        Assert.Equal(after["balance"].D() - 10, after["available"].D());

        var all = (await api.Get("/leaverequests/balances")).Data!.AsArray();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task Request_lifecycle_enforces_overlap_limits_and_decisions()
    {
        var api = await NewTenantAsync();
        var types = await TypesAsync(api);
        var employee = await EmployeeAsync(api, "E1");

        Assert.Equal(400, (await RequestAsync(api, employee, types["annual"], Today.AddDays(5), Today.AddDays(2))).Status);        // نهاية قبل البداية
        Assert.Equal(400, (await RequestAsync(api, employee, types["paternity"], Today.AddDays(1), Today.AddDays(5))).Status);     // فوق حد النوع (3)
        Assert.Equal(400, (await RequestAsync(api, employee, types["annual"], Today.AddYears(-5), Today.AddYears(-5))).Status);    // قبل الالتحاق

        var first = await RequestAsync(api, employee, types["annual"], Today.AddDays(1), Today.AddDays(5));
        Assert.Equal(201, first.Status);
        var id = first.Data!["id"].S();
        Assert.Equal(409, (await RequestAsync(api, employee, types["sick"], Today.AddDays(5), Today.AddDays(6))).Status);           // تداخل

        // تعديل وهو معلّق، ثم اعتماد
        var edited = await api.Put($"/leaverequests/{id}", new { employeeId = employee, leaveTypeId = types["annual"], startDate = Day(Today.AddDays(1)), endDate = Day(Today.AddDays(3)) });
        Assert.Equal(200, edited.Status); Assert.Equal(3, edited.Data!["days"]!.GetValue<int>());
        var approved = await api.Post($"/leaverequests/{id}/approve", new { note = "موافق" });
        Assert.Equal(200, approved.Status); Assert.Equal("approved", approved.Data!["status"].S());
        Assert.NotNull(approved.Data["decidedAt"]);

        Assert.Equal(409, (await api.Post($"/leaverequests/{id}/approve", new { })).Status);   // بُتّ فيه
        Assert.Equal(409, (await api.Put($"/leaverequests/{id}", new { employeeId = employee, leaveTypeId = types["annual"], startDate = Day(Today.AddDays(1)), endDate = Day(Today.AddDays(2)) })).Status);
        Assert.Equal(409, (await api.Delete($"/leaverequests/{id}")).Status);                  // المعتمدة تُلغى لا تُحذف
        Assert.Equal(409, (await api.Delete($"/employees/{employee}")).Status);                // موظف له إجازات

        var balance = (await api.Get($"/leaverequests/balance/{employee}")).Data!;
        Assert.Equal(3, balance["taken"].D());

        Assert.Equal(200, (await api.Post($"/leaverequests/{id}/cancel", new { })).Status);
        Assert.Equal(0, (await api.Get($"/leaverequests/balance/{employee}")).Data!["taken"].D()); // الإلغاء يعيد الرصيد
        Assert.Equal(409, (await api.Post($"/leaverequests/{id}/cancel", new { })).Status);

        // طلب مرفوض يُحذف
        var second = await RequestAsync(api, employee, types["annual"], Today.AddDays(20), Today.AddDays(21));
        Assert.Equal(200, (await api.Post($"/leaverequests/{second.Data!["id"].S()}/reject", new { note = "ضغط عمل" })).Status);
        Assert.Equal(200, (await api.Delete($"/leaverequests/{second.Data["id"].S()}")).Status);

        var grid = await api.Get("/leaverequests/load?requireTotalCount=true&filter=" + Uri.EscapeDataString("[\"status\",\"=\",\"cancelled\"]"));
        Assert.Equal(1, grid.Body!["totalCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task Unpaid_leave_is_deducted_from_payroll_and_locked_once_the_month_is_posted()
    {
        var api = await NewTenantAsync();
        var types = await TypesAsync(api);
        var employee = await EmployeeAsync(api, "E1"); // الراتب الثابت 7500 ← أجر اليوم 250
        var month = new DateTime(Today.Year, Today.Month, 1).AddMonths(-1);
        var period = month.ToString("yyyy-MM");

        async Task<string> ApprovedAsync(string type, int fromDay, int toDay)
        {
            var r = await RequestAsync(api, employee, types[type], month.AddDays(fromDay - 1), month.AddDays(toDay - 1));
            Assert.Equal(201, r.Status);
            Assert.Equal(200, (await api.Post($"/leaverequests/{r.Data!["id"].S()}/approve", new { })).Status);
            return r.Data["id"].S();
        }
        var unpaid = await ApprovedAsync("unpaid", 3, 6);     // 4 أيام بلا أجر
        await ApprovedAsync("sick_75", 10, 13);               // 4 أيام بربع خصم = يوم واحد
        await ApprovedAsync("sick", 20, 21);                  // مدفوعة كاملة: لا خصم

        var line = (await api.Post("/payroll/preview", new { period })).Data!["lines"]![0]!;
        Assert.Equal(5, line["unpaidLeaveDays"].D());
        Assert.Equal(1250, line["leaveDeduction"].D());       // 5 × 250
        Assert.Equal(1250, line["deductions"].D());
        Assert.Equal(7500, line["gross"].D());
        Assert.Equal(6250, line["net"].D());

        // خصم يدوي يُضاف إلى خصم الإجازة
        var withManual = (await api.Post("/payroll/preview", new { period, adjustments = new[] { new { employeeId = employee, additions = 0m, deductions = 100m } } })).Data!["lines"]![0]!;
        Assert.Equal(1350, withManual["deductions"].D());

        Assert.Equal(200, (await api.Post("/payroll/post", new { period })).Status);
        // بعد ترحيل الشهر: إلغاء الإجازة غير المدفوعة أو اعتماد أخرى فيه ممنوع حتى يُعكس المسير
        Assert.Equal(409, (await api.Post($"/leaverequests/{unpaid}/cancel", new { })).Status);
        var late = await RequestAsync(api, employee, types["unpaid"], month.AddDays(24), month.AddDays(24));
        Assert.Equal(409, (await api.Post($"/leaverequests/{late.Data!["id"].S()}/approve", new { })).Status);
    }
}
