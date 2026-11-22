using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// ورش السيارات W2: الفحص من القالب (الإكمال يتطلب الإجابات وصورة الفاشل)، والأسطر بأسعارها، والتقدير المجمّد وموافقة العميل الجزئية، ومنع
/// تنفيذ غير الموافق إلا باستثناء موثق، وسطر بعد الاعتماد بنسخة تقدير لاحقة.
/// </summary>
[Collection("api")]
public class WorkshopWorkTests : TestBase
{
    public WorkshopWorkTests(ErpFactory f) : base(f) { }

    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");

    private async Task<(Client Api, string OrderId, string ServiceId)> SetupAsync()
    {
        var api = await NewTenantWithModulesAsync("accounting", "workshop");
        var serviceId = await WorkshopReceptionTests.SeedServiceAsync(api);
        var (customer, _) = await SeedCustomerAsync(api);
        var reception = await api.Post("/workshop/receptions", new
        {
            customerId = customer, vehicles = new[] { new { newVehicle = new { plateNumber = "ر س ت 999", make = "كيا" }, odometerIn = 30000, damages = Array.Empty<object>() } },
        });
        Ok(reception, "reception");
        return (api, reception.Data!["orders"]!.AsArray()[0]!["id"].S(), serviceId);
    }

    [Fact]
    public async Task Inspection_requires_answers_and_photo_for_failed_items_then_locks()
    {
        var (api, orderId, _) = await SetupAsync();
        var template = await api.Post("/workshop/inspection-templates", new
        {
            nameAr = "فحص الاستقبال", usage = "reception",
            items = new object[]
            {
                new { textAr = "الإطارات", resultType = "condition", photoRequiredOnFail = true, sortOrder = 1 },
                new { textAr = "ضغط الإطارات", resultType = "number", sortOrder = 2 },
            },
        });
        Ok(template, "template");
        var inspection = await api.Post($"/workshop/work-orders/{orderId}/inspections", new { templateId = template.Data!["id"].S() });
        Ok(inspection, "inspection");
        var id = inspection.Data!["id"].S();
        var tyres = inspection.Data["results"]!.AsArray()[0]!["id"].S();
        var pressure = inspection.Data["results"]!.AsArray()[1]!["id"].S();

        Assert.Equal(400, (await api.Post($"/workshop/work-orders/{orderId}/inspections/{id}/complete")).Status); // بلا إجابة
        Ok(await api.Put($"/workshop/work-orders/{orderId}/inspections/{id}", new
        {
            results = new object[] { new { resultId = tyres, condition = "fail", note = "تآكل" }, new { resultId = pressure, numberValue = 32 } },
        }), "answers");
        Assert.Equal(400, (await api.Post($"/workshop/work-orders/{orderId}/inspections/{id}/complete")).Status); // بلا صورة للفاشل
        Assert.Equal(200, await UploadFileAsync(api, $"/attachments/work_order_inspection_result/{tyres}", "tyre.png", Png));
        var done = await api.Post($"/workshop/work-orders/{orderId}/inspections/{id}/complete");
        Ok(done, "complete");
        Assert.Equal("completed", done.Data!["status"].S());
        Assert.Equal(1, done.Data["failCount"]!.GetValue<int>());

        // السطر المقترح من البند الفاشل، والمكتمل مقفل
        var product = await SeedProductAsync(api, "TYRE", stock: 4, cost: 200, price: 350);
        var suggested = await api.Post($"/workshop/work-orders/{orderId}/lines", new { kind = "part", productId = product, quantity = 4, sourceResultId = tyres });
        Ok(suggested, "suggested line");
        Assert.Equal(1400m, suggested.Data!["lines"]!.AsArray()[0]!["netAmount"]!.GetValue<decimal>());
        Assert.Equal(409, (await api.Put($"/workshop/work-orders/{orderId}/inspections/{id}", new { results = Array.Empty<object>() })).Status);
        Assert.Equal(409, (await api.Delete($"/workshop/work-orders/{orderId}/inspections/{id}")).Status);
    }

