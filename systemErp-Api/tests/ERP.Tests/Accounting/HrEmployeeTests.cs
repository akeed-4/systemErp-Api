using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>شؤون الموظفين: ملف الموظف (وثائق وعقد وقسم)، تنبيهات الانتهاء، المؤشرات، وصلاحية الشاشة.</summary>
[Collection("api")]
public class HrEmployeeTests : TestBase
{
    public HrEmployeeTests(ErpFactory f) : base(f) { }

    private static string Day(int offset) => DateTime.UtcNow.Date.AddDays(offset).ToString("yyyy-MM-dd");

    [Fact]
    public async Task Employee_profile_is_validated_and_linked_to_a_department()
    {
        var api = await NewTenantAsync();
        var dept = await api.Post("/departments", new { code = "D-SALES", nameAr = "المبيعات" });
        Assert.Equal(201, dept.Status);
        Assert.Equal(409, (await api.Post("/departments", new { code = "D-SALES", nameAr = "مكرر" })).Status);
        var deptId = dept.Data!["id"].S();

        var created = await api.Post("/employees", new
        {
            code = "E1", nameAr = "أحمد", hireDate = "2025-01-01", basicSalary = 6000, departmentId = deptId,
            idType = "iqama", nationalId = "2123456789", idExpiryDate = Day(400), nationality = "EG", phone = "0500000000",
            email = "ahmed@example.com", iban = "sa03 8000 0000 6080 1016 7519", contractType = "fixed", contractEndDate = Day(700),
        });
        Assert.Equal(201, created.Status);
        Assert.Equal("SA0380000000608010167519", created.Data!["iban"].S()); // يُوحَّد: بلا فراغات وبأحرف كبيرة
        Assert.Equal("iqama", created.Data["idType"].S());
        Assert.Equal(deptId, created.Data["departmentId"].S());

        object Base(object extra) => Merge(new { code = "E2", nameAr = "موظف", hireDate = "2025-01-01", basicSalary = 3000 }, extra);
        Assert.Equal(400, (await api.Post("/employees", Base(new { contractType = "fixed" }))).Status);                       // بلا نهاية عقد
        Assert.Equal(400, (await api.Post("/employees", Base(new { contractEndDate = "2024-01-01" }))).Status);              // نهاية قبل البداية
        Assert.Equal(400, (await api.Post("/employees", Base(new { idType = "passport" }))).Status);
        Assert.Equal(400, (await api.Post("/employees", Base(new { email = "not-an-email" }))).Status);
        Assert.Equal(400, (await api.Post("/employees", Base(new { iban = "SA123" }))).Status);
        Assert.Equal(400, (await api.Post("/employees", Base(new { status = "terminated" }))).Status);                       // بلا تاريخ نهاية خدمة
        Assert.Equal(400, (await api.Post("/employees", Base(new { departmentId = Guid.NewGuid() }))).Status);

        Assert.Equal(409, (await api.Delete($"/departments/{deptId}")).Status); // قسم له موظفون
        Assert.Equal(200, (await api.Delete($"/employees/{created.Data["id"].S()}")).Status);
        Assert.Equal(200, (await api.Delete($"/departments/{deptId}")).Status);
    }

    [Fact]
    public async Task Expiring_documents_and_contracts_are_listed_nearest_first_for_active_employees_only()
    {
        var api = await NewTenantAsync();
        async Task<string> Add(string code, object extra, string status = "active")
        {
            var r = await api.Post("/employees", Merge(new { code, nameAr = "موظف " + code, hireDate = "2024-01-01", basicSalary = 3000, status }, extra));
            Assert.Equal(201, r.Status);
            return r.Data!["id"].S();
        }
        await Add("E1", new { idType = "iqama", idExpiryDate = Day(10), passportExpiryDate = Day(200) });   // الإقامة فقط ضمن 60 يوماً
        await Add("E2", new { idExpiryDate = Day(-5) });                                                    // منتهية: تظهر
        await Add("E3", new { contractType = "fixed", contractEndDate = Day(30), probationEndDate = Day(-3) }); // تجربة منتهية لا تظهر
        await Add("E4", new { idExpiryDate = Day(5) }, status: "inactive");                                 // غير نشط: لا يظهر
        await Add("E5", new { probationEndDate = Day(20) });

        var alerts = (await api.Get("/employees/expiring?days=60")).Data!.AsArray();
        Assert.Equal(new[] { "E2:id", "E1:id", "E5:probation", "E3:contract" },
            alerts.Select(a => $"{a!["employeeCode"].S()}:{a["type"].S()}").ToArray());
        Assert.Equal(-5, alerts[0]!["daysLeft"]!.GetValue<int>());
        Assert.Equal(10, alerts[1]!["daysLeft"]!.GetValue<int>());

        // مدى أوسع يلتقط جواز E1
        var wide = (await api.Get("/employees/expiring?days=365")).Data!.AsArray();
        Assert.Contains(wide, a => a!["employeeCode"].S() == "E1" && a["type"].S() == "passport");
    }

