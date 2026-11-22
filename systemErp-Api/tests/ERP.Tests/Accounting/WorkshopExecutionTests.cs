using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// ورش السيارات W3: ساعات الفني بتكلفته المعيارية وقيد العمالة، وحجز القطع وصرفها وإرجاعها بحركة مخزون وقيد، ومنع تنفيذ غير الموافق،
/// ومنع إلغاء أمر عليه تكاليف.
/// </summary>
[Collection("api")]
public class WorkshopExecutionTests : TestBase
{
    public WorkshopExecutionTests(ErpFactory f) : base(f) { }

    private static DateTime Now => DateTime.SpecifyKind(DateTime.UtcNow.AddHours(3), DateTimeKind.Unspecified);

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");

    [Fact]
    public async Task Hours_and_parts_post_to_work_in_progress_only_for_approved_lines()
    {
        var api = await NewTenantWithModulesAsync("accounting", "workshop");
        var serviceId = await WorkshopReceptionTests.SeedServiceAsync(api);
        var part = await SeedProductAsync(api, "PAD", stock: 10, cost: 40, price: 90);
        var tech = await api.Post("/employee-cost-rates/staff", new { purpose = "technician", nameAr = "فني أول", basicSalary = 5000, costPerHour = 60, hireDate = Now.Date.AddDays(-7) });
        Ok(tech, "technician");
        var techId = tech.Data!["employeeId"].S();
        var (customer, _) = await SeedCustomerAsync(api);
        var reception = await api.Post("/workshop/receptions", new
        {
            customerId = customer, receivedAt = Now.AddHours(-5),
            vehicles = new[] { new { newVehicle = new { plateNumber = "ك م ن 111", make = "هوندا" }, odometerIn = 10000, damages = Array.Empty<object>() } },
        });
        Ok(reception, "reception");
        var orderId = reception.Data!["orders"]!.AsArray()[0]!["id"].S();

        Ok(await api.Post($"/workshop/work-orders/{orderId}/lines", new { kind = "labor", serviceId, quantity = 1 }), "labor");
        Ok(await api.Post($"/workshop/work-orders/{orderId}/lines", new { kind = "part", productId = part, quantity = 4 }), "part");
        var work = await api.Get($"/workshop/work-orders/{orderId}/work");
        var lines = work.Data!["lines"]!.AsArray();
        string laborLine = lines[0]!["id"].S(), partLine = lines[1]!["id"].S();

        // الحجز ممكن قبل الموافقة، والتنفيذ لا
        Ok(await api.Post($"/workshop/work-orders/{orderId}/parts/reserve", new { lineId = partLine, quantity = 3 }), "reserve");
        Assert.Equal(400, (await api.Post($"/workshop/work-orders/{orderId}/parts/reserve", new { lineId = partLine, quantity = 2 })).Status); // يتجاوز كمية السطر

        var estimate = await api.Post($"/workshop/work-orders/{orderId}/estimates", new { });
        Ok(estimate, "estimate");
        var estimateId = estimate.Data!["id"].S();
        Ok(await api.Post($"/workshop/work-orders/{orderId}/estimates/{estimateId}/send"), "send");
        Assert.Equal(409, (await api.Post($"/workshop/work-orders/{orderId}/parts/issue", new { lineId = partLine, quantity = 1 })).Status); // الأمر ليس في التنفيذ
        Ok(await api.Post($"/workshop/work-orders/{orderId}/estimates/{estimateId}/respond", new
        {
            channel = "in_person", respondedByName = "العميل",
            decisions = new[] { new { lineId = laborLine, decision = "approved" }, new { lineId = partLine, decision = "approved" } },
        }), "respond");
        Ok(await api.Post($"/workshop/work-orders/{orderId}/status", new { status = "in_progress" }), "in progress");

        // الساعات: ساعة ونصف × 60 = 90 تُقيَّد على تحت التنفيذ
        var log = await api.Post($"/workshop/work-orders/{orderId}/time-logs", new
        {
            employeeId = techId, kind = "work", lineId = laborLine, startedAt = Now.AddHours(-3), endedAt = Now.AddHours(-1.5),
        });
        Ok(log, "time log");
        Assert.Equal(90m, log.Data!["minutes"]!.GetValue<decimal>());
        Assert.Equal(90m, log.Data["cost"]!.GetValue<decimal>());
        Assert.True(log.Data["posted"]!.GetValue<bool>());
        Assert.Equal(409, (await api.Post($"/workshop/work-orders/{orderId}/time-logs", new
        {
            employeeId = techId, kind = "work", lineId = laborLine, startedAt = Now.AddHours(-2), endedAt = Now.AddHours(-1),
        })).Status); // تداخل
        var open = await api.Post($"/workshop/work-orders/{orderId}/time-logs", new { employeeId = techId, kind = "work", lineId = laborLine, startedAt = Now.AddMinutes(-30) });
        Ok(open, "open log");
        Assert.Equal(409, (await api.Post($"/workshop/work-orders/{orderId}/time-logs", new { employeeId = techId, kind = "work", lineId = laborLine })).Status);
        Assert.Single((await api.Get("/workshop/time-logs/open")).Data!.AsArray());
        Ok(await api.Post($"/workshop/work-orders/{orderId}/time-logs/{open.Data!["id"].S()}/stop", new { }), "stop");

        // القطع: صرف 2 بتكلفة 40 يستهلك الحجز، وتجاوز السطر مرفوض، وإرجاع 1 بتكلفته
        var issued = await api.Post($"/workshop/work-orders/{orderId}/parts/issue", new { lineId = partLine, quantity = 2 });
        Ok(issued, "issue");
        var issue = issued.Data!["issues"]!.AsArray()[0]!;
        Assert.Equal(80m, issue["totalCost"]!.GetValue<decimal>());
        Assert.True(issue["posted"]!.GetValue<bool>());
        Assert.Equal(1m, issued.Data["parts"]!.AsArray()[0]!["reserved"]!.GetValue<decimal>());
        Assert.Equal(8m, issued.Data["parts"]!.AsArray()[0]!["warehouseStock"]!.GetValue<decimal>());
        Assert.Equal(400, (await api.Post($"/workshop/work-orders/{orderId}/parts/issue", new { lineId = partLine, quantity = 3 })).Status);
        var issueId = issue["id"].S();
        Assert.Equal(400, (await api.Post($"/workshop/work-orders/{orderId}/parts/return", new { issueId, quantity = 3 })).Status);
        var back = await api.Post($"/workshop/work-orders/{orderId}/parts/return", new { issueId, quantity = 1 });
        Ok(back, "return");
        Assert.Equal(40m, back.Data!["partsCost"]!.GetValue<decimal>());
        Assert.Equal(9m, back.Data["parts"]!.AsArray()[0]!["warehouseStock"]!.GetValue<decimal>());

        // سطر جديد غير موافق لا يُصرف، والأمر بتكاليف لا يُلغى
        var extra = await api.Post($"/workshop/work-orders/{orderId}/lines", new { kind = "part", productId = part, quantity = 1 });
        Ok(extra, "extra line");
        var extraLine = extra.Data!["lines"]!.AsArray().Last()!["id"].S();
        Assert.Equal(409, (await api.Post($"/workshop/work-orders/{orderId}/parts/issue", new { lineId = extraLine, quantity = 1 })).Status);
        Assert.Equal(409, (await api.Post($"/workshop/work-orders/{orderId}/cancel", new { reason = "x" })).Status);
        // السطر عليه تنفيذ فلا يُحذف
        Assert.Equal(409, (await api.Delete($"/workshop/work-orders/{orderId}/lines/{partLine}")).Status);
    }
}
