using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// ورش السيارات W1: الإعداد والخدمات فوق الصنف الخدمي، وسجل المركبات (الهيكل فريد)، والاستقبال متعدد المركبات بأمر عمل لكل مركبة،
/// وأمر قائم واحد للمركبة، والعدّاد لا يتراجع، والانتقالات المسموحة بسببها وسجلها، والإلغاء والحذف.
/// </summary>
[Collection("api")]
public class WorkshopReceptionTests : TestBase
{
    public WorkshopReceptionTests(ErpFactory f) : base(f) { }

    private static DateTime Now => DateTime.SpecifyKind(DateTime.UtcNow.AddHours(3), DateTimeKind.Unspecified);

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");

    /// <summary>صنف خدمي وخدمة عمالة بالساعة (ساعة ونصف × 150 = 225).</summary>
    internal static async Task<string> SeedServiceAsync(Client api, string sku = "LAB-1")
    {
        await api.Put("/workshop/settings", new { defaultLaborRate = 150, reservationHoldDays = 5 });
        await api.Post("/productcategories", new { code = "C1", nameAr = "تصنيف" });
        await api.Post("/unitsofmeasure", new { code = "PCS", nameAr = "قطعة", isBaseUnit = true, conversionFactor = 1 });
        var product = await api.Post("/products", new { sku, nameAr = "عمالة " + sku, category = "C1", unit = "PCS", sellingPrice = 0, vatRate = 15, itemType = "service" });
        Assert.Equal(201, product.Status);
        var service = await api.Post("/workshop/services", new { productId = product.Data!["id"].S(), pricingMethod = "hourly", standardHours = 1.5m });
        Ok(service, "service");
        return service.Data!["id"].S();
    }

    [Fact]
    public async Task Settings_services_and_templates_validate_their_rules()
    {
        var api = await NewTenantWithModulesAsync("accounting", "workshop");
        Ok(await api.Put("/workshop/settings", new { defaultLaborRate = 150, reservationHoldDays = 5, defaultWarrantyMonths = 6, defaultWarrantyKm = 10000 }), "settings");
        Assert.Equal(400, (await api.Put("/workshop/settings", new { defaultLaborRate = -1 })).Status);

        var stock = await SeedProductAsync(api, "OIL", stock: 0);
        await api.Post("/productcategories", new { code = "C1", nameAr = "تصنيف" });
        var labour = await api.Post("/products", new { sku = "LAB-1", nameAr = "تغيير زيت", category = "C1", unit = "PCS", sellingPrice = 0, vatRate = 15, itemType = "service" });
        Assert.Equal(201, labour.Status);
        var serviceProduct = labour.Data!["id"].S();

        Assert.Equal(400, (await api.Post("/workshop/services", new { productId = stock, pricingMethod = "hourly", standardHours = 1 })).Status);
        Assert.Equal(400, (await api.Post("/workshop/services", new { productId = serviceProduct, pricingMethod = "fixed" })).Status);
        var hourly = await api.Post("/workshop/services", new { productId = serviceProduct, pricingMethod = "hourly", standardHours = 1.5m });
        Ok(hourly, "hourly service");
        Assert.Equal(225m, hourly.Data!["effectivePrice"]!.GetValue<decimal>());
        Assert.Equal(6, hourly.Data["warrantyMonths"]!.GetValue<int>());
        Assert.StartsWith("SRV-", hourly.Data["code"].S());

        Assert.Equal(400, (await api.Post("/workshop/inspection-templates", new { nameAr = "استقبال", usage = "reception", items = Array.Empty<object>() })).Status);
        var template = await api.Post("/workshop/inspection-templates", new
        {
            nameAr = "فحص الاستقبال", usage = "reception",
            items = new object[]
            {
                new { section = "الخارج", textAr = "الصدام الأمامي", resultType = "condition", photoRequiredOnFail = true, sortOrder = 1 },
                new { section = "المحرك", textAr = "مستوى الزيت", resultType = "text", photoRequiredOnFail = true, sortOrder = 2 },
            },
        });
        Ok(template, "template");
        var items = template.Data!["items"]!.AsArray();
        Assert.True(items[0]!["photoRequiredOnFail"]!.GetValue<bool>());
        Assert.False(items[1]!["photoRequiredOnFail"]!.GetValue<bool>()); // الصورة الإلزامية لبند الحالة فقط

        var stage = await api.Post("/workshop/stages", new { status = "in_progress", nameAr = "سمكرة" });
        Ok(stage, "stage");
        Assert.Equal(400, (await api.Post("/workshop/stages", new { status = "closed", nameAr = "x" })).Status);
    }

