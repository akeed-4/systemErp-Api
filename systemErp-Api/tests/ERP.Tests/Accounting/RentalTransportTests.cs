using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// R2c: تسجيل المشغّل من إعدادات التأجير دون إضافة الموارد البشرية، وتعيين مشغّل السطر بفحص التعارض، ومهام النقل (مجدولة ← منفَّذة أو ملغاة).
/// </summary>
[Collection("api")]
public class RentalTransportTests : TestBase
{
    public RentalTransportTests(ErpFactory f) : base(f) { }

    private static DateTime Now => DateTime.SpecifyKind(DateTime.UtcNow.AddHours(3), DateTimeKind.Unspecified);
    private static DateTime T0 => Now.Date.AddDays(2).AddHours(8);

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");

    private async Task<(Client Api, string A, string B, Guid Customer)> SetupAsync()
    {
        var api = await NewTenantWithModulesAsync("accounting", "equipment_rental");
        var category = await api.Post("/rental/categories", new { nameAr = "معدات ثقيلة", isActive = true });
        Ok(category, "category");
        var categoryId = category.Data!["id"].S();
        Ok(await api.Post("/rental/rates", new { categoryId, basis = "day", rate = 1000, operatorRate = 300, minimumPeriods = 1, effectiveFrom = Now.Date.AddMonths(-1) }), "rate");
        async Task<string> EquipmentAsync(string name)
        {
            var eq = await api.Post("/equipment", new { nameAr = name, ownership = "owned", rate = 0 });
            Ok(eq, name);
            var id = eq.Data!["id"].S();
            Ok(await api.Put($"/rental/equipment/{id}", new { isRentable = true, rentalCategoryId = categoryId }), "rentable " + name);
            return id;
        }
        var (customer, _) = await SeedCustomerAsync(api);
        return (api, await EquipmentAsync("حفار"), await EquipmentAsync("رافعة"), customer);
    }

    private static object Line(string equipmentId, DateTime from, DateTime to, string? operatorId = null)
        => new { equipmentId, from, to, basis = "day", withOperator = true, operatorEmployeeId = operatorId };

    [Fact]
    public async Task Operator_registered_without_hr_cannot_be_on_two_overlapping_lines()
    {
        var (api, a, b, customer) = await SetupAsync();
        var staff = await api.Post("/employee-cost-rates/staff", new { purpose = "operator", nameAr = "مشغّل أول", basicSalary = 4000, costPerHour = 25 });
        Ok(staff, "register operator");
        var operatorId = staff.Data!["employeeId"].S();
        var list = await api.Get("/employee-cost-rates/staff?purpose=operator");
        Ok(list, "staff list");
        Assert.Contains(list.Data!.AsArray(), x => x!["employeeId"].S() == operatorId && x["currentCostPerHour"]!.GetValue<decimal>() == 25);

        // سطران متداخلان بالمشغّل نفسه في العقد مرفوضان
        Assert.Equal(409, (await api.Post("/rental/contracts", new
        {
            customerId = customer, term = "short", lines = new[] { Line(a, T0, T0.AddDays(3), operatorId), Line(b, T0.AddDays(1), T0.AddDays(4), operatorId) },
        })).Status);

        var first = await api.Post("/rental/contracts", new { customerId = customer, term = "short", lines = new[] { Line(a, T0, T0.AddDays(3), operatorId) } });
        Ok(first, "first contract");
        // عقد آخر متداخل بالمشغّل نفسه مرفوض، وبلا مشغّل يُقبل ثم يُعيَّن بعد انتهاء الأول
        Assert.Equal(409, (await api.Post("/rental/contracts", new { customerId = customer, term = "short", lines = new[] { Line(b, T0.AddDays(2), T0.AddDays(5), operatorId) } })).Status);
        var second = await api.Post("/rental/contracts", new { customerId = customer, term = "short", lines = new[] { Line(b, T0.AddDays(2), T0.AddDays(5)) } });
        Ok(second, "second contract");
        var secondId = second.Data!["id"].S();
        var lineId = second.Data["lines"]!.AsArray()[0]!["id"].S();
        Assert.Equal(409, (await api.Post($"/rental/contracts/{secondId}/lines/{lineId}/operator", new { operatorEmployeeId = operatorId })).Status);

        var other = await api.Post("/employee-cost-rates/staff", new { purpose = "operator", nameAr = "مشغّل ثانٍ", basicSalary = 3500 });
        Ok(other, "second operator");
        var assigned = await api.Post($"/rental/contracts/{secondId}/lines/{lineId}/operator", new { operatorEmployeeId = other.Data!["employeeId"].S() });
        Ok(assigned, "assign");
        Assert.Equal(other.Data["employeeId"].S(), assigned.Data!["lines"]!.AsArray()[0]!["operatorEmployeeId"].S());
        Assert.Contains(assigned.Data["changes"]!.AsArray(), c => c!["kind"].S() == "operator");
    }

