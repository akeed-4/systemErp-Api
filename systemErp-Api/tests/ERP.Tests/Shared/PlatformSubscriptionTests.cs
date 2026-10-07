using ERP.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ERP.Tests;

/// <summary>إدارة الاشتراكات والباقات على مستوى المنصة: لمدير المنصة فقط، وتعمل عبر المنشآت.</summary>
[Collection("api")]
public class PlatformSubscriptionTests : TestBase
{
    public PlatformSubscriptionTests(ErpFactory f) : base(f) { }

    /// <summary>منشأة يُعامَل بريد مديرها كمدير منصة (الإعداد Platform:AdminEmails) على نفس قاعدة البيانات.</summary>
    private async Task<Client> PlatformAdminAsync()
    {
        var admin = await NewTenantAsync("شركة مدير المنصة");
        var host = Factory.WithWebHostBuilder(b => b.UseSetting("Platform:AdminEmails:0", admin.Email));
        return await admin.LoginAsAsync(host.CreateClient(), admin.Email, "Passw0rd!");
    }

    private static JsonNodeRow Row(Res tenants, Guid tenantId)
        => new(tenants.Data!.AsArray().First(t => t!["tenantId"].G() == tenantId)!);

    private sealed record JsonNodeRow(System.Text.Json.Nodes.JsonNode Node)
    {
        public string Plan => Node["subscription"]!["planType"].S();
        public string Status => Node["effectiveStatus"].S();
    }

    [Fact]
    public async Task Regular_tenant_users_are_forbidden_and_see_no_platform_access()
    {
        var owner = await NewTenantAsync();
        Assert.False((await owner.Get("/platform/access")).Data!["isPlatformAdmin"]!.GetValue<bool>());
        Assert.Equal(403, (await owner.Get("/platform/tenants")).Status);
        Assert.Equal(403, (await owner.Get("/platform/plans")).Status);
        Assert.Equal(403, (await owner.Post($"/platform/tenants/{owner.TenantId}/subscriptions/extend", new { days = 30 })).Status);
    }

    [Fact]
    public async Task Platform_admin_sees_every_tenant_with_its_subscription()
    {
        var admin = await PlatformAdminAsync();
        var other = await NewTenantAsync("شركة أخرى");

        Assert.True((await admin.Get("/platform/access")).Data!["isPlatformAdmin"]!.GetValue<bool>());
        var tenants = await admin.Get("/platform/tenants");
        Assert.Equal(200, tenants.Status);
        var row = Row(tenants, other.TenantId);
        Assert.Equal("professional", row.Plan);
        Assert.Equal("active", row.Status);

        var summary = (await admin.Get("/platform/summary")).Data!;
        Assert.True(summary["totalTenants"]!.GetValue<int>() >= 2);
        Assert.True(summary["active"]!.GetValue<int>() >= 2);
        Assert.True(summary["monthlyRecurringRevenue"].D() > 0);
    }

    [Fact]
    public async Task Changing_the_plan_replaces_the_active_subscription_and_keeps_history()
    {
        var admin = await PlatformAdminAsync();
        var other = await NewTenantAsync("شركة تغيير الباقة");

        var changed = await admin.Post($"/platform/tenants/{other.TenantId}/subscriptions/change-plan",
            new { planId = "starter", billingCycle = "monthly", paymentMethod = "manual" });
        Assert.Equal(200, changed.Status);
        Assert.Equal("starter", changed.Data!["planType"].S());
        Assert.Equal(199m, changed.Data["price"].D());

        // الشركة نفسها ترى الباقة الجديدة؛ وباقة مدير المنصة لم تتأثر
        Assert.Equal("starter", (await other.Get("/subscriptions/current")).Data!["planType"].S());
        Assert.Equal("professional", (await admin.Get("/subscriptions/current")).Data!["planType"].S());

        var history = (await admin.Get($"/platform/tenants/{other.TenantId}/subscriptions")).Data!.AsArray();
        Assert.Equal(2, history.Count);
        Assert.Equal("starter", history[0]!["planType"].S());
        Assert.Equal("expired", history[1]!["status"].S());
    }

