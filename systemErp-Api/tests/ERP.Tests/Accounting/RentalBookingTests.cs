using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// تأجير المعدات للعملاء (R1): الفئات والأسعار (المعدة تتقدم على الفئة لكل أساس)، والتقدير (فترات كاملة بالأساس المختار والكسر
/// بأصغر أساس مسعّر، والحد الأدنى، والمشغّل)، والحجز (مسودة لا تحجز، المؤقت والمؤكد يمنعان التداخل، والإلغاء يحرر الفترة)، والتقويم.
/// </summary>
[Collection("api")]
public class RentalBookingTests : TestBase
{
    public RentalBookingTests(ErpFactory f) : base(f) { }

    private static readonly DateTime D = DateTime.UtcNow.AddHours(3).Date.AddDays(10);

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");

    private sealed record Setup(Client Api, string Equipment, string Category, Guid Customer);

    /// <summary>منشأة بالتأجير: فئة «حفارات» بسعر يومي 1000 (مشغّل 300) وساعي 150، ومعدة قابلة للتأجير بسعر أسبوعي خاص 5000.</summary>
    private async Task<Setup> SetupAsync()
    {
        var api = await NewTenantWithModulesAsync("accounting", "equipment_rental");
        var category = await api.Post("/rental/categories", new { nameAr = "حفارات", nameEn = "Excavators", isActive = true });
        Ok(category, "category");
        var categoryId = category.Data!["id"].S();
        var eq = await api.Post("/equipment", new { nameAr = "حفار 320", ownership = "owned", rate = 0, hoursPerDay = 8, daysPerMonth = 26 });
        Ok(eq, "equipment");
        var equipmentId = eq.Data!["id"].S();
        Ok(await api.Put($"/rental/equipment/{equipmentId}", new { isRentable = true, rentalCategoryId = categoryId }), "rentable");

        var from = D.AddMonths(-1);
        Ok(await api.Post("/rental/rates", new { categoryId, basis = "day", rate = 1000, operatorRate = 300, minimumPeriods = 1, effectiveFrom = from }), "day");
        Ok(await api.Post("/rental/rates", new { categoryId, basis = "hour", rate = 150, minimumPeriods = 1, effectiveFrom = from }), "hour");
        Ok(await api.Post("/rental/rates", new { equipmentId, basis = "week", rate = 5000, minimumPeriods = 1, effectiveFrom = from }), "week");
        var (customer, _) = await SeedCustomerAsync(api);
        return new Setup(api, equipmentId, categoryId, customer);
    }

    private static object Line(string equipmentId, DateTime from, DateTime to, string basis = "day", bool withOperator = false)
        => new { equipmentId, from, to, basis, withOperator };

    [Fact]
    public async Task Quote_uses_whole_periods_then_smallest_priced_basis_minimum_and_operator()
    {
        var s = await SetupAsync();
        var api = s.Api;

        // يومان و5 ساعات باليومي: 2×1000 + 5×150
        var q = (await api.Post("/rental/quote", new { equipmentId = s.Equipment, from = D, to = D.AddDays(2).AddHours(5), basis = "day" })).Data!;
        Assert.Equal(2750, q["total"].D());
        Assert.Equal(2, q["parts"]!.AsArray().Count);

        // 9 أيام بالأسبوعي (سعر المعدة الخاص) + يومان من الفئة
        q = (await api.Post("/rental/quote", new { equipmentId = s.Equipment, from = D, to = D.AddDays(9), basis = "week" })).Data!;
        Assert.Equal(7000, q["total"].D());

        // ساعة ونصف = ساعتان (الكسر وحدة كاملة من أصغر أساس)
        q = (await api.Post("/rental/quote", new { equipmentId = s.Equipment, from = D, to = D.AddMinutes(90), basis = "hour" })).Data!;
        Assert.Equal(300, q["total"].D());

        // مع مشغّل: ثلاثة أيام كاملة، والكسر الساعي بلا سعر مشغّل مرفوض
        q = (await api.Post("/rental/quote", new { equipmentId = s.Equipment, from = D, to = D.AddDays(3), basis = "day", withOperator = true })).Data!;
        Assert.Equal(3000, q["rentAmount"].D());
        Assert.Equal(900, q["operatorAmount"].D());
        Assert.Equal(400, (await api.Post("/rental/quote", new { equipmentId = s.Equipment, from = D, to = D.AddDays(1).AddHours(2), basis = "day", withOperator = true })).Status);

        // أساس بلا سعر، وفترة معكوسة
        Assert.Equal(400, (await api.Post("/rental/quote", new { equipmentId = s.Equipment, from = D, to = D.AddDays(40), basis = "month" })).Status);
        Assert.Equal(400, (await api.Post("/rental/quote", new { equipmentId = s.Equipment, from = D, to = D.AddDays(-1), basis = "day" })).Status);

        // الحد الأدنى: سعر شهري للمعدة بحد شهرين يُحتسب شهرين لشهر واحد
        Ok(await api.Post("/rental/rates", new { equipmentId = s.Equipment, basis = "month", rate = 15000, minimumPeriods = 2, effectiveFrom = D.AddMonths(-1) }), "month");
        q = (await api.Post("/rental/quote", new { equipmentId = s.Equipment, from = D, to = D.AddMonths(1), basis = "month" })).Data!;
        Assert.Equal(30000, q["total"].D());
        Assert.True(q["minimumApplied"]!.GetValue<bool>());

        // سعر متداخل لنفس الهدف والأساس مرفوض
        Assert.Equal(400, (await api.Post("/rental/rates", new { equipmentId = s.Equipment, basis = "week", rate = 4000, effectiveFrom = D })).Status);
    }

