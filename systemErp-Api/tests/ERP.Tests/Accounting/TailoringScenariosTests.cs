using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// الخياطة — سيناريوهات التكامل A–D (المرحلة 6) عبر الواجهة البرمجية:
/// A دورة كاملة بعربون وتسليم واحد وإغلاق؛ B تسليم جزئي باسترداد نسبي وآخر تسليم يسترد الباقي؛ C رفض الجودة وتعديل ثم تسليم وإلغاؤه
/// وإعادته؛ D عربون كامل = تسوية بلا فاتورة، وقماش العميل، واللوحة والتقارير، وعزل المنشآت.
/// النوع بلا حقول ولا خيارات وسعر أساسي 100 وضريبة 15% (صنف خدمي)، والمراحل الافتراضية (الإلزامية: القص، الخياطة، التشطيب، الجودة).
/// </summary>
[Collection("api")]
public class TailoringScenariosTests : TestBase
{
    public TailoringScenariosTests(ErpFactory f) : base(f) { }

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");
    private static decimal M(JsonNode? n) => n!.GetValue<decimal>();

    private sealed record Setup(Client Api, Guid Customer, string TypeId, string ProfileId);

    private async Task<Setup> SetupAsync()
    {
        var api = await NewTenantWithModulesAsync("accounting", "tailoring");
        await api.Post("/productcategories", new { code = "C1", nameAr = "تصنيف" });
        await api.Post("/unitsofmeasure", new { code = "PCS", nameAr = "قطعة", isBaseUnit = true, conversionFactor = 1 });
        var service = await api.Post("/products", new { sku = "THOBE", nameAr = "خدمة تفصيل", category = "C1", unit = "PCS", sellingPrice = 0, vatRate = 15, itemType = "service" });
        Ok(service, "service item");
        Ok(await api.Get("/tailoring/settings"), "seed template and stages");
        var type = await api.Post("/tailoring/garment-types", new
        {
            nameAr = "ثوب اختبار", category = "men", productId = service.Data!["id"].S(), basePrice = 100, defaultFabricMeters = 0, measurementUnit = "cm",
            sortOrder = 1, isActive = true, fields = Array.Empty<object>(), options = Array.Empty<object>(),
        });
        Ok(type, "garment type");
        var typeId = type.Data!["id"].S();
        var (customer, _) = await SeedCustomerAsync(api, name: "عميل خياطة");
        var profile = await api.Post($"/tailoring/customers/{customer}/measurement-profiles", new { garmentTypeId = typeId, wearerName = "أحمد", isActive = true });
        Ok(profile, "measurement profile");
        var profileId = profile.Data!["id"].S();
        Ok(await api.Post($"/tailoring/customers/measurement-profiles/{profileId}/versions", new { values = new Dictionary<string, decimal?>() }), "measurement");
        return new Setup(api, customer, typeId, profileId);
    }

    /// <summary>أمر مؤكد بتشكيلة واحدة بقماش العميل؛ يعيد الأمر بقطعه.</summary>
    private static async Task<JsonNode> ConfirmedOrderAsync(Setup s, int quantity)
    {
        var order = await s.Api.Post("/tailoring/orders", new
        {
            customerId = s.Customer, priority = "normal",
            lines = new[] { new { garmentTypeId = s.TypeId, measurementProfileId = s.ProfileId, quantity, fabricSource = "customer", customerFabricDescription = "قماش ياباني أبيض", customerFabricMeters = 3.5m } },
        });
        Ok(order, "order");
        var confirmed = await s.Api.Post($"/tailoring/orders/{order.Data!["id"].S()}/confirm");
        Ok(confirmed, "confirm");
        return confirmed.Data!;
    }

    private static string[] Pieces(JsonNode order) => order["garments"]!.AsArray().Select(g => g!["id"].S()).ToArray();

    /// <summary>ينقل القطع عبر المراحل الإلزامية حتى الجودة.</summary>
    private static async Task ToQualityAsync(Client api, params string[] garments)
    {
        var stages = (await api.Get("/tailoring/production/board")).Data!["stages"]!.AsArray();
        var path = new[] { "CUT", "SEW", "FINISH", "QC" }.Select(c => stages.First(x => x!["code"].S() == c)!["id"].S()).ToList();
        foreach (var g in garments)
            foreach (var stageId in path)
                Ok(await api.Post($"/tailoring/production/garments/{g}/move", new { stageId }), $"move {g}");
    }

    private static Task<Res> InspectAsync(Client api, bool approved, string? note, params string[] garments)
        => api.Post("/tailoring/quality/inspect", new { garmentIds = garments, approved, note });

    private static Task<Res> DeliverAsync(Client api, string orderId, params string[] garments)
        => api.Post("/tailoring/deliveries", new { orderId, garmentIds = garments, paymentMethod = "cash", receivedBy = "أحمد" });