    [Fact]
    public async Task Extend_suspend_activate_and_cancel_follow_the_rules()
    {
        var admin = await PlatformAdminAsync();
        var other = await NewTenantAsync("شركة إجراءات");
        var before = DateTime.Parse((await other.Get("/subscriptions/current")).Data!["expiryDate"].S());

        var extended = await admin.Post($"/platform/tenants/{other.TenantId}/subscriptions/extend", new { days = 30 });
        Assert.Equal(200, extended.Status);
        Assert.Equal(before.AddDays(30).Date, DateTime.Parse(extended.Data!["expiryDate"].S()).Date);
        Assert.Equal(400, (await admin.Post($"/platform/tenants/{other.TenantId}/subscriptions/extend", new { days = 0 })).Status);

        var suspended = await admin.Post($"/platform/tenants/{other.TenantId}/subscriptions/status", new { status = "suspended" });
        Assert.Equal("suspended", suspended.Data!["status"].S());
        Assert.Equal("suspended", Row(await admin.Get("/platform/tenants"), other.TenantId).Status);

        Assert.Equal("active", (await admin.Post($"/platform/tenants/{other.TenantId}/subscriptions/status", new { status = "active" })).Data!["status"].S());

        Assert.Equal("cancelled", (await admin.Post($"/platform/tenants/{other.TenantId}/subscriptions/status", new { status = "cancelled" })).Data!["status"].S());
        Assert.Equal(400, (await admin.Post($"/platform/tenants/{other.TenantId}/subscriptions/extend", new { days = 10 })).Status); // ملغى
        Assert.Equal(400, (await admin.Post($"/platform/tenants/{other.TenantId}/subscriptions/status", new { status = "expired" })).Status); // حالة غير مسموحة
    }

    [Fact]
    public async Task Unknown_tenant_is_not_found()
    {
        var admin = await PlatformAdminAsync();
        Assert.Equal(404, (await admin.Get($"/platform/tenants/{Guid.NewGuid()}/subscriptions")).Status);
        Assert.Equal(404, (await admin.Post($"/platform/tenants/{Guid.NewGuid()}/subscriptions/extend", new { days = 5 })).Status);
    }

