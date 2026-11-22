using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// تأجير المعدات R3a: الفوترة التراكمية للإيجار عبر فترات، ورسوم التأخير المولّدة (تعويض يتطلب تصنيفاً، خارج النطاق) والرسوم اليدوية،
/// وإلغاء آخر فوترة يعيد المفوتر والرسوم، والإغلاق بعد الاستلام والفوترة.
/// </summary>
[Collection("api")]
public class RentalBillingTests : TestBase
{
    public RentalBillingTests(ErpFactory f) : base(f) { }

    private static DateTime Now => DateTime.SpecifyKind(DateTime.UtcNow.AddHours(3), DateTimeKind.Unspecified);

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");

    [Fact]
    public async Task Rent_is_billed_cumulatively_late_fee_needs_classification_and_contract_closes()
    {
        var api = await NewTenantWithModulesAsync("accounting", "equipment_rental");
        var category = await api.Post("/rental/categories", new { nameAr = "رافعات", isActive = true });
        Ok(category, "category");
        var categoryId = category.Data!["id"].S();
        var t0 = Now.Date.AddDays(-5).AddHours(8);
        Ok(await api.Post("/rental/rates", new { categoryId, basis = "day", rate = 1000, minimumPeriods = 1, effectiveFrom = t0.Date.AddMonths(-1) }), "rate");
        var eq = await api.Post("/equipment", new { nameAr = "رافعة شوكية", ownership = "owned", rate = 0 });
        Ok(eq, "equipment");
        var equipmentId = eq.Data!["id"].S();
        Ok(await api.Put($"/rental/equipment/{equipmentId}", new { isRentable = true, rentalCategoryId = categoryId }), "rentable");
        var (customer, _) = await SeedCustomerAsync(api);

        var contract = await api.Post("/rental/contracts", new
        {
            customerId = customer, term = "short", lines = new[] { new { equipmentId, from = t0, to = t0.AddDays(2), basis = "day", withOperator = false } },
        });
        Ok(contract, "contract");
        var contractId = contract.Data!["id"].S();
        var lineId = contract.Data["lines"]!.AsArray()[0]!["id"].S();
        Ok(await api.Post($"/rental/contracts/{contractId}/activate"), "activate");
        Ok(await api.Post("/rental/handovers", new { contractId, kind = "delivery", handoverAt = t0, lines = new[] { new { contractLineId = lineId, meter = 100 } } }), "deliver");

        // الفترة الأولى: يوم واحد = 1000، ولا فوترة مستقبلية لعقد يُفوتر مؤخراً
        Assert.Equal(400, (await api.Post($"/rental/contracts/{contractId}/bill", new { until = Now.AddDays(2) })).Status);
        var first = await api.Post($"/rental/contracts/{contractId}/bill", new { until = t0.AddDays(1) });
        Ok(first, "first billing");
        Assert.Equal(1000m, first.Data!["rentAmount"]!.GetValue<decimal>());
        Assert.Equal(1150m, first.Data["totalAmount"]!.GetValue<decimal>());

        // الاستلام بعد النهاية المخططة بيوم: تأخير بعد ساعتي السماح
        Ok(await api.Post("/rental/handovers", new { contractId, kind = "return", handoverAt = t0.AddDays(3), lines = new[] { new { contractLineId = lineId, meter = 130 } } }), "return");
        var generated = await api.Post($"/rental/contracts/{contractId}/generate-charges");
        Ok(generated, "generate");
        var late = Assert.Single(generated.Data!.AsArray())!;
        Assert.Equal("late", late["kind"].S());
        Assert.Equal(1000m, late["amount"]!.GetValue<decimal>());
        Assert.Equal("pending", late["status"].S());
        Assert.Empty((await api.Post($"/rental/contracts/{contractId}/generate-charges")).Data!.AsArray()); // مرة واحدة لكل مصدر
        Assert.Equal(400, (await api.Post($"/rental/charges/{late["id"].S()}/approve", new { })).Status);   // D11: يتطلب تصنيفاً
        Ok(await api.Post($"/rental/charges/{late["id"].S()}/approve", new { taxTreatment = "out_of_scope" }), "approve late");

        var types = await api.Get("/rental/charge-types");
        var fuelType = types.Data!.AsArray().First(t => t!["kind"].S() == "fuel")!["id"].S();
        var fuel = await api.Post("/rental/charges", new { contractId, lineId, chargeTypeId = fuelType, quantity = 1, unitPrice = 150 });
        Ok(fuel, "fuel charge");
        Ok(await api.Post($"/rental/charges/{fuel.Data!["id"].S()}/approve", new { }), "approve fuel");

        // الفترة الثانية: التراكمي 3 أيام (3000) ناقص المفوتر 1000، والتأخير خارج النطاق بلا ضريبة
        var preview = await api.Get($"/rental/contracts/{contractId}/billing-preview");
        Ok(preview, "preview");
        Assert.Equal(3150m, preview.Data!["net"]!.GetValue<decimal>());
        Assert.Equal(322.5m, preview.Data["vat"]!.GetValue<decimal>());
        var second = await api.Post($"/rental/contracts/{contractId}/bill", new { });
        Ok(second, "second billing");
        Assert.Equal(2000m, second.Data!["rentAmount"]!.GetValue<decimal>());
        Assert.Equal(3472.5m, second.Data["totalAmount"]!.GetValue<decimal>());

        // إلغاء الأولى ممنوع قبل اللاحقة، وإلغاء الثانية يعيد الرسوم معتمدة والمفوتر كما كان
        Assert.Equal(409, (await api.Post($"/rental/billings/{first.Data["id"].S()}/cancel", new { reason = "خطأ" })).Status);
        Ok(await api.Post($"/rental/billings/{second.Data["id"].S()}/cancel", new { reason = "تصحيح" }), "cancel second");
        Assert.Equal(2, (await api.Get($"/rental/charges?contractId={contractId}&status=approved")).Data!.AsArray().Count);
        Assert.Equal(409, (await api.Post($"/rental/contracts/{contractId}/close")).Status); // غير مفوتر حتى الاستلام
        Ok(await api.Post($"/rental/contracts/{contractId}/bill", new { }), "rebill");
        var closed = await api.Post($"/rental/contracts/{contractId}/close");
        Ok(closed, "close");
        Assert.Equal("closed", closed.Data!["status"].S());
    }
}