    private static async Task<string> StatusAsync(Client api, string orderId) => (await api.Get($"/tailoring/orders/{orderId}")).Data!["status"].S();

    [Fact]
    public async Task A_full_cycle_with_advance_single_delivery_and_close()
    {
        var s = await SetupAsync();
        var order = await ConfirmedOrderAsync(s, 2);
        var orderId = order["id"].S();
        Assert.Equal(230m, M(order["grandTotal"]));
        var advanced = await s.Api.Post($"/tailoring/orders/{orderId}/advance", new { amount = 115, paymentMethod = "cash" });
        Ok(advanced, "advance");
        Assert.Equal(100m, M(advanced.Data!["advanceNet"]));

        var pieces = Pieces(order);
        await ToQualityAsync(s.Api, pieces);
        Assert.Equal("quality_inspection", await StatusAsync(s.Api, orderId));
        Ok(await InspectAsync(s.Api, true, null, pieces), "approve");
        Assert.Equal("ready_for_delivery", await StatusAsync(s.Api, orderId));

        var preview = await s.Api.Post("/tailoring/deliveries/preview", new { orderId, garmentIds = pieces });
        Ok(preview, "preview");
        Assert.True(preview.Data!["isFinal"]!.GetValue<bool>());
        var delivery = await DeliverAsync(s.Api, orderId, pieces);
        Ok(delivery, "deliver");
        Assert.Equal(200m, M(delivery.Data!["netAmount"]));
        Assert.Equal(100m, M(delivery.Data["advanceRecovery"]));
        Assert.Equal(115m, M(delivery.Data["totalAmount"]));
        Assert.False(string.IsNullOrEmpty(delivery.Data["invoiceNumber"].S()));
        Assert.Equal("delivered", await StatusAsync(s.Api, orderId));

        // الإلغاء بلا سبب مرفوض، والإغلاق بعد التسليم الكامل
        Assert.Equal(400, (await s.Api.Post($"/tailoring/deliveries/{delivery.Data["id"].S()}/cancel", new { reason = "" })).Status);
        var closed = await s.Api.Post($"/tailoring/orders/{orderId}/close");
        Ok(closed, "close");
        Assert.Equal("closed", closed.Data!["status"].S());
        Assert.Equal(409, (await s.Api.Post($"/tailoring/deliveries/{delivery.Data["id"].S()}/cancel", new { reason = "متأخر" })).Status);
    }

    [Fact]
    public async Task B_partial_deliveries_recover_advance_proportionally_and_final_takes_the_rest()
    {
        var s = await SetupAsync();
        var order = await ConfirmedOrderAsync(s, 3);
        var orderId = order["id"].S();
        Assert.Equal(345m, M(order["grandTotal"]));
        Ok(await s.Api.Post($"/tailoring/orders/{orderId}/advance", new { amount = 69, paymentMethod = "cash" }), "advance"); // صافيه 60
        var pieces = Pieces(order);
        await ToQualityAsync(s.Api, pieces);
        Ok(await InspectAsync(s.Api, true, "مطابقة", pieces), "approve");

        var first = await DeliverAsync(s.Api, orderId, pieces[0]);
        Ok(first, "first delivery");
        Assert.Equal(100m, M(first.Data!["netAmount"]));
        Assert.Equal(20m, M(first.Data["advanceRecovery"]));
        Assert.Equal(92m, M(first.Data["totalAmount"]));
        Assert.False(first.Data["isFinal"]!.GetValue<bool>());
        Assert.Equal("partially_delivered", await StatusAsync(s.Api, orderId));

        var last = await DeliverAsync(s.Api, orderId, pieces[1], pieces[2]);
        Ok(last, "final delivery");
        Assert.Equal(200m, M(last.Data!["netAmount"]));
        Assert.Equal(40m, M(last.Data["advanceRecovery"]));
        Assert.Equal(184m, M(last.Data["totalAmount"]));
        Assert.True(last.Data["isFinal"]!.GetValue<bool>());

        // العربون + فواتير التسليم = إجمالي الأمر، والمسترد = صافي العربون
        var summary = (await s.Api.Get($"/tailoring/orders/{orderId}/delivery")).Data!;
        Assert.Equal(60m, M(summary["advanceRecovered"]));
        Assert.Equal(345m, 69m + M(summary["deliveredTotal"]));
        Assert.Equal("delivered", summary["status"].S());
        Assert.Equal(409, (await s.Api.Post($"/tailoring/orders/{orderId}/advance/cancel", new { reason = "خطأ" })).Status); // استُرد منه
    }