    [Fact]
    public async Task Summary_counts_headcount_saudization_and_monthly_cost_for_active_employees()
    {
        var api = await NewTenantAsync();
        var dept = (await api.Post("/departments", new { code = "D1", nameAr = "الإدارة" })).Data!["id"].S();
        await api.Post("/employees", new { code = "E1", nameAr = "سعودي", hireDate = "2024-01-01", basicSalary = 8000, housingAllowance = 2000, employerGosiRate = 11.75, departmentId = dept });
        await api.Post("/employees", new { code = "E2", nameAr = "مقيم", hireDate = "2024-01-01", basicSalary = 4000, transportAllowance = 500, idType = "iqama", employerGosiRate = 2 });
        await api.Post("/employees", new { code = "E3", nameAr = "منتهٍ", hireDate = "2024-01-01", basicSalary = 9000, status = "terminated", terminationDate = "2025-06-30" });

        var s = (await api.Get("/employees/summary")).Data!;
        Assert.Equal(2, s["activeCount"]!.GetValue<int>());
        Assert.Equal(1, s["terminatedCount"]!.GetValue<int>());
        Assert.Equal(1, s["saudiCount"]!.GetValue<int>());
        Assert.Equal(50, s["saudizationPercent"].D());
        Assert.Equal(14500, s["monthlyGross"].D());          // 10000 + 4500
        Assert.Equal(1255, s["monthlyEmployerGosi"].D());    // 11.75% × 10000 + 2% × 4000
        var departments = s["departments"]!.AsArray();
        Assert.Equal(2, departments.Count);
        Assert.Contains(departments, d => d!["departmentName"]?.GetValue<string>() == "الإدارة" && d["count"]!.GetValue<int>() == 1);

        // المنتهية خدمته لا يدخل مسير الرواتب
        var preview = (await api.Post("/payroll/preview", new { period = DateTime.UtcNow.AddMonths(-1).ToString("yyyy-MM") })).Data!;
        Assert.Equal(2, preview["lines"]!.AsArray().Count);
    }

    [Fact]
    public async Task Hr_endpoints_require_authentication_and_are_isolated_per_tenant()
    {
        var first = await NewTenantAsync();
        await first.Post("/employees", new { code = "E1", nameAr = "موظف", hireDate = "2024-01-01", basicSalary = 3000, idExpiryDate = Day(3) });
        var second = await NewTenantAsync();

        Assert.Empty((await second.Get("/employees/expiring")).Data!.AsArray());
        Assert.Equal(0, (await second.Get("/employees/summary")).Data!["activeCount"]!.GetValue<int>());
        Assert.Equal(0, (await second.Get("/employees/load?requireTotalCount=true")).Body!["totalCount"]!.GetValue<int>());

        var anonymous = new Client(NewHttp());
        Assert.Equal(401, (await anonymous.Get("/employees/summary")).Status);
        Assert.Equal(401, (await anonymous.Get("/departments")).Status);
    }

    /// <summary>يدمج خصائص كائنين مجهولين في جسم طلب واحد (الثاني يغلب).</summary>
    private static JsonObject Merge(object a, object b)
    {
        var result = System.Text.Json.JsonSerializer.SerializeToNode(a)!.AsObject();
        foreach (var (key, value) in System.Text.Json.JsonSerializer.SerializeToNode(b)!.AsObject().ToList())
            result[key] = value?.DeepClone();
        return result;
    }
}