    [Fact]
    public async Task Held_and_confirmed_bookings_block_overlaps_and_cancel_releases_them()
    {
        var s = await SetupAsync();
        var api = s.Api;

        var first = await api.Post("/rental/bookings", new { customerId = s.Customer, lines = new[] { Line(s.Equipment, D, D.AddDays(3)) } });
        Ok(first, "first");
        Assert.Equal("draft", first.Data!["status"].S());
        Assert.Equal(3000, first.Data["estimatedTotal"].D());
        var firstId = first.Data["id"].S();

        // مسودة متداخلة لا تحجز، فيُؤكَّد الأول
        var second = await api.Post("/rental/bookings", new { customerId = s.Customer, lines = new[] { Line(s.Equipment, D.AddDays(2), D.AddDays(4)) } });
        Ok(second, "second draft");
        var secondId = second.Data!["id"].S();
        Ok(await api.Post($"/rental/bookings/{firstId}/confirm"), "confirm first");

        // المتداخل مرفوض حجزاً مؤقتاً وتأكيداً، والتقدير يعرض التعارض
        Assert.Equal(409, (await api.Post($"/rental/bookings/{secondId}/hold", new { holdUntil = DateTime.UtcNow.AddHours(3).AddDays(1) })).Status);
        Assert.Equal(409, (await api.Post($"/rental/bookings/{secondId}/confirm")).Status);
        var quote = (await api.Post("/rental/quote", new { equipmentId = s.Equipment, from = D.AddDays(2), to = D.AddDays(4), basis = "day" })).Data!;
        Assert.Single(quote["conflicts"]!.AsArray());

        // المتلاصق (يبدأ عند نهاية الأول) مقبول
        var touching = await api.Post("/rental/bookings", new { customerId = s.Customer, lines = new[] { Line(s.Equipment, D.AddDays(3), D.AddDays(5)) } });
        Ok(touching, "touching");
        Ok(await api.Post($"/rental/bookings/{touching.Data!["id"].S()}/hold", new { holdUntil = DateTime.UtcNow.AddHours(3).AddDays(1) }), "hold touching");

        // التقويم يعرض الحجزين
        var calendar = (await api.Get($"/rental/availability?from={D:yyyy-MM-dd}&to={D.AddDays(7):yyyy-MM-dd}")).Data!.AsArray();
        var row = calendar.Single(r => r!["equipment"]!["id"].S() == s.Equipment)!;
        Assert.Equal(2, row["blocks"]!.AsArray().Count(b => b!["kind"].S() == "booking"));

        // المؤكد لا يُعدَّل ولا يُحذف، والإلغاء بسبب يحرر فترته
        Assert.Equal(409, (await api.Put($"/rental/bookings/{firstId}", new { customerId = s.Customer, lines = new[] { Line(s.Equipment, D, D.AddDays(1)) } })).Status);
        Assert.Equal(409, (await api.Delete($"/rental/bookings/{firstId}")).Status);
        Assert.Equal(400, (await api.Post($"/rental/bookings/{firstId}/cancel", new { reason = "" })).Status);
        Ok(await api.Post($"/rental/bookings/{firstId}/cancel", new { reason = "العميل اعتذر" }), "cancel");
        Assert.Equal(409, (await api.Post($"/rental/bookings/{secondId}/confirm")).Status); // ما زال يتداخل مع المؤقت المتلاصق (D+3..D+4)
        Ok(await api.Put($"/rental/bookings/{secondId}", new { customerId = s.Customer, lines = new[] { Line(s.Equipment, D.AddDays(1), D.AddDays(3)) } }), "move second");
        Ok(await api.Post($"/rental/bookings/{secondId}/confirm"), "confirm second");

        // المسودة تُحذف
        var draft = await api.Post("/rental/bookings", new { customerId = s.Customer, lines = new[] { Line(s.Equipment, D.AddDays(20), D.AddDays(21)) } });
        Ok(draft, "draft");
        Ok(await api.Delete($"/rental/bookings/{draft.Data!["id"].S()}"), "delete draft");
    }

    [Fact]
    public async Task Category_lines_quote_only_and_non_rentable_equipment_is_rejected()
    {
        var s = await SetupAsync();
        var api = s.Api;

        var byCategory = await api.Post("/rental/bookings", new
        {
            customerId = s.Customer,
            lines = new object[] { new { categoryId = s.Category, quantity = 2, from = D, to = D.AddDays(1), basis = "day" } },
        });
        Ok(byCategory, "category draft");
        Assert.Equal(2000, byCategory.Data!["estimatedTotal"].D());
        Assert.Equal(400, (await api.Post($"/rental/bookings/{byCategory.Data["id"].S()}/confirm")).Status);

        var other = await api.Post("/equipment", new { nameAr = "مولد", ownership = "owned", rate = 0 });
        Ok(other, "other equipment");
        Assert.Equal(400, (await api.Post("/rental/bookings", new { customerId = s.Customer, lines = new[] { Line(other.Data!["id"].S(), D, D.AddDays(1)) } })).Status);

        // التقويم للمعدات القابلة للتأجير وحدها
        var calendar = (await api.Get($"/rental/availability?from={D:yyyy-MM-dd}&to={D.AddDays(2):yyyy-MM-dd}")).Data!.AsArray();
        Assert.Single(calendar);

        // منشأة بلا إضافة التأجير لا تصل للمسارات
        var plain = await NewTenantAsync();
        Assert.Equal(403, (await plain.Get("/rental/categories")).Status);
    }
}
