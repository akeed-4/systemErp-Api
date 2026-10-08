using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>السلف (صرف بقيد، خصم الأقساط من المسير، إلغاء) ومكافأة نهاية الخدمة وتصفية المستحقات.</summary>
[Collection("api")]
public class HrLoanAndEndOfServiceTests : TestBase
{
    public HrLoanAndEndOfServiceTests(ErpFactory f) : base(f) { }

    private static readonly DateTime Today = DateTime.UtcNow.Date;
    private static string Day(DateTime d) => d.ToString("yyyy-MM-dd");
    private static async Task<decimal> BalanceAsync(Client api, string code) => (await api.Get($"/accounts/ByCode/{code}")).Data!["balance"].D();

    private static async Task<string> EmployeeAsync(Client api, string code, DateTime hireDate, decimal basic = 6000, decimal housing = 1500)
    {
        var r = await api.Post("/employees", new { code, nameAr = "موظف " + code, hireDate = Day(hireDate), basicSalary = basic, housingAllowance = housing });
        Assert.Equal(201, r.Status);
        return r.Data!["id"].S();
    }

    [Fact]
    public async Task Loan_is_posted_deducted_by_payroll_installments_and_restored_on_reversal()
    {
        var api = await NewTenantAsync();
        var employee = await EmployeeAsync(api, "E1", Today.AddYears(-1)); // الراتب 7500
        var month = new DateTime(Today.Year, Today.Month, 1).AddMonths(-3); // ثلاثة أشهر مكتملة قبل الشهر الحالي
        string Period(int offset) => month.AddMonths(offset).ToString("yyyy-MM");

        object Loan(decimal amount, decimal installment, string period) => new { employeeId = employee, date = Day(month), amount, installmentAmount = installment, firstDeductionPeriod = period };
        Assert.Equal(400, (await api.Post("/employeeloans", Loan(0, 100, Period(0)))).Status);
        Assert.Equal(400, (await api.Post("/employeeloans", Loan(1000, 2000, Period(0)))).Status);     // قسط أكبر من السلفة
        Assert.Equal(400, (await api.Post("/employeeloans", Loan(1000, 500, "2025/01"))).Status);      // صيغة شهر خاطئة

        var cashBefore = await BalanceAsync(api, "1111");
        var created = await api.Post("/employeeloans", Loan(2500, 1000, Period(0)));
        Assert.Equal(201, created.Status);
        var loanId = created.Data!["id"].S();
        Assert.StartsWith("LN-", created.Data["loanNumber"].S());
        Assert.Equal(2500, Math.Abs(await BalanceAsync(api, "115")));                // ذمة على الموظف
        Assert.Equal(2500, Math.Abs(await BalanceAsync(api, "1111") - cashBefore)); // خرجت من الصندوق

        // الشهر الأول: قسط 1000
        var line = (await api.Post("/payroll/preview", new { period = Period(0) })).Data!["lines"]![0]!;
        Assert.Equal(1000, line["loanDeduction"].D());
        Assert.Equal(6500, line["net"].D());
        Assert.Equal(0, line["deductions"].D()); // القسط ليس خصماً من المصروف
        var first = await api.Post("/payroll/post", new { period = Period(0) });
        Assert.Equal(200, first.Status);
        Assert.Equal(1500, Math.Abs(await BalanceAsync(api, "115")));
        Assert.Equal(7500, Math.Abs(await BalanceAsync(api, "526"))); // مصروف الرواتب كاملاً
        var (debit, credit) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(debit, credit);
        Assert.Equal(409, (await api.Post($"/employeeloans/{loanId}/cancel", new { })).Status); // خُصم منها قسط

        // الشهر الثاني 1000، والثالث الباقي 500 وتُقفل
        Assert.Equal(200, (await api.Post("/payroll/post", new { period = Period(1) })).Status);
        var last = (await api.Post("/payroll/preview", new { period = Period(2) })).Data!["lines"]![0]!;
        Assert.Equal(500, last["loanDeduction"].D());
        var third = await api.Post("/payroll/post", new { period = Period(2) });
        var loan = (await api.Get($"/employeeloans/{loanId}")).Data!;
        Assert.Equal("settled", loan["status"].S()); Assert.Equal(0, loan["remaining"].D());
        Assert.Equal(3, loan["repayments"]!.AsArray().Count);
        Assert.Equal(0, await BalanceAsync(api, "115"));

        // عكس آخر مسير يعيد قسطه ويفتح السلفة
        Assert.Equal(200, (await api.Post($"/payroll/{third.Data!["id"].S()}/reverse")).Status);
        loan = (await api.Get($"/employeeloans/{loanId}")).Data!;
        Assert.Equal("active", loan["status"].S()); Assert.Equal(500, loan["remaining"].D());
        Assert.Equal(500, Math.Abs(await BalanceAsync(api, "115")));

        // سلفة لم يُخصم منها شيء تُلغى ويُعكس قيدها
        var other = await api.Post("/employeeloans", new { employeeId = employee, date = Day(Today), amount = 800, installmentAmount = 400, firstDeductionPeriod = Today.AddMonths(6).ToString("yyyy-MM") });
        Assert.Equal(200, (await api.Put($"/employeeloans/{other.Data!["id"].S()}/schedule", new { installmentAmount = 200, firstDeductionPeriod = Today.AddMonths(7).ToString("yyyy-MM") })).Status);
        Assert.Equal(200, (await api.Post($"/employeeloans/{other.Data["id"].S()}/cancel", new { })).Status);
        Assert.Equal(500, Math.Abs(await BalanceAsync(api, "115")));
    }