    [Fact]
    public async Task Transport_job_lifecycle_validates_mode_and_locks_after_completion()
    {
        var (api, a, b, customer) = await SetupAsync();
        var contract = await api.Post("/rental/contracts", new { customerId = customer, term = "short", lines = new[] { Line(a, T0, T0.AddDays(3)) } });
        Ok(contract, "contract");
        var contractId = contract.Data!["id"].S();
        var lineId = contract.Data["lines"]!.AsArray()[0]!["id"].S();
        var (supplier, _) = await SeedSupplierAsync(api);

        object Job(string mode, Guid? supplierId, string? vehicle, decimal cost) => new
        {
            contractId, kind = "delivery", mode, scheduledAt = T0.AddHours(-2), fromLocation = "الساحة", toLocation = "موقع العميل",
            supplierId, transportEquipmentId = vehicle, cost, chargeAmount = 500, contractLineIds = new[] { lineId },
        };

        Assert.Equal(400, (await api.Post("/rental/transport", Job("external", null, null, 800))).Status);   // المورد إلزامي
        Assert.Equal(400, (await api.Post("/rental/transport", Job("internal", null, a, 0))).Status);       // المركبة من المنقول
        Assert.Equal(400, (await api.Post("/rental/transport", Job("internal", null, null, -1))).Status);   // تكلفة سالبة

        var job = await api.Post("/rental/transport", Job("external", supplier, null, 800));
        Ok(job, "create");
        var jobId = job.Data!["id"].S();
        Assert.StartsWith("RTJ-", job.Data["jobNumber"].S());
        Assert.Equal("planned", job.Data["status"].S());

        Ok(await api.Put($"/rental/transport/{jobId}", Job("internal", null, b, 300)), "update to internal");
        Assert.Equal(400, (await api.Post($"/rental/transport/{jobId}/complete", new { completedAt = Now.AddDays(1) })).Status);
        var done = await api.Post($"/rental/transport/{jobId}/complete", new { cost = 350 });
        Ok(done, "complete");
        Assert.Equal("completed", done.Data!["status"].S());
        Assert.Equal(350m, done.Data["cost"]!.GetValue<decimal>());

        Assert.Equal(409, (await api.Put($"/rental/transport/{jobId}", Job("internal", null, null, 0))).Status);
        Assert.Equal(409, (await api.Post($"/rental/transport/{jobId}/cancel", new { reason = "x" })).Status);
        Assert.Equal(409, (await api.Delete($"/rental/transport/{jobId}")).Status);

        var second = await api.Post("/rental/transport", Job("internal", null, null, 0));
        Ok(second, "second");
        Assert.Equal(400, (await api.Post($"/rental/transport/{second.Data!["id"].S()}/cancel", new { reason = "" })).Status);
        var cancelled = await api.Post($"/rental/transport/{second.Data["id"].S()}/cancel", new { reason = "أجّل العميل" });
        Ok(cancelled, "cancel");
        Assert.Equal("cancelled", cancelled.Data!["status"].S());
        Assert.Equal(2, (await api.Get($"/rental/transport?contractId={contractId}")).Data!.AsArray().Count);
    }
}