    [Fact]
    public async Task C_quality_rejection_modification_then_delivery_cancel_and_redelivery()
    {
        var s = await SetupAsync();
        var order = await ConfirmedOrderAsync(s, 1);
        var orderId = order["id"].S();
        var piece = Pieces(order)[0];
        await ToQualityAsync(s.Api, piece);

        Assert.Equal(400, (await InspectAsync(s.Api, false, null, piece)).Status); // الرفض يتطلب تعليمات
        Ok(await InspectAsync(s.Api, false, "تضييق الكم", piece), "reject");
        Assert.Equal("modification_required", await StatusAsync(s.Api, orderId));
        Assert.Equal(409, (await InspectAsync(s.Api, true, null, piece)).Status);    // ليست في الجودة
        Assert.Equal(409, (await DeliverAsync(s.Api, orderId, piece)).Status);       // ليست جاهزة

        var mod = Assert.Single((await s.Api.Get($"/tailoring/modifications?openOnly=true&orderId={orderId}")).Data!.AsArray())!;
        Ok(await s.Api.Post($"/tailoring/modifications/{mod["id"].S()}/complete"), "complete modification");
        Assert.Equal("quality_inspection", await StatusAsync(s.Api, orderId));      // يعود للجودة
        Ok(await InspectAsync(s.Api, true, null, piece), "approve");

        var delivery = await DeliverAsync(s.Api, orderId, piece);
        Ok(delivery, "deliver");
        Assert.Equal(115m, M(delivery.Data!["totalAmount"]));
        var cancelled = await s.Api.Post($"/tailoring/deliveries/{delivery.Data["id"].S()}/cancel", new { reason = "خطأ في المستلم" });
        Ok(cancelled, "cancel delivery");
        Assert.Equal("cancelled", cancelled.Data!["status"].S());
        Assert.Equal("ready_for_delivery", await StatusAsync(s.Api, orderId));
        Assert.Equal(409, (await s.Api.Post($"/tailoring/deliveries/{delivery.Data["id"].S()}/cancel", new { reason = "مرة ثانية" })).Status);

        Ok(await DeliverAsync(s.Api, orderId, piece), "redeliver");
        Assert.Equal("delivered", await StatusAsync(s.Api, orderId));
        Assert.Equal(2, (await s.Api.Get($"/tailoring/deliveries?orderId={orderId}")).Data!.AsArray().Count);
    }

    [Fact]
    public async Task D_full_advance_settles_without_invoice_reports_and_tenant_isolation()
    {
        var s = await SetupAsync();
        var order = await ConfirmedOrderAsync(s, 1);
        var orderId = order["id"].S();
        Ok(await s.Api.Post($"/tailoring/orders/{orderId}/advance", new { amount = 115, paymentMethod = "cash" }), "full advance");
        var piece = Pieces(order)[0];
        await ToQualityAsync(s.Api, piece);
        Ok(await InspectAsync(s.Api, true, null, piece), "approve");

        var delivery = await DeliverAsync(s.Api, orderId, piece);
        Ok(delivery, "settlement delivery");
        Assert.True(delivery.Data!["settlementOnly"]!.GetValue<bool>());
        Assert.True(string.IsNullOrEmpty(delivery.Data["invoiceNumber"]?.ToString()));
        Assert.Equal(100m, M(delivery.Data["advanceRecovery"]));
        Assert.Equal(0m, M(delivery.Data["totalAmount"]));

        // اللوحة والتقارير (غير التكلفة) تعمل، وأقمشة العملاء تُظهر العهدة، والمفتاح المجهول 404
        Ok(await s.Api.Get("/tailoring/dashboard"), "dashboard");
        foreach (var key in new[] { "orders", "late", "materials", "customer_fabric", "productivity", "stage_durations", "deliveries", "advances", "garment_sales", "fittings" })
            Ok(await s.Api.Get($"/tailoring/reports/{key}"), $"report {key}");
        var fabric = await s.Api.Get("/tailoring/reports/customer_fabric");
        Assert.Equal("قماش ياباني أبيض", Assert.Single(fabric.Data!["rows"]!.AsArray())!["description"].S());
        Assert.Equal(1m, M((await s.Api.Get("/tailoring/reports/garment_sales")).Data!["totals"]!["pieces"]));
        Assert.Equal(404, (await s.Api.Get("/tailoring/reports/unknown")).Status);

        // منشأة أخرى لا ترى الأمر ولا تسليمه ولا تسلّم قطعه
        var other = await NewTenantWithModulesAsync("accounting", "tailoring");
        Assert.Equal(404, (await other.Get($"/tailoring/orders/{orderId}")).Status);
        Assert.Equal(404, (await other.Get($"/tailoring/deliveries/{delivery.Data["id"].S()}")).Status);
        Assert.Empty((await other.Get("/tailoring/deliveries")).Data!.AsArray());
        Assert.Equal(404, (await other.Post("/tailoring/deliveries", new { orderId, garmentIds = new[] { piece }, paymentMethod = "cash" })).Status);
    }
}