    [Theory]
    [InlineData("termination", 3, 1.0, 11250)]      // 3 سنوات × نصف شهر
    [InlineData("termination", 8, 1.0, 41250)]      // 5 × نصف شهر + 3 × شهر
    [InlineData("resignation", 1, 0.0, 0)]          // أقل من سنتين
    [InlineData("resignation", 3, 0.3333, 3750)]    // ثلث 11250
    [InlineData("resignation", 8, 0.6667, 27500)]   // ثلثا 41250
    [InlineData("resignation", 12, 1.0, 71250)]     // 5 × نصف + 7 × شهر
    [InlineData("dismissal", 8, 0.0, 0)]
    public async Task End_of_service_award_follows_the_labor_law_tiers(string reason, int years, double factor, double award)
    {
        var api = await NewTenantAsync();
        var lastDay = Today;
        // مدة الخدمة تُحسب بالأيام ÷ 365: نختار تاريخ الالتحاق ليعطي عدد السنين بالضبط
        var employee = await EmployeeAsync(api, "E1", lastDay.AddDays(-(years * 365 - 1)));
        var p = (await api.Post("/endofservice/preview", new { employeeId = employee, lastWorkingDay = Day(lastDay), reason })).Data!;
        Assert.Equal(years, p["serviceYears"].D());
        Assert.Equal(7500, p["wage"].D());
        Assert.Equal((decimal)factor, p["awardFactor"].D());
        Assert.Equal((decimal)award, p["award"].D());
    }

    [Fact]
    public async Task Settlement_pays_award_and_leave_balance_settles_loans_and_terminates_the_employee()
    {
        var api = await NewTenantAsync();
        var lastDay = Today;
        var employee = await EmployeeAsync(api, "E1", lastDay.AddDays(-(2 * 365 - 1))); // سنتان بالضبط
        var loan = await api.Post("/employeeloans", new { employeeId = employee, date = Day(Today.AddDays(-10)), amount = 2000, installmentAmount = 500, firstDeductionPeriod = Today.AddMonths(1).ToString("yyyy-MM") });
        Assert.Equal(201, loan.Status);

        object Request(decimal additions = 0, decimal deductions = 0) => new { employeeId = employee, lastWorkingDay = Day(lastDay), reason = "termination", otherAdditions = additions, otherDeductions = deductions };
        Assert.Equal(400, (await api.Post("/endofservice/preview", new { employeeId = employee, lastWorkingDay = Day(lastDay), reason = "unknown" })).Status);
        Assert.Equal(400, (await api.Post("/endofservice/preview", Request(deductions: 100000))).Status); // صافٍ سالب

        var p = (await api.Post("/endofservice/preview", Request(additions: 1000))).Data!;
        Assert.Equal(7500, p["award"].D());                    // سنتان × نصف شهر
        Assert.Equal(42, p["leaveBalanceDays"].D());           // 730 يوماً × 21 ÷ 365
        Assert.Equal(10500, p["leavePayout"].D());             // 42 × 250
        Assert.Equal(2000, p["loanDeduction"].D());
        Assert.Equal(17000, p["net"].D());                     // 7500 + 10500 + 1000 − 2000

        var posted = await api.Post("/endofservice", Request(additions: 1000));
        Assert.Equal(200, posted.Status);
        Assert.StartsWith("EOS-", posted.Data!["settlementNumber"].S());
        Assert.Equal(7500, Math.Abs(await BalanceAsync(api, "528")));
        Assert.Equal(11500, Math.Abs(await BalanceAsync(api, "526")));  // بدل الإجازات + المستحقات
        Assert.Equal(17000, Math.Abs(await BalanceAsync(api, "214")));
        Assert.Equal(0, await BalanceAsync(api, "115"));                // السلفة سُوّيت
        var (debit, credit) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(debit, credit);

        var person = (await api.Get($"/employees/{employee}")).Data!;
        Assert.Equal("terminated", person["status"].S());
        Assert.Equal("settled", (await api.Get($"/employeeloans/{loan.Data!["id"].S()}")).Data!["status"].S());
        Assert.Equal(409, (await api.Post("/endofservice", Request())).Status); // لا تصفية ثانية

        // العكس يعيد الموظف والسلفة
        Assert.Equal(200, (await api.Post($"/endofservice/{posted.Data["id"].S()}/reverse")).Status);
        Assert.Equal("active", (await api.Get($"/employees/{employee}")).Data!["status"].S());
        Assert.Equal(2000, Math.Abs(await BalanceAsync(api, "115")));
        Assert.Equal(0, await BalanceAsync(api, "528"));
        Assert.Equal(1, (await api.Get("/endofservice/load?requireTotalCount=true")).Body!["totalCount"]!.GetValue<int>());
    }
}
