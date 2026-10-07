using Microsoft.Data.SqlClient;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// اشتراك المنشأة الجديدة: شهر تجريبي مجاني عند التسجيل، ثم يُحجب النظام حتى سداد الاشتراك —
/// بإشعار Paymob الموقَّع فقط (نفس المسار بمفاتيح الاختبار أو المفاتيح الحية).
/// </summary>
[Collection("api")]
public class SignupPaymentTests : TestBase
{
    public SignupPaymentTests(ErpFactory f) : base(f) { }

    private Task<Client> NewTrialTenantAsync() => new Client(NewHttp()).RegisterAsync(pay: false);

    /// <summary>يُنهي الفترة التجريبية للمنشأة (كأن الشهر انقضى).</summary>
    private async Task EndTrialAsync(Client api)
    {
        await using var conn = new SqlConnection(Factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Subscription SET StartDate = DATEADD(day, -31, StartDate), ExpiryDate = DATEADD(day, -1, SYSUTCDATETIME()) WHERE TenantId = @t AND Status = 'Trial'";
        cmd.Parameters.AddWithValue("@t", api.TenantId);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task New_company_starts_a_one_month_free_trial_on_the_chosen_plan()
    {
        var api = await NewTrialTenantAsync();
        Assert.Null(api.Registration!.Body!["payment"]); // لا دفع عند التسجيل

        var trial = (await api.Get("/subscriptions/current")).Data!;
        Assert.Equal("trial", trial["status"].S());
        Assert.Equal("professional", trial["planType"].S());
        var days = (DateTime.Parse(trial["expiryDate"].S()) - DateTime.Parse(trial["startDate"].S())).TotalDays;
        Assert.InRange(days, 28, 31);
        Assert.InRange(trial["daysRemaining"]!.GetValue<int>(), 28, 31);
        Assert.False(trial["trialEnded"]!.GetValue<bool>());

        // النظام يعمل كاملاً خلال الفترة التجريبية
        Assert.Equal(200, (await api.Get("/customers")).Status);
        Assert.Equal(201, (await api.Post("/productcategories", new { code = "C1", nameAr = "تصنيف" })).Status);
    }

    [Fact]
    public async Task After_the_trial_ends_the_system_is_blocked_until_the_subscription_is_paid()
    {
        var api = await NewTrialTenantAsync();
        await EndTrialAsync(api);

        // محجوب كلياً (حتى القراءة) عدا الدخول والاشتراك والدفع
        api.Language = "en";
        var blocked = await api.Get("/customers");
        Assert.Equal(403, blocked.Status);
        Assert.Equal("The free trial has ended. Pay the subscription to continue using the system.", blocked.Body!["message"].S());
        api.Language = null;
        Assert.Equal(403, (await api.Post("/productcategories", new { code = "C1", nameAr = "تصنيف" })).Status);
        Assert.Equal(200, (await api.Get("/auth/me")).Status);
        var ended = (await api.Get("/subscriptions/current")).Data!;
        Assert.Equal("trial", ended["status"].S()); Assert.True(ended["trialEnded"]!.GetValue<bool>());
        Assert.NotNull((await api.LoginAsAsync(NewHttp(), api.Email, "Passw0rd!")).Token); // الدخول متاح ليسدّد

        // دفع مرفوض: يبقى محجوباً
        var declined = await api.PaySubscriptionAsync(success: false);
        Assert.Equal("failed", (await api.Get($"/payments/{declined.Data!["id"].S()}")).Data!["status"].S());
        Assert.Equal(403, (await api.Get("/customers")).Status);

        // السداد يفتح النظام، والمدة المدفوعة تبدأ من يوم السداد
        await api.PaySubscriptionAsync("starter", "monthly");
        var current = (await api.Get("/subscriptions/current")).Data!;
        Assert.Equal("active", current["status"].S());
        Assert.Equal("starter", current["planType"].S());
        Assert.InRange((DateTime.Parse(current["expiryDate"].S()) - DateTime.Parse(current["startDate"].S())).TotalDays, 28, 31); // شهر من يوم السداد
        Assert.Equal(200, (await api.Get("/customers")).Status);
    }

    [Fact]
    public async Task Paying_during_the_trial_keeps_the_remaining_free_days()
    {
        var api = await NewTrialTenantAsync();
        var trialEnd = DateTime.Parse((await api.Get("/subscriptions/current")).Data!["expiryDate"].S());

        await api.PaySubscriptionAsync("professional", "yearly");
        var paid = (await api.Get("/subscriptions/current")).Data!;
        Assert.Equal("active", paid["status"].S());
        // سنة كاملة بعد نهاية الفترة التجريبية: أيامها الباقية أُضيفت إلى المدة المدفوعة
        Assert.InRange((DateTime.Parse(paid["expiryDate"].S()) - trialEnd.AddYears(1)).TotalDays, -1, 1);
        Assert.Equal(200, (await api.Get("/customers")).Status);
    }

    [Fact]
    public async Task A_plan_cannot_be_activated_without_paying()
    {
        var api = await NewTrialTenantAsync();
        var upgrade = await api.Post("/subscriptions/upgrade", new { planId = "enterprise", billingCycle = "yearly", paymentMethod = "mada" });
        Assert.Contains(upgrade.Status, new[] { 404, 405 });
        Assert.Equal("trial", (await api.Get("/subscriptions/current")).Data!["status"].S());

    }
}
