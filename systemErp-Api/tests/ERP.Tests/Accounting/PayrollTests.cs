using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>الرواتب: المسير الشهري يُحسب من بنود الموظف ونسب تأميناته، ويرحّل قيداً واحداً متوازناً، مرة لكل شهر.</summary>
[Collection("api")]
public class PayrollTests : TestBase
{
    public PayrollTests(ErpFactory f) : base(f) { }

    private static async Task<decimal> BalanceAsync(Client api, string code) => (await api.Get($"/accounts/ByCode/{code}")).Data!["balance"].D();

    [Fact]
    public async Task Monthly_payroll_posts_one_balanced_entry_once_per_period_and_can_be_reversed()
    {
        var api = await NewTenantAsync();
        var saudi = await api.Post("/employees", new { code = "E1", nameAr = "موظف سعودي", hireDate = "2025-01-01", basicSalary = 8000, housingAllowance = 2000,
            transportAllowance = 500, employeeGosiRate = 9.75, employerGosiRate = 11.75, status = "active" });
        Assert.Equal(201, saudi.Status);
        Assert.Equal(201, (await api.Post("/employees", new { code = "E2", nameAr = "موظف غير سعودي", hireDate = "2025-01-01", basicSalary = 4000, housingAllowance = 1000,
            employeeGosiRate = 0, employerGosiRate = 2, status = "active" })).Status);
        Assert.Equal(409, (await api.Post("/employees", new { code = "E1", nameAr = "مكرر", hireDate = "2025-01-01", basicSalary = 1 })).Status);
        // التحق بعد الشهر: لا يدخل المسير
        Assert.Equal(201, (await api.Post("/employees", new { code = "E3", nameAr = "لاحق", hireDate = "2030-01-01", basicSalary = 3000 })).Status);

        var period = DateTime.UtcNow.AddMonths(-1).ToString("yyyy-MM");
        object Request() => new { period, adjustments = new[] { new { employeeId = saudi.Data!["id"].S(), additions = 300m, deductions = 100m } } };

        var preview = (await api.Post("/payroll/preview", Request())).Data!;
        Assert.Equal(2, preview["lines"]!.AsArray().Count);
        // E1: إجمالي 10800، تأمينات 9.75% من 10000 = 975، صافي 10800 − 100 − 975 = 9725 | E2: صافي 5000
        Assert.Equal(15800, preview["totalGross"].D()); Assert.Equal(975, preview["totalEmployeeGosi"].D());
        Assert.Equal(1275, preview["totalEmployerGosi"].D()); Assert.Equal(14725, preview["totalNet"].D());

        var (debitBefore, creditBefore) = Totals(await api.Get("/reports/TrialBalance"));
        var posted = await api.Post("/payroll/post", Request());
        Assert.Equal(200, posted.Status);
        Assert.StartsWith("PAY-", posted.Data!["runNumber"].S());
        Assert.Equal(14725, Math.Abs(await BalanceAsync(api, "214")));
        Assert.Equal(2250, Math.Abs(await BalanceAsync(api, "215")));  // 975 + 1275
        Assert.Equal(15700, Math.Abs(await BalanceAsync(api, "526"))); // 15800 − 100
        Assert.Equal(1275, Math.Abs(await BalanceAsync(api, "527")));
        var (debit, credit) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(debit, credit);

        Assert.Equal(409, (await api.Post("/payroll/post", Request())).Status); // الشهر مرحَّل
        Assert.Equal(409, (await api.Delete($"/journalentries/{posted.Data["journalEntryId"].S()}")).Status); // القيد ملك المسير

        Assert.Equal(200, (await api.Post($"/payroll/{posted.Data["id"].S()}/reverse")).Status);
        Assert.Equal(0, await BalanceAsync(api, "214"));
        Assert.Equal(200, (await api.Post("/payroll/post", Request())).Status); // يُعاد بعد العكس
    }
}