    [Fact]
    public async Task Editing_a_plan_changes_new_subscriptions_and_a_disabled_plan_is_not_sold()
    {
        var admin = await PlatformAdminAsync();
        var plans = (await admin.Get("/platform/plans")).Data!.AsArray();
        Assert.Equal(3, plans.Count);
        var enterprise = plans.First(p => p!["id"].S() == "enterprise")!;

        // تعديل السعر والحدود يظهر في القائمة العامة
        var updated = await admin.Put("/platform/plans/enterprise", new
        {
            nameAr = enterprise["nameAr"].S(), nameEn = enterprise["nameEn"].S(), priceMonthly = 1234, priceYearly = 12340,
            maxUsers = 50, maxInvoicesPerMonth = (int?)null, branches = (int?)null, zatcaPhase2Enabled = true, isActive = true,
        });
        Assert.Equal(200, updated.Status);
        var pub = (await NewHttpClient().SendAsync(HttpMethod.Get, "/subscriptions/plans", anonymous: true)).Data!.AsArray();
        var pubEnterprise = pub.First(p => p!["id"].S() == "enterprise")!;
        Assert.Equal(1234m, pubEnterprise["priceMonthly"].D());
        Assert.Equal(50, pubEnterprise["maxUsers"]!.GetValue<int>());

        // اشتراك جديد بالسعر الجديد
        var other = await NewTenantAsync("شركة سعر جديد");
        var sub = await admin.Post($"/platform/tenants/{other.TenantId}/subscriptions/change-plan",
            new { planId = "enterprise", billingCycle = "monthly" });
        Assert.Equal(1234m, sub.Data!["price"].D());

        // تعطيل الباقة يخفيها عن العموم ويمنع التسجيل بها، لكن مدير المنصة يستطيع منحها
        Assert.Equal(200, (await admin.Put("/platform/plans/starter", new
        {
            nameAr = "باقة البداية", nameEn = "Starter", priceMonthly = 199, priceYearly = 1990, maxUsers = 2, maxInvoicesPerMonth = 500,
            branches = 1, zatcaPhase2Enabled = false, isActive = false,
        })).Status);
        var pub2 = (await NewHttpClient().SendAsync(HttpMethod.Get, "/subscriptions/plans", anonymous: true)).Data!.AsArray();
        Assert.DoesNotContain(pub2, p => p!["id"].S() == "starter");
        Assert.Equal(200, (await admin.Post($"/platform/tenants/{other.TenantId}/subscriptions/change-plan", new { planId = "starter", billingCycle = "monthly" })).Status);

        // إعادة الوضع الافتراضي حتى لا تتأثر اختبارات أخرى
        await admin.Put("/platform/plans/starter", new
        {
            nameAr = "باقة البداية (Starter)", nameEn = "Starter Plan", priceMonthly = 199, priceYearly = 1990, maxUsers = 2, maxInvoicesPerMonth = 500,
            branches = 1, zatcaPhase2Enabled = false, isActive = true,
        });
        await admin.Put("/platform/plans/enterprise", new
        {
            nameAr = enterprise["nameAr"].S(), nameEn = enterprise["nameEn"].S(), priceMonthly = 999, priceYearly = 9990,
            maxUsers = (int?)null, maxInvoicesPerMonth = (int?)null, branches = (int?)null, zatcaPhase2Enabled = true, isActive = true,
        });
    }

    [Fact]
    public async Task Plan_validation_rejects_bad_input()
    {
        var admin = await PlatformAdminAsync();
        var bad = await admin.Put("/platform/plans/professional", new
        {
            nameAr = "", nameEn = "x", priceMonthly = 10, priceYearly = 100, maxUsers = 5, zatcaPhase2Enabled = true, isActive = true,
        });
        Assert.Equal(400, bad.Status);
        var negative = await admin.Put("/platform/plans/professional", new
        {
            nameAr = "x", nameEn = "x", priceMonthly = -1, priceYearly = 100, zatcaPhase2Enabled = true, isActive = true,
        });
        Assert.Equal(400, negative.Status);
    }

    [Fact]
    public async Task New_subscription_carries_the_plan_modules_and_admin_can_restrict_them()
    {
        var admin = await PlatformAdminAsync();
        var other = await NewTenantAsync("شركة وحدات");

        var current = (await other.Get("/subscriptions/current")).Data!["modules"]!.AsArray().Select(m => m.S()).ToArray();
        Assert.Equal(new[] { "accounting", "car_showroom", "pos" }, current);

        // بيع التجارة العامة فقط
        var only = await admin.Post($"/platform/tenants/{other.TenantId}/subscriptions/modules", new { modules = new[] { "accounting" } });
        Assert.Equal(200, only.Status);
        Assert.Equal(new[] { "accounting" }, only.Data!["modules"]!.AsArray().Select(m => m.S()).ToArray());

        // وحدة المعرض ممنوعة، والمشترك (البيانات الأساسية) يعمل
        Assert.Equal(403, (await other.Get("/vehicles")).Status);
        Assert.Equal(200, (await other.Get("/customers")).Status);

        // بيع معارض السيارات فقط: نقطة البيع (وحدة مستقلة) ممنوعة والمعرض يعمل
        await admin.Post($"/platform/tenants/{other.TenantId}/subscriptions/modules", new { modules = new[] { "car_showroom" } });
        Assert.Equal(200, (await other.Get("/vehicles")).Status);
        Assert.Equal(403, (await other.Get("/pos/transactions")).Status);

        // تحقق المدخلات
        Assert.Equal(400, (await admin.Post($"/platform/tenants/{other.TenantId}/subscriptions/modules", new { modules = Array.Empty<string>() })).Status);
        Assert.Equal(400, (await admin.Post($"/platform/tenants/{other.TenantId}/subscriptions/modules", new { modules = new[] { "crm" } })).Status);
    }

