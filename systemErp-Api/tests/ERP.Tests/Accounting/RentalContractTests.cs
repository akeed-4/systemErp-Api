using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// عقود تأجير المعدات للعملاء (R2): عقد متعدد المعدات بلقطة أسعاره وافتراضات فوترة مدته، والمسودة تحجز فتراتها، والاعتماد والتمديد
/// بسبب وفحص الفترة الإضافية، والإلغاء يحرر الفترات، وتحويل الحجز لعقد (وحذف المسودة يعيده مؤكداً).
/// </summary>
[Collection("api")]
public class RentalContractTests : TestBase
{
    public RentalContractTests(ErpFactory f) : base(f) { }

    private static readonly DateTime D = DateTime.UtcNow.AddHours(3).Date.AddDays(10);

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");

    private sealed record Setup(Client Api, string Excavator, string Loader, Guid Customer);

    /// <summary>منشأة بالتأجير: فئة بسعر يومي 1000 وشهري 20000، ومعدتان قابلتان للتأجير فيها.</summary>
    private async Task<Setup> SetupAsync()
    {
        var api = await NewTenantWithModulesAsync("accounting", "equipment_rental");
        var category = await api.Post("/rental/categories", new { nameAr = "معدات ثقيلة", isActive = true });
        Ok(category, "category");
        var categoryId = category.Data!["id"].S();
        Ok(await api.Post("/rental/rates", new { categoryId, basis = "day", rate = 1000, operatorRate = 300, minimumPeriods = 1, effectiveFrom = D.AddMonths(-1) }), "day");
        Ok(await api.Post("/rental/rates", new { categoryId, basis = "month", rate = 20000, minimumPeriods = 1, effectiveFrom = D.AddMonths(-1) }), "month");
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

    private static object Line(string equipmentId, DateTime from, DateTime to, string basis = "day", decimal? rate = null)
        => new { equipmentId, from, to, basis, withOperator = false, rate };

    [Fact]
    public async Task Multi_equipment_contract_snapshots_prices_blocks_periods_and_extends_with_a_reason()
    {
        var s = await SetupAsync();
        var api = s.Api;

        var created = await api.Post("/rental/contracts", new
        {
            customerId = s.Customer, term = "short", depositRequired = 2000,
            lines = new[] { Line(s.Excavator, D, D.AddDays(3)), Line(s.Loader, D, D.AddDays(2), rate: 900) },
        });
        Ok(created, "create");
        var c = created.Data!;
        var id = c["id"].S();
        Assert.Equal("draft", c["status"].S());
        Assert.Equal("end", c["billingCycle"].S());
        Assert.False(c["billInAdvance"]!.GetValue<bool>());
        Assert.Equal(3000 + 1800, c["estimatedTotal"].D());
        Assert.True(c["lines"]!.AsArray().Single(l => l!["equipmentId"].S() == s.Loader)!["rateOverridden"]!.GetValue<bool>());

        // المسودة تحجز: حجز أو عقد آخر متداخل مرفوض
        var booking = await api.Post("/rental/bookings", new { customerId = s.Customer, lines = new[] { new { equipmentId = s.Excavator, from = D.AddDays(1), to = D.AddDays(4), basis = "day" } } });
        Ok(booking, "booking draft");
        Assert.Equal(409, (await api.Post($"/rental/bookings/{booking.Data!["id"].S()}/confirm")).Status);
        Assert.Equal(409, (await api.Post("/rental/contracts", new { customerId = s.Customer, term = "short", lines = new[] { Line(s.Loader, D.AddDays(1), D.AddDays(5)) } })).Status);

        // العقد الطويل: شهري ومقدماً افتراضياً
        var longTerm = (await api.Post("/rental/contracts", new { customerId = s.Customer, term = "long", lines = new[] { Line(s.Loader, D.AddMonths(2), D.AddMonths(5), "month") } })).Data!;
        Assert.Equal("monthly", longTerm["billingCycle"].S());
        Assert.True(longTerm["billInAdvance"]!.GetValue<bool>());
        Assert.Equal(60000, longTerm["estimatedTotal"].D());

        // الاعتماد ثم التمديد: السبب إلزامي، والفترة الإضافية تُفحص
        Ok(await api.Post($"/rental/contracts/{id}/activate"), "activate");
        Assert.Equal(400, (await api.Post($"/rental/contracts/{id}/extend", new { newEnd = D.AddDays(5), reason = "" })).Status);
        var other = await api.Post("/rental/contracts", new { customerId = s.Customer, term = "short", lines = new[] { Line(s.Excavator, D.AddDays(4), D.AddDays(6)) } });
        Ok(other, "adjacent contract");
        var excavatorLine = c["lines"]!.AsArray().Single(l => l!["equipmentId"].S() == s.Excavator)!["id"].S();
        Assert.Equal(409, (await api.Post($"/rental/contracts/{id}/extend", new { newEnd = D.AddDays(5), lineIds = new[] { excavatorLine }, reason = "طلب العميل" })).Status);

        var loaderLine = c["lines"]!.AsArray().Single(l => l!["equipmentId"].S() == s.Loader)!["id"].S();
        var extended = await api.Post($"/rental/contracts/{id}/extend", new { newEnd = D.AddDays(4), lineIds = new[] { loaderLine }, reason = "طلب العميل" });
        Ok(extended, "extend loader");
        var loader = extended.Data!["lines"]!.AsArray().Single(l => l!["id"].S() == loaderLine)!;
        Assert.Equal(3600, loader["estimatedAmount"].D()); // 4 أيام بالسعر اليدوي 900
        Assert.Single(extended.Data["changes"]!.AsArray());

        // الساري لا يُعدَّل ولا يُحذف؛ يُلغى بسبب قبل أي تسليم فيحرر فتراته
        Assert.Equal(409, (await api.Put($"/rental/contracts/{id}", new { customerId = s.Customer, term = "short", lines = new[] { Line(s.Excavator, D, D.AddDays(1)) } })).Status);
        Assert.Equal(409, (await api.Delete($"/rental/contracts/{id}")).Status);
        Ok(await api.Post($"/rental/contracts/{id}/cancel", new { reason = "إلغاء المشروع" }), "cancel");
        Ok(await api.Post($"/rental/bookings/{booking.Data!["id"].S()}/hold", new { holdUntil = DateTime.UtcNow.AddHours(3).AddDays(1) }), "booking after cancel");
    }

    [Fact]
    public async Task Converting_a_booking_keeps_its_prices_and_deleting_the_draft_restores_the_booking()
    {
        var s = await SetupAsync();
        var api = s.Api;
        var booking = await api.Post("/rental/bookings", new { customerId = s.Customer, lines = new[] { new { equipmentId = s.Excavator, from = D, to = D.AddDays(2), basis = "day" } } });
        Ok(booking, "booking");
        var bookingId = booking.Data!["id"].S();

        // المسودة لا تُحوَّل
        Assert.Equal(409, (await api.Post($"/rental/bookings/{bookingId}/convert?term=short")).Status);
        Ok(await api.Post($"/rental/bookings/{bookingId}/confirm"), "confirm");

        // تغيّر سعر القائمة بعد الحجز لا يمس سعر التحويل
        var rates = (await api.Get("/rental/rates")).Data!.AsArray();
        var day = rates.Single(r => r!["basis"].S() == "day")!;
        Ok(await api.Put($"/rental/rates/{day["id"].S()}", new { categoryId = day["categoryId"].S(), basis = "day", rate = 1500, operatorRate = 300, minimumPeriods = 1, effectiveFrom = D.AddMonths(-1) }), "new price");

        var converted = await api.Post($"/rental/bookings/{bookingId}/convert?term=short");
        Ok(converted, "convert");
        var contractId = converted.Data!["id"].S();
        Assert.Equal(2000, converted.Data["estimatedTotal"].D());
        Assert.Equal(bookingId, converted.Data["bookingId"].S());
        Assert.Equal("converted", (await api.Get($"/rental/bookings/{bookingId}")).Data!["status"].S());

        // العقد يحجز الفترة بدل الحجز
        var other = await api.Post("/rental/bookings", new { customerId = s.Customer, lines = new[] { new { equipmentId = s.Excavator, from = D.AddDays(1), to = D.AddDays(3), basis = "day" } } });
        Ok(other, "other");
        Assert.Equal(409, (await api.Post($"/rental/bookings/{other.Data!["id"].S()}/confirm")).Status);

        // حذف المسودة يعيد الحجز مؤكداً فيبقى حاجزاً
        Ok(await api.Delete($"/rental/contracts/{contractId}"), "delete draft");
        Assert.Equal("confirmed", (await api.Get($"/rental/bookings/{bookingId}")).Data!["status"].S());
        Assert.Equal(409, (await api.Post($"/rental/bookings/{other.Data!["id"].S()}/confirm")).Status);
    }
}