    [Fact]
    public async Task Multi_vehicle_reception_opens_one_order_per_vehicle_and_enforces_status_rules()
    {
        var api = await NewTenantWithModulesAsync("accounting", "workshop");
        var (customer, _) = await SeedCustomerAsync(api, name: "شركة نقل");
        var (other, _) = await SeedCustomerAsync(api, name: "عميل آخر");

        var existing = await api.Post("/workshop/vehicles", new { customerId = customer, vin = "jtd-bu4ek0 3j1234567", plateNumber = "أ ب ج 1234", make = "تويوتا", model = "كامري", year = 2020, lastOdometer = 50000 });
        Ok(existing, "vehicle");
        var vehicleId = existing.Data!["id"].S();
        Assert.Equal("JTDBU4EK03J1234567", existing.Data["vin"].S());
        Assert.Equal(409, (await api.Post("/workshop/vehicles", new { customerId = other, vin = "JTDBU4EK03J1234567", make = "تويوتا" })).Status);
        Assert.Equal(400, (await api.Post("/workshop/vehicles", new { customerId = customer, make = "نيسان" })).Status); // بلا هيكل ولا لوحة

        object Vehicle(object? id, object? newVehicle, decimal odometer) => new
        {
            vehicleId = id, newVehicle, odometerIn = odometer, fuelLevelIn = 50, complaint = "صوت في المحرك",
            damages = new[] { new { area = "الباب الأمامي الأيمن", damageType = "scratch", severity = "minor" } },
        };

        // العدّاد لا يقل عن آخر قراءة، ومركبة عميل آخر مرفوضة
        Assert.Equal(400, (await api.Post("/workshop/receptions", new { customerId = customer, vehicles = new[] { Vehicle(vehicleId, null, 49000) } })).Status);
        Assert.Equal(400, (await api.Post("/workshop/receptions", new { customerId = other, vehicles = new[] { Vehicle(vehicleId, null, 51000) } })).Status);

        var reception = await api.Post("/workshop/receptions", new
        {
            customerId = customer, contactName = "السائق", contactPhone = "0500000000",
            vehicles = new[] { Vehicle(vehicleId, null, 51000), Vehicle(null, new { plateNumber = "د هـ و 5678", make = "هيونداي", model = "سوناتا" }, 120000) },
        });
        Ok(reception, "reception");
        Assert.StartsWith("WRC-", reception.Data!["receptionNumber"].S());
        var orders = reception.Data["orders"]!.AsArray();
        Assert.Equal(2, orders.Count);
        Assert.All(orders, o => Assert.StartsWith("WO-", o!["orderNumber"].S()));
        Assert.All(orders, o => Assert.Equal("received", o!["status"].S()));
        Assert.All(orders, o => Assert.Equal("شركة نقل", o!["customerName"].S()));
        var orderId = orders.Single(o => o!["vehicleId"].S() == vehicleId)!["id"].S();

        // أمر قائم واحد للمركبة، والعدّاد ارتفع في السجل
        Assert.Equal(409, (await api.Post("/workshop/receptions", new { customerId = customer, vehicles = new[] { Vehicle(vehicleId, null, 52000) } })).Status);
        Assert.Equal(51000m, (await api.Get($"/workshop/vehicles/{vehicleId}")).Data!["lastOdometer"]!.GetValue<decimal>());

        // الانتقالات: لا قفز، والرجوع بسبب، والتوقف بسبب توقف نشط
        Assert.Equal(409, (await api.Post($"/workshop/work-orders/{orderId}/status", new { status = "in_progress" })).Status);
        Ok(await api.Post($"/workshop/work-orders/{orderId}/status", new { status = "inspection" }), "inspection");
        Assert.Equal(400, (await api.Post($"/workshop/work-orders/{orderId}/status", new { status = "received" })).Status);
        // «معتمد» يتطلب سطراً موافقاً عليه (W2): تقدير مُرسل (ينقل الأمر للانتظار) ورد العميل (ينقله معتمداً)
        Assert.Equal(409, (await api.Post($"/workshop/work-orders/{orderId}/status", new { status = "approved" })).Status);
        var serviceId = await SeedServiceAsync(api);
        var work = await api.Post($"/workshop/work-orders/{orderId}/lines", new { kind = "labor", serviceId, quantity = 1 });
        Ok(work, "line");
        var lineId = work.Data!["lines"]!.AsArray()[0]!["id"].S();
        var estimate = await api.Post($"/workshop/work-orders/{orderId}/estimates", new { });
        Ok(estimate, "estimate");
        Ok(await api.Post($"/workshop/work-orders/{orderId}/estimates/{estimate.Data!["id"].S()}/send"), "send");
        Ok(await api.Post($"/workshop/work-orders/{orderId}/estimates/{estimate.Data["id"].S()}/respond", new
        {
            channel = "phone", respondedByName = "مدير الأسطول", decisions = new[] { new { lineId, decision = "approved" } },
        }), "respond");
        Assert.Equal("approved", (await api.Get($"/workshop/work-orders/{orderId}")).Data!["status"].S());
        Ok(await api.Post($"/workshop/work-orders/{orderId}/status", new { status = "in_progress" }), "in progress");
        Assert.Equal(400, (await api.Post($"/workshop/work-orders/{orderId}/status", new { status = "paused" })).Status);
        var pause = await api.Post("/workshop/pause-reasons", new { nameAr = "بانتظار القطع" });
        Ok(pause, "pause reason");
        var paused = await api.Post($"/workshop/work-orders/{orderId}/status", new { status = "paused", pauseReasonId = pause.Data!["id"].S() });
        Ok(paused, "paused");
        Assert.Equal("بانتظار القطع", paused.Data!["pauseReasonName"].S());
        Assert.Equal(409, (await api.Delete($"/workshop/pause-reasons/{pause.Data["id"].S()}")).Status);

        var detail = await api.Get($"/workshop/work-orders/{orderId}");
        Ok(detail, "detail");
        Assert.Equal(6, detail.Data!["history"]!.AsArray().Count);
        Assert.Single(detail.Data["damages"]!.AsArray());
        Assert.Contains("in_progress", detail.Data["nextStatuses"]!.AsArray().Select(x => x!.S()));

        // الحذف لأمر «مستلم» لم يتحرك؛ الإلغاء بسبب
        Assert.Equal(409, (await api.Delete($"/workshop/work-orders/{orderId}")).Status);
        Assert.Equal(400, (await api.Post($"/workshop/work-orders/{orderId}/cancel", new { reason = "" })).Status);
        var cancelled = await api.Post($"/workshop/work-orders/{orderId}/cancel", new { reason = "العميل سحب المركبة" });
        Ok(cancelled, "cancel");
        Assert.Equal("cancelled", cancelled.Data!["status"].S());
        Assert.Equal(409, (await api.Put($"/workshop/work-orders/{orderId}", new { odometerIn = 51000 })).Status);

        var second = orders.Single(o => o!["vehicleId"].S() != vehicleId)!["id"].S();
        Ok(await api.Delete($"/workshop/work-orders/{second}"), "delete untouched");
        Assert.Single((await api.Get($"/workshop/work-orders?receptionId={reception.Data["id"].S()}")).Data!.AsArray());

        // بعد الإلغاء تُستقبل المركبة من جديد، وتاريخها يحفظ أوامرها
        Ok(await api.Post("/workshop/receptions", new { customerId = customer, vehicles = new[] { Vehicle(vehicleId, null, 51500) } }), "new visit");
        Assert.Equal(2, (await api.Get($"/workshop/vehicles/{vehicleId}/history")).Data!.AsArray().Count);
        Assert.Equal(409, (await api.Delete($"/workshop/vehicles/{vehicleId}")).Status);
    }
}
