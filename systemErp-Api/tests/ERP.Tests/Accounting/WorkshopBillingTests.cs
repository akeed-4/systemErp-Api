using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// ورش السيارات W4: الجودة تتطلب إنجاز الأسطر، والفوترة عبر محرك المبيعات بما صُرف من القطع، وإلغاء الفاتورة من الأمر وحده، والتسليم
/// ينشئ الضمان، والإغلاق، ومطالبة الضمان بأمر ضمان.
/// </summary>
[Collection("api")]
public class WorkshopBillingTests : TestBase
{
    public WorkshopBillingTests(ErpFactory f) : base(f) { }

    private static DateTime Now => DateTime.SpecifyKind(DateTime.UtcNow.AddHours(3), DateTimeKind.Unspecified);

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");

    [Fact]
    public async Task Ready_order_is_invoiced_delivered_with_warranty_and_claimed()
    {
        var api = await NewTenantWithModulesAsync("accounting", "workshop");
        var serviceId = await WorkshopReceptionTests.SeedServiceAsync(api);
        Ok(await api.Put("/workshop/settings", new { defaultLaborRate = 150, reservationHoldDays = 5, defaultWarrantyMonths = 3, defaultWarrantyKm = 5000 }), "settings");
        var part = await SeedProductAsync(api, "FILTER", stock: 10, cost: 40, price: 90);
        var tech = await api.Post("/employee-cost-rates/staff", new { purpose = "technician", nameAr = "فني", basicSalary = 4000, costPerHour = 60, hireDate = Now.Date.AddDays(-7) });
        Ok(tech, "technician");
        var (customer, _) = await SeedCustomerAsync(api);
        var reception = await api.Post("/workshop/receptions", new
        {
            customerId = customer, receivedAt = Now.AddHours(-5),
            vehicles = new[] { new { newVehicle = new { plateNumber = "س ص ع 222", make = "مازدا" }, odometerIn = 20000, damages = Array.Empty<object>() } },
        });
        Ok(reception, "reception");
        var orderId = reception.Data!["orders"]!.AsArray()[0]!["id"].S();
        Ok(await api.Post($"/workshop/work-orders/{orderId}/lines", new { kind = "labor", serviceId, quantity = 1 }), "labor");
        var work = await api.Post($"/workshop/work-orders/{orderId}/lines", new { kind = "part", productId = part, quantity = 4 });
        Ok(work, "part");
        string laborLine = work.Data!["lines"]!.AsArray()[0]!["id"].S(), partLine = work.Data["lines"]!.AsArray()[1]!["id"].S();
        var estimate = await api.Post($"/workshop/work-orders/{orderId}/estimates", new { });
        Ok(await api.Post($"/workshop/work-orders/{orderId}/estimates/{estimate.Data!["id"].S()}/send"), "send");
        Ok(await api.Post($"/workshop/work-orders/{orderId}/estimates/{estimate.Data["id"].S()}/respond", new
        {
            channel = "phone", respondedByName = "العميل",
            decisions = new[] { new { lineId = laborLine, decision = "approved" }, new { lineId = partLine, decision = "approved" } },
        }), "respond");
        Ok(await api.Post($"/workshop/work-orders/{orderId}/status", new { status = "in_progress" }), "in progress");
        Ok(await api.Post($"/workshop/work-orders/{orderId}/time-logs", new
        {
            employeeId = tech.Data!["employeeId"].S(), kind = "work", lineId = laborLine, startedAt = Now.AddHours(-3), endedAt = Now.AddHours(-2),
        }), "hours");
        Ok(await api.Post($"/workshop/work-orders/{orderId}/parts/issue", new { lineId = partLine, quantity = 3 }), "issue 3 of 4");

        // الجودة تتطلب إنجاز الأسطر، والفوترة تتطلب «جاهز»
        Assert.Equal(409, (await api.Post($"/workshop/work-orders/{orderId}/status", new { status = "quality" })).Status);
        Assert.Equal(409, (await api.Post($"/workshop/work-orders/{orderId}/invoice", new { })).Status);
        Ok(await api.Post($"/workshop/work-orders/{orderId}/lines/{laborLine}/execution", new { executionStatus = "done" }), "labor done");
        Ok(await api.Post($"/workshop/work-orders/{orderId}/lines/{partLine}/execution", new { executionStatus = "done" }), "part done");
        Ok(await api.Post($"/workshop/work-orders/{orderId}/status", new { status = "quality" }), "quality");
        Ok(await api.Post($"/workshop/work-orders/{orderId}/status", new { status = "ready" }), "ready");

        // المعاينة: العمالة 225 + القطع بما صُرف 3 × 90 = 270، والضريبة 15%
        var preview = await api.Get($"/workshop/work-orders/{orderId}/billing-preview");
        Ok(preview, "preview");
        Assert.Equal(495m, preview.Data!["net"]!.GetValue<decimal>());
        Assert.Equal(569.25m, preview.Data["total"]!.GetValue<decimal>());
        Assert.Equal(180m, preview.Data["costToRevenue"]!.GetValue<decimal>()); // 60 عمالة + 3 × 40 قطع

        var invoiced = await api.Post($"/workshop/work-orders/{orderId}/invoice", new { });
        Ok(invoiced, "invoice");
        Assert.Equal("invoiced", invoiced.Data!["status"].S());
        Assert.NotNull(invoiced.Data["invoiceNumber"]?.GetValue<string>());
        Assert.Equal(409, (await api.Put($"/workshop/work-orders/{orderId}", new { odometerIn = 20000 })).Status);

        // إلغاء الفاتورة يعيد الأمر جاهزاً، ثم الفوترة من جديد
        Assert.Equal(400, (await api.Post($"/workshop/work-orders/{orderId}/cancel-invoice", new { reason = "" })).Status);
        var reopened = await api.Post($"/workshop/work-orders/{orderId}/cancel-invoice", new { reason = "خطأ في السعر" });
        Ok(reopened, "cancel invoice");
        Assert.Equal("ready", reopened.Data!["status"].S());
        Assert.Null(reopened.Data["invoiceId"]?.GetValue<string>());
        Ok(await api.Post($"/workshop/work-orders/{orderId}/invoice", new { }), "invoice again");

        // التسليم بعدّاد لا يقل عن الاستقبال، وينشئ ضمان القطعة من افتراض الإعداد (العمالة بضمان خدمتها صفر)
        Assert.Equal(400, (await api.Post($"/workshop/work-orders/{orderId}/deliver", new { odometerOut = 19000, deliveredTo = "العميل" })).Status);
        Assert.Equal(400, (await api.Post($"/workshop/work-orders/{orderId}/deliver", new { odometerOut = 20010 })).Status);
        Ok(await api.Post($"/workshop/work-orders/{orderId}/deliver", new { odometerOut = 20010, deliveredTo = "العميل نفسه" }), "deliver");
        var warranties = await api.Get($"/workshop/warranties?workOrderId={orderId}");
        Ok(warranties, "warranties");
        var warranty = Assert.Single(warranties.Data!.AsArray())!;
        Assert.Equal(25010m, warranty["kmLimit"]!.GetValue<decimal>());
        Ok(await api.Post($"/workshop/work-orders/{orderId}/close"), "close");

        // المطالبة: ضمن الحد أمر ضمان جديد، وفوق الحد مرفوض
        var warrantyId = warranty["id"].S();
        Assert.Equal(409, (await api.Post($"/workshop/warranties/{warrantyId}/claim", new { odometerIn = 26000 })).Status);
        var claim = await api.Post($"/workshop/warranties/{warrantyId}/claim", new { odometerIn = 21000, complaint = "صوت من الفلتر" });
        Ok(claim, "claim");
        Assert.True(claim.Data!["isWarrantyJob"]!.GetValue<bool>());
        Assert.Equal(409, (await api.Post($"/workshop/warranties/{warrantyId}/claim", new { odometerIn = 21000 })).Status);

        // W5: التقارير تقرأ ما سبق — الإيراد 495 مقابل تكلفة 180 (عمالة 60 + قطع 3 × 40)
        var range = $"from={Now.Date.AddDays(-1):yyyy-MM-dd}&to={Now.Date:yyyy-MM-dd}";
        var profit = await api.Get($"/workshop/reports/profitability?{range}");
        Ok(profit, "profitability");
        var row = Assert.Single(profit.Data!["rows"]!.AsArray())!;
        Assert.Equal(495m, row["revenue"]!.GetValue<decimal>());
        Assert.Equal(180m, row["totalCost"]!.GetValue<decimal>());
        Assert.Equal(315m, row["profit"]!.GetValue<decimal>());
        var techs = await api.Get($"/workshop/reports/technicians?{range}");
        Ok(techs, "technicians");
        var t = Assert.Single(techs.Data!.AsArray())!;
        Assert.Equal(60m, t["workMinutes"]!.GetValue<decimal>());
        Assert.Equal(1.5m, t["billedHours"]!.GetValue<decimal>());
        Assert.Equal(150m, t["efficiencyPercent"]!.GetValue<decimal>());
        var parts = await api.Get($"/workshop/reports/parts?{range}");
        Ok(parts, "parts");
        var p = Assert.Single(parts.Data!.AsArray())!;
        Assert.Equal(3m, p["billedQuantity"]!.GetValue<decimal>());
        Assert.Equal(270m, p["revenue"]!.GetValue<decimal>());
        var dashboard = await api.Get("/workshop/reports/dashboard");
        Ok(dashboard, "dashboard");
        Assert.Equal(1, dashboard.Data!["openCount"]!.GetValue<int>()); // أمر الضمان
        var history = await api.Get($"/workshop/vehicles/{invoiced.Data["vehicleId"].S()}/history");
        Ok(history, "history");
        var first = history.Data!.AsArray().Single(h => h!["workOrderId"].S() == orderId)!;
        Assert.Equal(2, first["work"]!.AsArray().Count);
        Assert.Single(first["warranties"]!.AsArray());
    }
}