    [Fact]
    public async Task Change_plan_can_grant_a_specific_module_set()
    {
        var admin = await PlatformAdminAsync();
        var other = await NewTenantAsync("شركة معرض فقط");
        var changed = await admin.Post($"/platform/tenants/{other.TenantId}/subscriptions/change-plan",
            new { planId = "professional", billingCycle = "monthly", modules = new[] { "car_showroom" } });
        Assert.Equal(new[] { "car_showroom" }, changed.Data!["modules"]!.AsArray().Select(m => m.S()).ToArray());
    }

    [Fact]
    public async Task Suspended_and_cancelled_block_the_system_but_not_login_or_subscription_screens()
    {
        var admin = await PlatformAdminAsync();
        var other = await NewTenantAsync("شركة موقوفة");
        Assert.Equal(200, (await other.Get("/customers")).Status);

        await admin.Post($"/platform/tenants/{other.TenantId}/subscriptions/status", new { status = "suspended" });
        Assert.Equal(403, (await other.Get("/customers")).Status);
        Assert.Equal(403, (await other.Post("/customers", new { nameAr = "x" })).Status);
        Assert.Equal(200, (await other.Get("/subscriptions/current")).Status); // يرى سبب الإيقاف ويستطيع التجديد
        Assert.Equal("suspended", (await other.Get("/subscriptions/current")).Data!["status"].S());

        await admin.Post($"/platform/tenants/{other.TenantId}/subscriptions/status", new { status = "active" });
        Assert.Equal(200, (await other.Get("/customers")).Status);

        await admin.Post($"/platform/tenants/{other.TenantId}/subscriptions/status", new { status = "cancelled" });
        Assert.Equal(403, (await other.Get("/customers")).Status);
    }

    [Fact]
    public async Task Plan_default_modules_can_be_edited_and_must_be_valid()
    {
        var admin = await PlatformAdminAsync();
        var bad = await admin.Put("/platform/plans/starter", new
        {
            nameAr = "باقة البداية (Starter)", nameEn = "Starter Plan", priceMonthly = 199, priceYearly = 1990, maxUsers = 2, maxInvoicesPerMonth = 500,
            branches = 1, zatcaPhase2Enabled = false, isActive = true, modules = new[] { "nope" },
        });
        Assert.Equal(400, bad.Status);

        var ok = await admin.Put("/platform/plans/starter", new
        {
            nameAr = "باقة البداية (Starter)", nameEn = "Starter Plan", priceMonthly = 199, priceYearly = 1990, maxUsers = 2, maxInvoicesPerMonth = 500,
            branches = 1, zatcaPhase2Enabled = false, isActive = true, modules = new[] { "accounting" },
        });
        Assert.Equal(new[] { "accounting" }, ok.Data!["modules"]!.AsArray().Select(m => m.S()).ToArray());

        var other = await NewTenantAsync("شركة باقة محاسبية");
        var sub = await admin.Post($"/platform/tenants/{other.TenantId}/subscriptions/change-plan", new { planId = "starter", billingCycle = "monthly" });
        Assert.Equal(new[] { "accounting" }, sub.Data!["modules"]!.AsArray().Select(m => m.S()).ToArray());

        await admin.Put("/platform/plans/starter", new
        {
            nameAr = "باقة البداية (Starter)", nameEn = "Starter Plan", priceMonthly = 199, priceYearly = 1990, maxUsers = 2, maxInvoicesPerMonth = 500,
            branches = 1, zatcaPhase2Enabled = false, isActive = true, modules = new[] { "accounting", "car_showroom", "pos" },
        });
    }

    private Client NewHttpClient() => new(NewHttp());
}
