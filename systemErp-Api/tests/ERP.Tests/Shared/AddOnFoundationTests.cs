using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// أساس إضافتي الورش والتأجير: تهيئة حسابات كل إضافة (معاينة بلا كتابة، ثم إنشاء الناقص وربطه، والتكرار لا يكرر شيئاً، وغير المرخّصة
/// مرفوضة)، وسجل التكلفة المعيارية لساعة الموظف بغرضه (الإضافة المطلوبة، التاريخ الفريد، الساري اليوم، المستقبلي، الحذف).
/// </summary>
[Collection("api")]
public class AddOnFoundationTests : TestBase
{
    public AddOnFoundationTests(ErpFactory f) : base(f) { }

    private static readonly string[] WorkshopRoles =
        { "workshopWipAccountCode", "workshopAbsorbedLaborAccountCode", "workshopCostOfRevenueAccountCode", "workshopWarrantyExpenseAccountCode" };
    private static readonly string[] RentalRoles = { "rentalRevenueAccountCode", "rentalCompensationIncomeAccountCode", "rentalDepositsLiabilityAccountCode" };

    private static async Task<string> TypeOfAsync(Client api, string code)
        => (await api.Get($"/accounts/ByCode/{code}")).Data!["type"].S().ToLowerInvariant();

    [Fact]
    public async Task Workshop_accounts_preview_then_create_once_and_link_to_settings()
    {
        var api = await NewTenantWithModulesAsync("accounting", "workshop");
        foreach (var role in WorkshopRoles) Assert.Null((await api.Get("/accounting-settings")).Data![role]);

        var preview = (await api.Get("/accounting-settings/module-accounts/workshop")).Data!;
        Assert.True(preview["isPreview"]!.GetValue<bool>());
        Assert.Equal(WorkshopRoles.Length, preview["items"]!.AsArray().Count);
        foreach (var role in WorkshopRoles) Assert.Null((await api.Get("/accounting-settings")).Data![role]); // المعاينة لا تكتب

        var first = await api.Post("/accounting-settings/module-accounts/workshop");
        Assert.Equal(200, first.Status);
        Assert.Equal(0, first.Data!["problems"]!.GetValue<int>());
        Assert.Equal(WorkshopRoles.Length, first.Data["created"]!.GetValue<int>() + first.Data["reused"]!.GetValue<int>());

        var settings = (await api.Get("/accounting-settings")).Data!;
        Assert.Equal("asset", await TypeOfAsync(api, settings["workshopWipAccountCode"].S()));
        foreach (var role in WorkshopRoles.Skip(1)) Assert.Equal("expense", await TypeOfAsync(api, settings[role].S()));
        Assert.Equal(WorkshopRoles.Length, WorkshopRoles.Select(r => settings[r].S()).Distinct().Count());

        // التكرار: كلها باقية بلا إنشاء
        var again = (await api.Post("/accounting-settings/module-accounts/workshop")).Data!;
        Assert.Equal(0, again["created"]!.GetValue<int>());
        Assert.Equal(WorkshopRoles.Length, again["kept"]!.GetValue<int>());

        // حسابات التأجير لا تُنشأ لمنشأة بلا إضافته، والمفتاح المجهول غير موجود
        Assert.Equal(403, (await api.Post("/accounting-settings/module-accounts/equipment_rental")).Status);
        Assert.Equal(404, (await api.Get("/accounting-settings/module-accounts/construction")).Status);
        foreach (var role in RentalRoles) Assert.Null((await api.Get("/accounting-settings")).Data![role]);
    }