    [Fact]
    public async Task Partial_approval_blocks_unapproved_execution_unless_documented_exception()
    {
        var (api, orderId, serviceId) = await SetupAsync();
        // صنف الخارجي خدمي، والقطعة مخزنية
        Assert.Equal(400, (await api.Post($"/workshop/work-orders/{orderId}/lines", new { kind = "labor", quantity = 1 })).Status);
        Ok(await api.Post($"/workshop/work-orders/{orderId}/lines", new { kind = "labor", serviceId, quantity = 1 }), "labor");
        var work = await api.Post($"/workshop/work-orders/{orderId}/lines", new { kind = "labor", serviceId, quantity = 2, description = "موازنة إضافية" });
        Ok(work, "second labor");
        var lines = work.Data!["lines"]!.AsArray();
        string approvedLine = lines[0]!["id"].S(), rejectedLine = lines[1]!["id"].S();
        Assert.Equal(225m, lines[0]!["netAmount"]!.GetValue<decimal>());
        Assert.Equal(33.75m, lines[0]!["vatAmount"]!.GetValue<decimal>());

        var v1 = await api.Post($"/workshop/work-orders/{orderId}/estimates", new { });
        Ok(v1, "estimate v1");
        var v1Id = v1.Data!["id"].S();
        Assert.Equal(776.25m, v1.Data["total"]!.GetValue<decimal>()); // (225 + 450) × 1.15
        Ok(await api.Post($"/workshop/work-orders/{orderId}/estimates/{v1Id}/send"), "send");
        Assert.Equal("awaiting_approval", (await api.Get($"/workshop/work-orders/{orderId}")).Data!["status"].S());
        // المعروض في تقدير مُرسل لا يُعدَّل، والرد يغطي كل الأسطر
        Assert.Equal(409, (await api.Put($"/workshop/work-orders/{orderId}/lines/{rejectedLine}", new { kind = "labor", serviceId, quantity = 1 })).Status);
        Assert.Equal(400, (await api.Post($"/workshop/work-orders/{orderId}/estimates/{v1Id}/respond", new
        {
            channel = "whatsapp", respondedByName = "العميل", decisions = new[] { new { lineId = approvedLine, decision = "approved" } },
        })).Status);
        var responded = await api.Post($"/workshop/work-orders/{orderId}/estimates/{v1Id}/respond", new
        {
            channel = "whatsapp", respondedByName = "العميل",
            decisions = new[] { new { lineId = approvedLine, decision = "approved" }, new { lineId = rejectedLine, decision = "rejected" } },
        });
        Ok(responded, "respond");
        Assert.Equal("responded", responded.Data!["status"].S());
        Assert.Equal("approved", (await api.Get($"/workshop/work-orders/{orderId}")).Data!["status"].S());

        var board = await api.Get($"/workshop/work-orders/{orderId}/work");
        Ok(board, "board");
        Assert.Equal(258.75m, board.Data!["totals"]!["approvedTotal"]!.GetValue<decimal>());
        Assert.Equal(409, (await api.Put($"/workshop/work-orders/{orderId}/lines/{approvedLine}", new { kind = "labor", serviceId, quantity = 3 })).Status);

        // التنفيذ: الموافق يُنفَّذ، والمرفوض يُرفض حتى استثناء موثق
        Ok(await api.Post($"/workshop/work-orders/{orderId}/lines/{approvedLine}/execution", new { executionStatus = "in_progress" }), "execute approved");
        Assert.Equal(409, (await api.Post($"/workshop/work-orders/{orderId}/lines/{rejectedLine}/execution", new { executionStatus = "in_progress" })).Status);
        Assert.Equal(400, (await api.Post($"/workshop/work-orders/{orderId}/lines/{rejectedLine}/exception", new { reason = "" })).Status);
        var excepted = await api.Post($"/workshop/work-orders/{orderId}/lines/{rejectedLine}/exception", new { reason = "موافقة شفهية من مدير الأسطول بعد الرفض" });
        Ok(excepted, "exception");
        Assert.True(excepted.Data!["lines"]!.AsArray().Single(l => l!["id"].S() == rejectedLine)!["canExecute"]!.GetValue<bool>());
        Ok(await api.Post($"/workshop/work-orders/{orderId}/lines/{rejectedLine}/execution", new { executionStatus = "in_progress" }), "execute excepted");
        Assert.Equal(409, (await api.Post($"/workshop/work-orders/{orderId}/lines/{rejectedLine}/exception", new { reason = "مرة ثانية" })).Status);

        // سطر بعد الاعتماد يحتاج نسخة تقدير لاحقة تعرضه وحده
        var later = await api.Post($"/workshop/work-orders/{orderId}/lines", new { kind = "labor", serviceId, quantity = 1, description = "تبديل فلتر" });
        Ok(later, "later line");
        Assert.True(later.Data!["lines"]!.AsArray().Last()!["addedAfterApproval"]!.GetValue<bool>());
        var v2 = await api.Post($"/workshop/work-orders/{orderId}/estimates", new { });
        Ok(v2, "estimate v2");
        Assert.Equal(2, v2.Data!["version"]!.GetValue<int>());
        Assert.Single(v2.Data["lines"]!.AsArray());
        Assert.EndsWith("-E2", v2.Data["reference"].S());
    }
}
