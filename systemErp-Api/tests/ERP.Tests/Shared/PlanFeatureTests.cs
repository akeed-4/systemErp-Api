using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// ميزات الباقة (طبقة تحت الوحدات): باقة المنشآت الصغيرة بالأساسيات فقط، والميزات المتقدمة تُمنع من السرفر،
/// والاشتراكات القائمة بكل الميزات، وحد الفواتير الشهري يظهر في الاستخدام تنبيهاً لا منعاً.
/// </summary>
[Collection("api")]
public class PlanFeatureTests : TestBase
{
    public PlanFeatureTests(ErpFactory f) : base(f) { }

    private async Task<Client> SmallBusinessAsync() => await new Client(NewHttp()).RegisterAsync("منشأة صغيرة", planId: "small_business");

    [Fact]
    public async Task Small_business_plan_is_offered_with_accounting_only_and_no_advanced_features()
    {
        var plans = (await new Client(NewHttp()).Get("/subscriptions/plans")).Data!.AsArray();
        var small = plans.Single(p => p!["id"].S() == "small_business")!;
        Assert.Equal(new[] { "accounting" }, small["modules"]!.AsArray().Select(m => m!.GetValue<string>()));
        Assert.Empty(small["featureKeys"]!.AsArray());
        Assert.Equal(500, (int)small["maxInvoicesPerMonth"]!);
        Assert.Equal(2, (int)small["maxUsers"]!);

        // الباقات الأخرى بكل الميزات
        var professional = plans.Single(p => p!["id"].S() == "professional")!;
        Assert.Equal(8, professional["featureKeys"]!.AsArray().Count);
    }

    [Fact]
    public async Task Small_business_tenant_gets_the_basics_and_is_refused_the_advanced_features()
    {
        var api = await SmallBusinessAsync();
        var current = (await api.Get("/subscriptions/current")).Data!;
        Assert.Equal("small_business", current["planType"].S());
        Assert.Empty(current["featureKeys"]!.AsArray());

        // الأساسيات تعمل: صنف وعميل وفاتورة مبيعات
        var product = await SeedProductAsync(api);
        var sale = await api.PostInvoice(new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash",
            items = new[] { new { itemId = product, quantity = 1, unitPrice = 100, vatRate = 15 } } });
        Assert.Equal(201, sale.Status);
        Assert.Equal(200, (await api.Get("/accounts")).Status);
        Assert.Equal(200, (await api.Get("/reports/TrialBalance")).Status);

        // الميزات المتقدمة ممنوعة برسالة تذكر الميزة
        foreach (var path in new[] { "/quotations", "/commercialcontracts", "/deliverynotes", "/materialrequisitions", "/purchase-landed-costs",
                                     "/costcenters", "/fixedassets", "/bankreconciliations", "/ApprovalPolicies" })
        {
            var r = await api.Get(path);
            Assert.True(r.Status == 403, $"{path} → {r.Status}");
        }
        var denied = await api.Get("/costcenters");
        Assert.Contains("مراكز التكلفة", denied.Body!["message"].S());
        Assert.Equal(403, (await api.Post("/stocktransfers", new { })).Status);

        // العملات: القراءة متاحة (قوائم الفواتير) والإضافة ميزة متقدمة، والفاتورة بعملة أجنبية مرفوضة
        Assert.Equal(200, (await api.Get("/currencies")).Status);
        Assert.Equal(403, (await api.Post("/currencies", new { code = "USD", nameAr = "دولار", nameEn = "Dollar", symbol = "$", exchangeRate = 3.75 })).Status);
        var foreign = await api.PostInvoice(new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", currencyCode = "USD", exchangeRate = 3.75m,
            items = new[] { new { itemId = product, quantity = 1, unitPrice = 100, vatRate = 15 } } });
        Assert.Equal(400, foreign.Status);
        Assert.Contains("تعدد العملات", foreign.Body!["message"].S());
    }

    [Fact]
    public async Task Existing_plans_keep_every_feature()
    {
        var api = await NewTenantAsync(); // باقة professional
        Assert.Equal(8, (await api.Get("/subscriptions/current")).Data!["featureKeys"]!.AsArray().Count);
        Assert.Equal(200, (await api.Get("/costcenters")).Status);
        Assert.Equal(200, (await api.Get("/quotations")).Status);
        Assert.Equal(200, (await api.Get("/fixedassets")).Status);
    }

    [Fact]
    public async Task Invoice_limit_is_reported_in_usage_and_never_blocks_posting()
    {
        var api = await SmallBusinessAsync();
        var product = await SeedProductAsync(api);
        for (var i = 0; i < 2; i++)
            Assert.Equal(201, (await api.PostInvoice(new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash",
                items = new[] { new { itemId = product, quantity = 1, unitPrice = 100, vatRate = 15 } } })).Status);
        var usage = (await api.Get("/subscriptions/usage")).Data!;
        Assert.Equal(2, (int)usage["invoicesThisMonth"]!);
        Assert.Equal(500, (int)usage["maxInvoicesPerMonth"]!);

        // باقة بلا حد: null
        var unlimited = (await (await NewTenantAsync()).Get("/subscriptions/usage")).Data!;
        Assert.Null(unlimited["maxInvoicesPerMonth"]);
    }

    /// <summary>منشأة نزلت إلى باقة بلا مراكز التكلفة: حساب كان يلزم بمركز لا يمنع الترحيل، فلا مركز يُختار في شاشاتها.</summary>
    [Fact]
    public async Task Required_cost_center_is_not_enforced_once_the_plan_drops_cost_centers()
    {
        var admin = await NewTenantAsync("مدير المنصة - المراكز");
        var host = Factory.WithWebHostBuilder(b => b.UseSetting("Platform:AdminEmails:0", admin.Email));
        var root = await admin.LoginAsAsync(host.CreateClient(), admin.Email, "Passw0rd!");
        var api = await NewTenantAsync("منشأة تنزل باقتها");

        var account = (await api.Get("/accounts/ByCode/521")).Data!.AsObject();
        account["costCenterPolicy"] = "required";
        Assert.Equal(200, (await api.Put($"/accounts/{account["id"].S()}", account)).Status);
        object Entry() => new { date = DateTime.UtcNow, description = "مصروف",
            lines = new[] { new { accountCode = "521", debit = 50m, credit = 0m }, new { accountCode = "1111", debit = 0m, credit = 50m } } };
        Assert.Equal(400, (await api.Post("/journalentries", Entry())).Status); // الباقة فيها المراكز: الإلزام نافذ

        // مدير المنصة يزيل ميزات المنشأة (دون تغيير وحداتها)
        var set = await root.Post($"/platform/tenants/{api.TenantId}/subscriptions/modules", new { modules = new[] { "accounting" }, featureKeys = Array.Empty<string>() });
        Assert.Equal(200, set.Status);
        Assert.Empty(set.Data!["featureKeys"]!.AsArray());
        Assert.Equal(200, (await api.Post("/journalentries", Entry())).Status);
        Assert.Equal(403, (await api.Get("/costcenters")).Status);
    }
}
