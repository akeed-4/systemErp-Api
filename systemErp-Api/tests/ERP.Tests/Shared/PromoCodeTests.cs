using ERP.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ERP.Tests;

/// <summary>
/// رموز خصم الاشتراكات: يديرها مسؤول المنصة؛ الخصم من السعر قبل الضريبة؛ شروط الباقة والدورة والصلاحية وحدود الاستخدام؛
/// الاستخدام يُسجَّل عند التفعيل فقط؛ الرمز الذي يجعل الإجمالي صفراً يفعّل الاشتراك بلا دفع.
/// </summary>
[Collection("api")]
public class PromoCodeTests : TestBase
{
    public PromoCodeTests(ErpFactory f) : base(f) { }

    private async Task<Client> PlatformAdminAsync()
    {
        var admin = await NewTenantAsync("شركة مدير المنصة");
        var host = Factory.WithWebHostBuilder(b => b.UseSetting("Platform:AdminEmails:0", admin.Email));
        return await admin.LoginAsAsync(host.CreateClient(), admin.Email, "Passw0rd!");
    }

    private static string NewCode(string prefix) => prefix + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private static async Task<decimal> YearlyPriceAsync(Client api, string plan = "professional")
        => (await api.Get("/subscriptions/plans")).Data!.AsArray().First(p => p!["id"].S() == plan)!["priceYearly"].D();

    [Fact]
    public async Task Percent_code_discounts_before_vat_is_recorded_on_activation_and_is_once_per_tenant()
    {
        var admin = await PlatformAdminAsync();
        var code = NewCode("SAVE");
        var created = await admin.Post("/platform/promo-codes", new { code = code.ToLowerInvariant(), discountType = "percent", value = 20, planIds = new[] { "professional" }, billingCycle = "yearly" });
        Assert.True(created.Status == 200, created.Body?.ToJsonString());
        Assert.Equal(code, created.Data!["code"].S()); // يُحفظ بأحرف كبيرة
        var promoId = created.Data["id"].S();

        var tenant = await NewTenantAsync();
        var price = await YearlyPriceAsync(tenant);
        var quote = await tenant.Post("/subscriptions/quote", new { planId = "professional", billingCycle = "yearly", promoCode = code });
        Assert.Equal(200, quote.Status);
        var discount = Math.Round(price * 0.2m, 2);
        Assert.Equal(discount, quote.Data!["discount"].D());
        Assert.Equal(Math.Round((price - discount) * 1.15m, 2), quote.Data["total"].D());

        // لا ينطبق على الشهري ولا على باقة أخرى
        Assert.Equal(400, (await tenant.Post("/subscriptions/quote", new { planId = "professional", billingCycle = "monthly", promoCode = code })).Status);

        // فتح صفحة الدفع لا يُحتسب استخداماً؛ الإشعار الناجح يفعّل الاشتراك بالسعر بعد الخصم ويسجّل الاستخدام
        Assert.Equal(0, (await admin.Get("/platform/promo-codes")).Data!.AsArray().First(p => p!["id"].S() == promoId)!["redemptions"]!.GetValue<int>());
        var checkout = await tenant.PaySubscriptionAsync(promoCode: code);
        Assert.Equal(quote.Data["total"].D(), checkout.Data!["amount"].D());
        var current = (await tenant.Get("/subscriptions/current")).Data!;
        Assert.Equal(code, current["promoCode"].S());
        Assert.Equal(discount, current["discountAmount"].D());
        Assert.Equal(price - discount, current["price"].D());

        var row = (await admin.Get("/platform/promo-codes")).Data!.AsArray().First(p => p!["id"].S() == promoId)!;
        Assert.Equal(1, row["redemptions"]!.GetValue<int>());
        Assert.Equal(discount, row["totalDiscount"].D());

        var again = await tenant.Post("/subscriptions/quote", new { planId = "professional", billingCycle = "yearly", promoCode = code });
        Assert.Equal(400, again.Status); // مرة لكل منشأة
        Assert.Equal(409, (await admin.Delete($"/platform/promo-codes/{promoId}")).Status); // المستخدم يُعطَّل لا يُحذف
    }

    [Fact]
    public async Task Invalid_codes_are_rejected_with_reasons_and_only_platform_admin_manages_codes()
    {
        var admin = await PlatformAdminAsync();
        var tenant = await NewTenantAsync();
        Assert.Equal(403, (await tenant.Get("/platform/promo-codes")).Status);
        Assert.Equal(403, (await tenant.Post("/platform/promo-codes", new { code = NewCode("X"), discountType = "percent", value = 10 })).Status);

        Assert.Equal(400, (await admin.Post("/platform/promo-codes", new { code = NewCode("P"), discountType = "percent", value = 150 })).Status);
        Assert.Equal(400, (await admin.Post("/platform/promo-codes", new { code = "a b", discountType = "percent", value = 10 })).Status);
        var expired = NewCode("OLD");
        Assert.Equal(200, (await admin.Post("/platform/promo-codes", new { code = expired, discountType = "fixed", value = 50, validUntil = DateTime.UtcNow.Date.AddDays(-2) })).Status);
        Assert.Equal(409, (await admin.Post("/platform/promo-codes", new { code = expired, discountType = "fixed", value = 50 })).Status);

        var unknown = await tenant.Post("/subscriptions/quote", new { planId = "professional", billingCycle = "yearly", promoCode = "NOPE-NOPE" });
        Assert.Equal(400, unknown.Status);
        Assert.Contains("رمز الخصم", unknown.Body!["message"].S());
        var late = await tenant.Post("/subscriptions/quote", new { planId = "professional", billingCycle = "yearly", promoCode = expired });
        Assert.Equal(400, late.Status);
        Assert.Contains("انتهت", late.Body!["message"].S());
        // الدفع نفسه يرفض الرمز غير الصالح (لا يُوثق بالواجهة)
        Assert.Equal(400, (await tenant.Post("/payments/subscription", new { planId = "professional", billingCycle = "yearly", promoCode = expired })).Status);

        // بلا رمز: السعر الكامل
        var plain = (await tenant.Post("/subscriptions/quote", new { planId = "professional", billingCycle = "yearly" })).Data!;
        Assert.Equal(0, plain["discount"].D());
    }

    [Fact]
    public async Task Full_discount_activates_without_payment_and_respects_the_global_limit()
    {
        var admin = await PlatformAdminAsync();
        var code = NewCode("FREE");
        Assert.Equal(200, (await admin.Post("/platform/promo-codes", new { code, discountType = "percent", value = 100, maxRedemptions = 1 })).Status);

        var first = await NewTenantAsync();
        var checkout = await first.Post("/payments/subscription", new { planId = "professional", billingCycle = "monthly", promoCode = code });
        Assert.Equal(200, checkout.Status);
        Assert.Equal("paid", checkout.Data!["status"].S());
        Assert.True(string.IsNullOrEmpty(checkout.Data["checkoutUrl"]?.ToString()));
        var current = (await first.Get("/subscriptions/current")).Data!;
        Assert.Equal("active", current["status"].S());
        Assert.Equal(0, current["totalAmount"].D());
        Assert.Equal("monthly", current["billingCycle"].S());

        var second = await NewTenantAsync();
        var exhausted = await second.Post("/subscriptions/quote", new { planId = "professional", billingCycle = "monthly", promoCode = code });
        Assert.Equal(400, exhausted.Status);
        Assert.Contains("استُنفدت", exhausted.Body!["message"].S());
    }
}
