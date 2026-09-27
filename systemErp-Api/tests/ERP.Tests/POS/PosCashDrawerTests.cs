using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>الكاشير والدرج: تسعير السلة المسبق، الإيداع/الصرف النقدي، تقارير X/Z، والجرد بالفئات مع قيد العجز/الزيادة.</summary>
[Collection("api")]
public class PosCashDrawerTests : TestBase
{
    public PosCashDrawerTests(ErpFactory f) : base(f) { }

    private static async Task OpenShiftAsync(Client api, decimal opening = 500)
        => Assert.Equal(200, (await api.Post("/pos/shifts/open", new { openingCash = opening, posTerminalName = "POS-1" })).Status);

    private static object Cart(Guid product, decimal qty, decimal paid = 0, string? coupon = null)
        => new { items = new[] { new { itemId = product, quantity = qty } }, paymentMethod = "cash", paidCash = paid, couponCode = coupon };

    private static async Task<decimal> BalanceAsync(Client api, string code) => (await api.Get($"/accounts/by-code/{code}")).Data!["balance"].D();

    [Fact]
    public async Task Quote_prices_the_cart_like_checkout_without_side_effects()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api); // سعر 100 + ضريبة 15%، مخزون 10

        var q = await api.Post("/pos/transactions/quote", Cart(product, 2, coupon: "NOPE"));
        Assert.Equal(200, q.Status);
        Assert.Equal(230, q.Data!["grandTotal"].D());
        Assert.Equal(30, q.Data["vatTotal"].D());
        Assert.Equal(10, q.Data["lines"]!.AsArray()[0]!["stockOnHand"].D());
        Assert.False(string.IsNullOrEmpty(q.Data["couponError"].S())); // كوبون غير صالح: رسالة لا رفض
        Assert.Equal(0, q.Data["couponDiscount"].D());
        Assert.Equal(10, (await api.Get($"/products/{product}")).Data!["currentStock"].D());

        await OpenShiftAsync(api);
        var sale = await api.Post("/pos/transactions/checkout", Cart(product, 2, paid: 230));
        Assert.Equal(201, sale.Status);
        Assert.Equal(q.Data["grandTotal"].D(), sale.Data!["grandTotal"].D());
        Assert.Equal(400, (await api.Post("/pos/transactions/checkout", Cart(product, 1, paid: 115, coupon: "NOPE"))).Status); // الإتمام صارم
    }

    [Fact]
    public async Task Paid_in_and_paid_out_move_the_expected_cash_and_post_entries()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        Assert.Equal(409, (await api.Post("/pos/shifts/cash-movements", new { type = "paid_out", amount = 10, reason = "x" })).Status); // بلا وردية
        await OpenShiftAsync(api);
        Assert.Equal(201, (await api.Post("/pos/transactions/checkout", Cart(product, 2, paid: 230))).Status);

        var pin = await api.Post("/pos/shifts/cash-movements", new { type = "paid_in", amount = 200, reason = "فكّة من البنك" });
        Assert.Equal(200, pin.Status);
        var pout = await api.Post("/pos/shifts/cash-movements", new { type = "paid_out", amount = 50, reason = "ضيافة" });
        Assert.Equal(200, pout.Status);
        Assert.Equal(409, (await api.Post("/pos/shifts/cash-movements", new { type = "paid_out", amount = 100000, reason = "أكبر من الدرج" })).Status);
        Assert.Equal(400, (await api.Post("/pos/shifts/cash-movements", new { type = "paid_out", amount = 5, reason = "" })).Status);

        // الصندوق: بيع 230 + إيداع 200 − صرف 50 ؛ المصروف النثري 50 ؛ البنك دائن 200
        Assert.Equal(380, await BalanceAsync(api, "1111"));
        Assert.Equal(50, await BalanceAsync(api, "521"));

        var x = (await api.Get("/pos/shifts/active/report")).Data!;
        Assert.Equal("X", x["reportType"].S());
        Assert.Equal(880, x["expectedCash"].D()); // 500 + 230 + 200 − 50
        Assert.Equal(1, x["salesCount"].D());
        Assert.Equal(2, x["topItems"]!.AsArray()[0]!["quantity"].D());
        Assert.Equal(2, x["cashMovements"]!.AsArray().Count);

        Assert.Equal(200, (await api.Delete($"/pos/shifts/cash-movements/{pout.Data!["id"].S()}")).Status);
        Assert.Equal(0, await BalanceAsync(api, "521"));
        Assert.Equal(930, (await api.Get("/pos/shifts/active/report")).Data!["expectedCash"].D());
        var (d, c) = Totals(await api.Get("/reports/trial-balance")); Assert.Equal(d, c);
    }

    [Fact]
    public async Task Closing_with_denominations_posts_shortage_and_corrections_resync_the_entry()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        await OpenShiftAsync(api);
        Assert.Equal(201, (await api.Post("/pos/transactions/checkout", Cart(product, 1, paid: 115))).Status); // المتوقع 615

        var denoms = new[] { new { value = 500m, count = 1 }, new { value = 100m, count = 1 }, new { value = 10m, count = 1 } }; // 610
        Assert.Equal(400, (await api.Post("/pos/shifts/close", new { closingCashActual = 999, denominations = denoms })).Status); // لا يطابق الفئات
        var closed = await api.Post("/pos/shifts/close", new { denominations = denoms });
        Assert.Equal(200, closed.Status);
        Assert.Equal(610, closed.Data!["closingCashActual"].D());
        Assert.Equal(-5, closed.Data["cashVariance"].D());
        Assert.Equal(5, await BalanceAsync(api, "522"));   // عجز
        Assert.Equal(110, await BalanceAsync(api, "1111")); // 115 − 5
        var shiftId = closed.Data["id"].S();
        Assert.Equal("Z", (await api.Get($"/pos/shifts/{shiftId}/report")).Data!["reportType"].S());

        // تصحيح الجرد بعد الإغلاق: يُعكس القيد القديم ويُرحَّل الجديد
        Assert.Equal(200, (await api.Put($"/pos/shifts/{shiftId}", new { posTerminalName = "POS-1", openingCash = 500, closingCashActual = 615 })).Status);
        Assert.Equal(0, await BalanceAsync(api, "522"));
        var over = await api.Put($"/pos/shifts/{shiftId}", new { posTerminalName = "POS-1", openingCash = 500, closingCashActual = 620 });
        Assert.Equal(5, over.Data!["cashVariance"].D());
        Assert.Equal(5, Math.Abs(await BalanceAsync(api, "421"))); // زيادة
        Assert.Equal(120, await BalanceAsync(api, "1111"));
        var (d, c) = Totals(await api.Get("/reports/trial-balance")); Assert.Equal(d, c);
    }
}