    [Fact]
    public async Task Rental_accounts_have_their_types_and_tenants_without_add_ons_are_unchanged()
    {
        var api = await NewTenantWithModulesAsync("accounting", "equipment_rental");
        Assert.Equal(200, (await api.Post("/accounting-settings/module-accounts/equipment_rental")).Status);
        var settings = (await api.Get("/accounting-settings")).Data!;
        Assert.Equal("revenue", await TypeOfAsync(api, settings["rentalRevenueAccountCode"].S()));
        Assert.Equal("revenue", await TypeOfAsync(api, settings["rentalCompensationIncomeAccountCode"].S()));
        Assert.Equal("liability", await TypeOfAsync(api, settings["rentalDepositsLiabilityAccountCode"].S()));

        // منشأة بلا الإضافات: لا حسابات لها، وإعداداتها كما هي
        var plain = await NewTenantAsync();
        Assert.Equal(403, (await plain.Get("/accounting-settings/module-accounts/workshop")).Status);
        foreach (var role in WorkshopRoles.Concat(RentalRoles)) Assert.Null((await plain.Get("/accounting-settings")).Data![role]);
    }

    [Fact]
    public async Task Standard_cost_rates_are_history_per_employee_and_purpose()
    {
        var api = await NewTenantWithModulesAsync("accounting", "hr", "workshop");
        var emp = await api.Post("/employees", new { code = "T1", nameAr = "فني كهرباء", hireDate = "2025-01-01", basicSalary = 5000 });
        Assert.True(emp.Status is 200 or 201, emp.Body?.ToJsonString());
        var employeeId = emp.Data!["id"].S();
        var today = DateTime.UtcNow.AddHours(3).Date;

        var past = await api.Post("/employee-cost-rates", new { employeeId, purpose = "technician", costPerHour = 40, effectiveFrom = today.AddMonths(-2) });
        Assert.Equal(200, past.Status);
        Assert.Equal(200, (await api.Post("/employee-cost-rates", new { employeeId, purpose = "technician", costPerHour = 45, effectiveFrom = today.AddDays(-5) })).Status);
        Assert.Equal(200, (await api.Post("/employee-cost-rates", new { employeeId, purpose = "technician", costPerHour = 50, effectiveFrom = today.AddMonths(1) })).Status);

        // تاريخ مكرر، وتكلفة سالبة، وغرض مجهول، وغرض إضافة غير مرخّصة
        Assert.Equal(400, (await api.Post("/employee-cost-rates", new { employeeId, purpose = "technician", costPerHour = 47, effectiveFrom = today.AddDays(-5) })).Status);
        Assert.Equal(400, (await api.Post("/employee-cost-rates", new { employeeId, purpose = "technician", costPerHour = -1 })).Status);
        Assert.Equal(400, (await api.Post("/employee-cost-rates", new { employeeId, purpose = "driver", costPerHour = 10 })).Status);
        Assert.Equal(403, (await api.Post("/employee-cost-rates", new { employeeId, purpose = "operator", costPerHour = 30 })).Status);
        Assert.Equal(404, (await api.Post("/employee-cost-rates", new { employeeId = Guid.NewGuid(), purpose = "technician", costPerHour = 30 })).Status);

        var list = (await api.Get($"/employee-cost-rates?purpose=technician&employeeId={employeeId}")).Data!.AsArray();
        Assert.Equal(3, list.Count);
        var current = list.Single(r => r!["isCurrent"]!.GetValue<bool>())!;
        Assert.Equal(45, current["costPerHour"].D()); // الساري اليوم، لا المستقبلي ولا القديم
        Assert.Equal("T1", current["employeeCode"].S());

        // حذف الساري يعيد القديم سارياً
        Assert.Equal(200, (await api.Delete($"/employee-cost-rates/{current["id"].S()}")).Status);
        list = (await api.Get($"/employee-cost-rates?purpose=technician&employeeId={employeeId}")).Data!.AsArray();
        Assert.Equal(40, list.Single(r => r!["isCurrent"]!.GetValue<bool>())!["costPerHour"].D());

        // منشأة بلا الإضافتين لا تصل للمسار
        var plain = await NewTenantAsync();
        Assert.Equal(403, (await plain.Get("/employee-cost-rates?purpose=technician")).Status);
    }
}
