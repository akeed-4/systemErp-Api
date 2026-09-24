using ERP.Tests.Infrastructure;

namespace ERP.Tests;

[Collection("api")]
public class PosTests : TestBase
{
    public PosTests(ErpFactory f) : base(f) { }

    private static async Task OpenShiftAsync(Client api, decimal opening = 500)
        => Assert.Equal(200, (await api.Post("/pos/shifts/open", new { openingCash = opening, posTerminalName = "POS-1" })).Status);

    private static object Cash(Guid product, decimal qty, decimal paid, string? coupon = null, Guid? customer = null)
        => new { customerId = customer, couponCode = coupon, items = new[] { new { itemId = product, quantity = qty } }, paymentMethod = "cash", paidCash = paid };

    // ---------- الورديات ----------
    [Fact]
    public async Task Sale_requires_an_open_shift_and_only_one_shift_may_be_open()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        Assert.Equal(409, (await api.Post("/pos/transactions/checkout", Cash(product, 1, 200))).Status);
        Assert.Equal(200, (await api.Post("/pos/shifts/open", new { openingCash = 100, posTerminalName = "T1" })).Status);
        Assert.Equal(409, (await api.Post("/pos/shifts/open", new { openingCash = 100, posTerminalName = "T2" })).Status);
        Assert.Equal(200, (await api.Get("/pos/shifts/active")).Status);
        Assert.NotNull((await api.Get("/pos/shifts/active")).Data);
        Assert.Equal(200, (await api.Post("/pos/shifts/close", new { closingCashActual = 100 })).Status);
        Assert.Null((await api.Get("/pos/shifts/active")).Data);
        Assert.Equal(404, (await api.Post("/pos/shifts/close", new { closingCashActual = 100 })).Status);
    }

    // ---------- التسعير في الخادم ----------
    [Fact]
    public async Task Checkout_prices_from_the_catalog_and_applies_server_side_offers_and_coupons()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        await OpenShiftAsync(api);
        await api.Post("/pos/offers", new { titleAr = "خصم 10%", type = "percentage_discount", discountPercent = 10, targetItemId = product, isActive = true, badgeText = "-10%" });
        var coupon = await api.Post("/pos/coupons", new { code = "save5", titleAr = "خصم 5", discountType = "fixed", discountValue = 5, minCartAmount = 50,
            validFrom = DateTime.UtcNow.AddDays(-1), validTo = DateTime.UtcNow.AddDays(30), isActive = true });
        Assert.Equal("SAVE5", coupon.Data!["code"].S());

        // 2×100 = 200 − عرض 10% (20) = 180 − كوبون 5 = 175 صافي، ضريبة 26.25 → 201.25
        var tx = await api.Post("/pos/transactions/checkout", Cash(product, 2, 300, "save5"));
        Assert.Equal(201, tx.Status);
        Assert.Equal(201.25m, tx.Data!["grandTotal"].D()); Assert.Equal(98.75m, tx.Data["changeAmount"].D());
        Assert.Equal(5, tx.Data["couponDiscount"].D()); Assert.Equal(25, tx.Data["totalDiscount"].D());
        Assert.StartsWith("POS-", tx.Data["invoiceNumber"].S());
        Assert.False(string.IsNullOrEmpty(tx.Data["qrCodeBase64"].S()));
        Assert.Equal(1, (await api.Get("/pos/coupons")).Data!["items"]![0]!["usageCount"].D());
    }

    [Fact]
    public async Task Client_side_prices_are_ignored()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        await OpenShiftAsync(api);
        var tx = await api.Post("/pos/transactions/checkout", new { items = new[] { new { itemId = product, quantity = 1, unitPrice = 1, discount = 99 } }, paymentMethod = "cash", paidCash = 115, grandTotal = 1 });
        Assert.Equal(115, tx.Data!["grandTotal"].D());
    }

    [Fact]
    public async Task Buy_x_get_y_offer_gives_free_units()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 20);
        await OpenShiftAsync(api);
        await api.Post("/pos/offers", new { titleAr = "اشترِ 2 واحصل على 1", type = "buy_x_get_y", buyQuantity = 2, getQuantity = 1, targetItemId = product, isActive = true, badgeText = "2+1" });
        var tx = await api.Post("/pos/transactions/checkout", Cash(product, 3, 1000));
        Assert.Equal(200 * 1.15m, tx.Data!["grandTotal"].D()); // واحدة مجانية من ثلاث
    }

    [Fact]
    public async Task Coupon_rules_are_enforced()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        await OpenShiftAsync(api);
        await api.Post("/pos/coupons", new { code = "OLD", titleAr = "منتهي", discountType = "percent", discountValue = 10, minCartAmount = 0, validFrom = DateTime.UtcNow.AddDays(-10), validTo = DateTime.UtcNow.AddDays(-1), isActive = true });
        await api.Post("/pos/coupons", new { code = "ONCE", titleAr = "مرة", discountType = "percent", discountValue = 10, minCartAmount = 0, usageLimit = 1, validFrom = DateTime.UtcNow.AddDays(-1), validTo = DateTime.UtcNow.AddDays(5), isActive = true });
        Assert.False((await api.Post("/pos/coupons/validate", new { code = "OLD", cartAmount = 100 })).Data!["isValid"]!.GetValue<bool>());
        Assert.False((await api.Post("/pos/coupons/validate", new { code = "NOPE", cartAmount = 100 })).Data!["isValid"]!.GetValue<bool>());
        Assert.Equal(400, (await api.Post("/pos/transactions/checkout", Cash(product, 1, 200, "OLD"))).Status);
        Assert.Equal(201, (await api.Post("/pos/transactions/checkout", Cash(product, 1, 200, "ONCE"))).Status);
        Assert.Equal(400, (await api.Post("/pos/transactions/checkout", Cash(product, 1, 200, "ONCE"))).Status); // استنفد الحد
        Assert.Equal(409, (await api.Post("/pos/coupons", new { code = "ONCE", titleAr = "x", discountType = "fixed", discountValue = 1, validFrom = DateTime.UtcNow, validTo = DateTime.UtcNow.AddDays(1) })).Status);
    }

    // ---------- الدفع ----------
    [Fact]
    public async Task Payment_validation_and_split_payment_change()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        await OpenShiftAsync(api);
        Assert.Equal(400, (await api.Post("/pos/transactions/checkout", Cash(product, 1, 10))).Status); // نقد أقل من الإجمالي
        var split = await api.Post("/pos/transactions/checkout", new { items = new[] { new { itemId = product, quantity = 1 } }, paymentMethod = "split", paidCash = 60, paidCard = 60 });
        Assert.Equal(201, split.Status);
        Assert.Equal(5, split.Data!["changeAmount"].D()); Assert.Equal(55, split.Data["paidCash"].D()); Assert.Equal(60, split.Data["paidCard"].D());
        Assert.Equal(400, (await api.Post("/pos/transactions/checkout", new { items = new[] { new { itemId = product, quantity = 1 } }, paymentMethod = "split", paidCash = 10, paidCard = 200 })).Status); // الباقي من غير النقد
        Assert.Equal(400, (await api.Post("/pos/transactions/checkout", new { items = new[] { new { itemId = product, quantity = 1 } }, paymentMethod = "credit" })).Status); // آجل بلا عميل
    }

    [Fact]
    public async Task Stock_shortage_rolls_back_the_entire_sale()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 2);
        await OpenShiftAsync(api);
        Assert.Equal(409, (await api.Post("/pos/transactions/checkout", Cash(product, 5, 10000))).Status);
        Assert.Equal(0, (await api.Get("/pos/transactions")).Data!["totalCount"].D());
        Assert.Equal(0, (await api.Get("/invoices")).Data!["totalCount"].D());
        Assert.Equal(2, (await api.Get($"/products/{product}")).Data!["currentStock"].D());
        Assert.Equal(0, (await api.Get("/pos/shifts/active")).Data!["totalGross"].D());
    }

    // ---------- التكامل مع المحاسبة والمخزون ----------
    [Fact]
    public async Task Pos_sale_is_a_real_invoice_with_journal_stock_and_balanced_books()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        await OpenShiftAsync(api);
        var tx = await api.Post("/pos/transactions/checkout", Cash(product, 2, 230));
        var invoice = (await api.Get($"/invoices/{tx.Data!["invoiceId"].S()}")).Data!;

        Assert.Equal("pos_transaction", invoice["referenceType"].S()); Assert.Equal(tx.Data["invoiceNumber"].S(), invoice["invoiceNumber"].S());
        Assert.Equal(230, invoice["grandTotal"].D()); Assert.Equal(80, invoice["totalCost"].D());
        Assert.False(string.IsNullOrEmpty(invoice["journalEntryId"].S()));
        Assert.Equal(8, (await api.Get($"/products/{product}")).Data!["currentStock"].D());
        var accounts = (await api.Get("/accounts?pageSize=500")).Data!["items"]!.AsArray();
        decimal Bal(string code) => accounts.First(a => a!["code"].S() == code)!["balance"].D();
        Assert.Equal(230, Bal("1111")); Assert.Equal(200, Bal("411")); Assert.Equal(30, Bal("213")); Assert.Equal(80, Bal("511"));
        var (d, c) = Totals(await api.Get("/reports/trial-balance")); Assert.Equal(d, c);
    }

    // ---------- الولاء ----------
    [Fact]
    public async Task Loyalty_points_are_earned_redeemed_and_revoked_on_return()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 50);
        var (customer, _) = await SeedCustomerAsync(api);
        await OpenShiftAsync(api);

        var first = await api.Post("/pos/transactions/checkout", Cash(product, 2, 230, null, customer)); // 230 → 23 نقطة
        Assert.Equal(23, first.Data!["pointsEarned"].D());
        Assert.Equal(23, (await api.Get($"/pos/loyalty/customer/{customer}")).Data!["pointsBalance"].D());

        Assert.Equal(400, (await api.Post("/pos/transactions/checkout", new { customerId = customer, items = new[] { new { itemId = product, quantity = 1 } }, paymentMethod = "cash", paidCash = 500, loyaltyPointsToRedeem = 1000 })).Status);
        // استبدال 20 نقطة = 2 ريال خصم
        var second = await api.Post("/pos/transactions/checkout", new { customerId = customer, items = new[] { new { itemId = product, quantity = 1 } }, paymentMethod = "cash", paidCash = 500, loyaltyPointsToRedeem = 20 });
        Assert.Equal(2, second.Data!["loyaltyDiscount"].D()); Assert.Equal(20, second.Data["loyaltyPointsRedeemed"].D());
        var loyalty = (await api.Get($"/pos/loyalty/customer/{customer}")).Data!;
        Assert.Equal(23 - 20 + 11, loyalty["pointsBalance"].D()); // 112.7 → 11 نقطة
        Assert.Equal("bronze", loyalty["tier"].S());
    }

    // ---------- المرتجعات ----------
    [Fact]
    public async Task Pos_return_restocks_reverses_the_sale_and_respects_remaining_quantity()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        await OpenShiftAsync(api);
        var tx = await api.Post("/pos/transactions/checkout", Cash(product, 2, 230));
        var id = tx.Data!["id"].S();

        Assert.Equal(400, (await api.Post("/pos/returns", new { originalTransactionId = id, returnReason = "x", refundMethod = "cash", items = new[] { new { itemId = product, returnQuantity = 5 } } })).Status);
        var ret = await api.Post("/pos/returns", new { originalTransactionId = id, returnReason = "عيب", refundMethod = "cash", items = new[] { new { itemId = product, returnQuantity = 1 } } });
        Assert.Equal(200, ret.Status); Assert.StartsWith("RET-", ret.Data!["returnNumber"].S()); Assert.Equal(115, ret.Data["grandTotal"].D());
        Assert.Equal(9, (await api.Get($"/products/{product}")).Data!["currentStock"].D());
        Assert.Equal("completed", (await api.Get($"/pos/transactions/{id}")).Data!["status"].S()); // مرتجع جزئي

        var rest = await api.Post("/pos/returns", new { originalTransactionId = id, returnReason = "الباقي", refundMethod = "cash" }); // الباقي كله
        Assert.Equal(115, rest.Data!["grandTotal"].D());
        Assert.Equal("returned", (await api.Get($"/pos/transactions/{id}")).Data!["status"].S());
        Assert.Equal(409, (await api.Post("/pos/returns", new { originalTransactionId = id, returnReason = "x", refundMethod = "cash" })).Status);
        Assert.Equal(10, (await api.Get($"/products/{product}")).Data!["currentStock"].D());
        var (d, c) = Totals(await api.Get("/reports/trial-balance")); Assert.Equal(d, c);
        Assert.Equal(0, (await api.Get("/accounts/by-code/411")).Data!["balance"].D()); // الإيراد مُلغى بالكامل
    }

    [Fact]
    public async Task Shift_close_computes_cash_variance_from_cash_sales_and_cash_refunds_only()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 50);
        await OpenShiftAsync(api, opening: 500);
        await api.Post("/pos/transactions/checkout", Cash(product, 2, 230));                                                                            // نقد 230
        await api.Post("/pos/transactions/checkout", new { items = new[] { new { itemId = product, quantity = 1 } }, paymentMethod = "card" });          // بطاقة 115
        var tx = await api.Post("/pos/transactions/checkout", Cash(product, 1, 115));                                                                   // نقد 115
        await api.Post("/pos/returns", new { originalTransactionId = tx.Data!["id"].S(), returnReason = "x", refundMethod = "cash" });                  // مرتجع نقدي 115
        await api.Post("/pos/returns", new { originalTransactionId = (await api.Get("/pos/transactions")).Data!["items"]!.AsArray().First(t => t!["paymentMethod"].S() == "card")!["id"].S(), returnReason = "x", refundMethod = "card" }); // مرتجع بطاقة

        var closed = await api.Post("/pos/shifts/close", new { closingCashActual = 700, closingNotes = "إغلاق" });
        Assert.Equal("closed", closed.Data!["status"].S());
        Assert.Equal(345, closed.Data["totalCashSales"].D()); Assert.Equal(115, closed.Data["totalCardSales"].D());
        Assert.Equal(230, closed.Data["totalReturns"].D());
        // المتوقع: 500 + 345 − 115 (مرتجع نقدي فقط) = 730 → الفرق −30
        Assert.Equal(-30, closed.Data["cashVariance"].D());
        Assert.Equal(409, (await api.Post("/pos/transactions/checkout", Cash(product, 1, 115))).Status); // لا بيع بعد الإغلاق
    }

    // ---------- سلال معلّقة وإعدادات ----------
    [Fact]
    public async Task Held_carts_and_invoice_settings()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        var cart = await api.Post("/pos/held-carts", new { customerName = "زبون", items = new[] { new { itemId = product, itemCode = "P1", nameAr = "منتج", quantity = 2, unitPrice = 100, vatRate = 15, discount = 0 } } });
        Assert.Equal(201, cart.Status); Assert.StartsWith("HOLD-", cart.Data!["cartReference"].S());
        Assert.Equal(400, (await api.Post("/pos/held-carts", new { customerName = "x", items = Array.Empty<object>() })).Status);
        Assert.Equal(1, (await api.Get("/pos/held-carts")).Data!["totalCount"].D());
        Assert.Equal(200, (await api.Delete($"/pos/held-carts/{cart.Data["id"].S()}")).Status);

        Assert.Equal("80mm", (await api.Get("/pos/settings")).Data!["paperSize"].S());
        Assert.Equal(400, (await api.Put("/pos/settings", new { defaultInvoiceType = "x", paperSize = "80mm" })).Status);
        var upd = await api.Put("/pos/settings", new { defaultInvoiceType = "standard", paperSize = "58mm", headerTextAr = "أهلاً", showVatBreakdown = true });
        Assert.Equal("58mm", upd.Data!["paperSize"].S());
        // النوع القياسي يتطلب رقماً ضريبياً للمشتري
        await OpenShiftAsync(api);
        Assert.Equal(400, (await api.Post("/pos/transactions/checkout", Cash(product, 1, 200))).Status);
    }
}
