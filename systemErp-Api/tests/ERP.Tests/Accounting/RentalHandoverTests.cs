using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// محاضر تسليم معدات التأجير واستلامها (R2b): التسليم يُخرج السطر ويجعل المعدة «مؤجَّرة» بعدّادها، والاستلام يعيدها بالتأخر وفرق
/// الوقود وساعات التشغيل، والعدّاد لا يتراجع، والإلغاء يعكس الأثر بترتيبه. والتجديد عقد لاحق تنتقل إليه المعدة الخارجة بمحضر تسليمه.
/// </summary>
[Collection("api")]
public class RentalHandoverTests : TestBase
{
    public RentalHandoverTests(ErpFactory f) : base(f) { }

    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };

    /// <summary>توقيت المنشأة (بلا نوع) كما يرسله المتصفح.</summary>
    private static DateTime Now => DateTime.SpecifyKind(DateTime.UtcNow.AddHours(3), DateTimeKind.Unspecified);
    private static DateTime T0 => Now.Date.AddDays(-3).AddHours(8);

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");

    private sealed record Setup(Client Api, string Excavator, string Loader, Guid Customer);

    private async Task<Setup> SetupAsync()
    {
        var api = await NewTenantWithModulesAsync("accounting", "equipment_rental");
        var category = await api.Post("/rental/categories", new { nameAr = "معدات ثقيلة", isActive = true });
        Ok(category, "category");
        var categoryId = category.Data!["id"].S();
        Ok(await api.Post("/rental/rates", new { categoryId, basis = "day", rate = 1000, minimumPeriods = 1, effectiveFrom = T0.Date.AddMonths(-1) }), "day");
        async Task<string> EquipmentAsync(string name)
        {
            var eq = await api.Post("/equipment", new { nameAr = name, ownership = "owned", rate = 0 });
            Ok(eq, name);
            var id = eq.Data!["id"].S();
            Ok(await api.Put($"/rental/equipment/{id}", new { isRentable = true, rentalCategoryId = categoryId }), "rentable " + name);
            return id;
        }
        var (customer, _) = await SeedCustomerAsync(api);
        return new Setup(api, await EquipmentAsync("حفار"), await EquipmentAsync("شيول"), customer);
    }

    private static object Line(string equipmentId, DateTime from, DateTime to) => new { equipmentId, from, to, basis = "day", withOperator = false };

    private static string LineId(System.Text.Json.Nodes.JsonNode contract, string equipmentId)
        => contract["lines"]!.AsArray().Single(l => l!["equipmentId"].S() == equipmentId)!["id"].S();

    private static async Task<System.Text.Json.Nodes.JsonNode> EquipmentAsync(Client api, string id) => (await api.Get($"/equipment/{id}")).Data!;

    [Fact]
    public async Task Delivery_and_return_move_lines_status_and_meter_and_cancelling_reverses_them_in_order()
    {
        var s = await SetupAsync();
        var api = s.Api;
        var created = await api.Post("/rental/contracts", new
        {
            customerId = s.Customer, term = "short", lines = new[] { Line(s.Excavator, T0, T0.AddDays(5)), Line(s.Loader, T0, T0.AddDays(2)) },
        });
        Ok(created, "contract");
        var contractId = created.Data!["id"].S();
        var excavatorLine = LineId(created.Data, s.Excavator);
        var loaderLine = LineId(created.Data, s.Loader);

        object Deliver(DateTime at, decimal excavatorMeter) => new
        {
            contractId, kind = "delivery", handoverAt = at, counterpartyName = "مندوب العميل", siteLocation = "موقع المشروع",
            lines = new object[]
            {
                new { contractLineId = excavatorLine, meter = excavatorMeter, fuelPercent = 100, condition = "good", accessories = "دلو إضافي" },
                new { contractLineId = loaderLine, meter = 50, fuelPercent = 100 },
            },
        };

        // المسودة لا تُسلَّم، واللحظة المستقبلية مرفوضة
        Assert.Equal(409, (await api.Post("/rental/handovers", Deliver(T0.AddHours(1), 100))).Status);
        Ok(await api.Post($"/rental/contracts/{contractId}/activate"), "activate");
        Assert.Equal(400, (await api.Post("/rental/handovers", Deliver(Now.AddDays(1), 100))).Status);

        var delivery = await api.Post("/rental/handovers", Deliver(T0.AddHours(1), 100));
        Ok(delivery, "deliver");
        var deliveryId = delivery.Data!["id"].S();
        Assert.StartsWith("RHO-", delivery.Data["handoverNumber"].S());
        var excavator = await EquipmentAsync(api, s.Excavator);
        Assert.Equal("rented", excavator["status"].S());
        Assert.Equal(100, excavator["meterHours"].D());
        Assert.Equal(409, (await api.Post("/rental/handovers", Deliver(T0.AddHours(2), 120))).Status); // سُلِّمت من قبل

        var contract = (await api.Get($"/rental/contracts/{contractId}")).Data!;
        var loader = contract["lines"]!.AsArray().Single(l => l!["id"].S() == loaderLine)!;
        Assert.Equal("out", loader["status"].S());
        Assert.True(loader["isOverdue"]!.GetValue<bool>()); // نهايته المخططة أمس

        // الاستلام: العدّاد لا يتراجع؛ التأخر وساعات التشغيل وفرق الوقود في المحضر
        object Return(decimal meter) => new
        {
            contractId, kind = "return", handoverAt = Now.AddHours(-1),
            lines = new object[] { new { contractLineId = loaderLine, meter, fuelPercent = 60, condition = "damaged", conditionNotes = "كسر في المرآة" } },
        };
        Assert.Equal(400, (await api.Post("/rental/handovers", Return(40))).Status);
        var returned = await api.Post("/rental/handovers", Return(58));
        Ok(returned, "return");
        var returnId = returned.Data!["id"].S();
        var rl = returned.Data["lines"]!.AsArray().Single()!;
        Assert.Equal(8, rl["usedHours"].D());
        Assert.Equal(-40, rl["fuelDifference"].D());
        Assert.True(rl["lateHours"].D() > 10); // النهاية أمس 08:00 والاستلام قبل ساعة
        Assert.True(returned.Data["hasDamage"]!.GetValue<bool>());
        Assert.Equal("available", (await EquipmentAsync(api, s.Loader))["status"].S());
        Assert.Equal(58, (await EquipmentAsync(api, s.Loader))["meterHours"].D());

        // سُلِّمت معدات: العقد لا يُلغى
        Assert.Equal(409, (await api.Post($"/rental/contracts/{contractId}/cancel", new { reason = "خطأ" })).Status);

        // الإلغاء بترتيبه: التسليم ليس آخر حركة على الشيول ما دام استلامه قائماً
        Assert.Equal(400, (await api.Post($"/rental/handovers/{returnId}/cancel", new { reason = "" })).Status);
        Assert.Equal(409, (await api.Post($"/rental/handovers/{deliveryId}/cancel", new { reason = "خطأ إدخال" })).Status);
        Ok(await api.Post($"/rental/handovers/{returnId}/cancel", new { reason = "استلام بالخطأ" }), "cancel return");
        var loaderEq = await EquipmentAsync(api, s.Loader);
        Assert.Equal("rented", loaderEq["status"].S());
        Assert.Equal(50, loaderEq["meterHours"].D());
        Assert.Equal(409, (await api.Post($"/rental/handovers/{returnId}/cancel", new { reason = "مرة ثانية" })).Status);

        Ok(await api.Post($"/rental/handovers/{deliveryId}/cancel", new { reason = "خطأ إدخال" }), "cancel delivery");
        excavator = await EquipmentAsync(api, s.Excavator);
        Assert.Equal("available", excavator["status"].S());
        Assert.Equal(0, excavator["meterHours"].D());
        contract = (await api.Get($"/rental/contracts/{contractId}")).Data!;
        Assert.All(contract["lines"]!.AsArray(), l => Assert.Equal("pending", l!["status"].S()));
        Assert.Equal(2, (await api.Get($"/rental/handovers?contractId={contractId}")).Data!.AsArray().Count(h => h!["status"].S() == "cancelled"));
        Ok(await api.Post($"/rental/contracts/{contractId}/cancel", new { reason = "لم يتم" }), "cancel contract");
    }

    [Fact]
    public async Task Renewal_holds_the_next_period_and_its_delivery_carries_the_equipment_from_the_previous_contract()
    {
        var s = await SetupAsync();
        var api = s.Api;
        var created = await api.Post("/rental/contracts", new { customerId = s.Customer, term = "short", lines = new[] { Line(s.Excavator, T0, T0.AddDays(2)) } });
        Ok(created, "contract");
        var parentId = created.Data!["id"].S();
        var parentLine = LineId(created.Data, s.Excavator);
        Ok(await api.Post($"/rental/contracts/{parentId}/activate"), "activate");
        Ok(await api.Post("/rental/handovers", new
        {
            contractId = parentId, kind = "delivery", handoverAt = T0.AddHours(1), lines = new[] { new { contractLineId = parentLine, meter = 10 } },
        }), "deliver");

        // التجديد بسبب، من نهاية السطر حتى النهاية الجديدة، مرة واحدة
        var newEnd = Now.Date.AddDays(5).AddHours(8);
        Assert.Equal(400, (await api.Post($"/rental/contracts/{parentId}/renew", new { newEnd, reason = "" })).Status);
        var renewed = await api.Post($"/rental/contracts/{parentId}/renew", new { newEnd, reason = "استمرار المشروع" });
        Ok(renewed, "renew");
        var renewal = renewed.Data!;
        var renewalId = renewal["id"].S();
        Assert.Equal("draft", renewal["status"].S());
        Assert.Equal(parentId, renewal["parentContractId"].S());
        var renewalLine = renewal["lines"]!.AsArray().Single()!;
        Assert.Equal(T0.AddDays(2), renewalLine["plannedFrom"]!.GetValue<DateTime>());
        Assert.Equal(409, (await api.Post($"/rental/contracts/{parentId}/renew", new { newEnd = newEnd.AddDays(1), reason = "ثانٍ" })).Status);
        var parent = (await api.Get($"/rental/contracts/{parentId}")).Data!;
        Assert.Equal("renewal", parent["changes"]!.AsArray().Single()!["kind"].S());

        // السطر الخارج المتأخر على السابق لا يعارض التجديد، والتجديد يحجز فترته عن غيره
        Ok(await api.Post($"/rental/contracts/{renewalId}/activate"), "activate renewal");
        Assert.Equal(409, (await api.Post("/rental/contracts", new
        {
            customerId = s.Customer, term = "short", lines = new[] { Line(s.Excavator, Now.Date.AddDays(1), Now.Date.AddDays(3)) },
        })).Status);

        // تسليم التجديد ينقل المعدة: يُستلم سطر السابق في اللحظة والعدّاد نفسيهما وتبقى «مؤجَّرة»
        var carry = await api.Post("/rental/handovers", new
        {
            contractId = renewalId, kind = "delivery", handoverAt = Now.AddMinutes(-30),
            lines = new[] { new { contractLineId = renewalLine["id"].S(), meter = 30 } },
        });
        Ok(carry, "carry");
        Assert.Equal(parentLine, carry.Data!["lines"]!.AsArray().Single()!["carriedFromLineId"].S());
        parent = (await api.Get($"/rental/contracts/{parentId}")).Data!;
        var previous = parent["lines"]!.AsArray().Single()!;
        Assert.Equal("returned", previous["status"].S());
        Assert.Equal(30, previous["meterIn"].D());
        var eq = await EquipmentAsync(api, s.Excavator);
        Assert.Equal("rented", eq["status"].S());
        Assert.Equal(30, eq["meterHours"].D());

        // صور المحضر وتوقيعه مرفقات مشتركة على شاشة المحاضر
        var handoverId = carry.Data["id"].S();
        var summary = (await api.Get($"/attachments/rental_handover/{handoverId}/summary")).Data!;
        Assert.Contains("signature", summary["categories"]!.AsArray().Select(c => c.S()));
        Assert.Equal(200, await UploadFileAsync(api, $"/attachments/rental_handover/{handoverId}", "front.png", Png));
        Assert.Equal(1, (await api.Get($"/rental/handovers/{handoverId}")).Data!["attachmentCount"]!.GetValue<int>());
    }
}
