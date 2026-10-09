using ERP.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ERP.Tests;

/// <summary>
/// الوحدات المرخّصة: كل منشأة ترى وحداتها فقط، والمشترك (المحاسبة، البيانات الأساسية، الفواتير، المخزون) متاح للجميع.
/// </summary>
[Collection("api")]
public class SubscriptionModuleTests : TestBase
{
    public SubscriptionModuleTests(ErpFactory f) : base(f) { }

    private static readonly string[] Shared = { "/customers", "/suppliers", "/paymentmethods", "/banks", "/accounts", "/journalentries", "/vouchers", "/products", "/invoices?kind=sales" };
    private static readonly string[] Trade = { "/quotations", "/agreements", "/commercialorders", "/commercialcontracts", "/deliverynotes", "/materialrequisitions" };
    private static readonly string[] Car = { "/vehicles", "/carbrands", "/carprocurementorders", "/carsalescontracts" };
    private static readonly string[] Pos = { "/pos/transactions", "/pos/shifts", "/pos/settings", "/pos/offers" };

    private static readonly string[] Hr = { "/employees", "/departments", "/leavetypes", "/leaverequests/balances", "/attendance/summary", "/payroll" };

    private async Task<Client> TenantWithAsync(params string[] modules)
    {
        var admin = await NewTenantAsync("شركة مدير المنصة");
        var host = Factory.WithWebHostBuilder(b => b.UseSetting("Platform:AdminEmails:0", admin.Email));
        var root = await admin.LoginAsAsync(host.CreateClient(), admin.Email, "Passw0rd!");

        var tenant = await NewTenantAsync("شركة " + string.Join("+", modules));
        var set = await root.Post($"/platform/tenants/{tenant.TenantId}/subscriptions/modules", new { modules });
        Assert.Equal(200, set.Status);
        return tenant;
    }

    private static async Task AssertAsync(Client tenant, IEnumerable<string> paths, bool allowed)
    {
        foreach (var path in paths)
        {
            var status = (await tenant.Get(path)).Status;
            if (allowed) Assert.True(status != 403, $"{path} مرفوض ({status}) وكان يجب أن يُسمح");
            else Assert.True(status == 403, $"{path} مسموح ({status}) وكان يجب أن يُرفض");
        }
    }

    [Theory]
    [InlineData("accounting")]
    [InlineData("car_showroom")]
    [InlineData("pos")]
    [InlineData("hr")]
    public async Task A_single_module_opens_its_screens_and_the_shared_ones_only(string module)
    {
        var tenant = await TenantWithAsync(module);

        await AssertAsync(tenant, Shared, allowed: true);
        await AssertAsync(tenant, Trade, allowed: module == "accounting");
        await AssertAsync(tenant, Car, allowed: module == "car_showroom");
        await AssertAsync(tenant, Pos, allowed: module == "pos");
        await AssertAsync(tenant, Hr, allowed: module == "hr");
    }

    [Fact]
    public async Task Point_of_sale_is_sold_on_its_own_or_together_with_others()
    {
        var both = await TenantWithAsync("car_showroom", "pos");
        await AssertAsync(both, Car.Concat(Pos).Concat(Shared), allowed: true);
        await AssertAsync(both, Trade, allowed: false);

        var current = (await both.Get("/subscriptions/current")).Data!["modules"]!.AsArray().Select(m => m.S()).ToArray();
        Assert.Equal(new[] { "car_showroom", "pos" }, current);
    }

    [Fact]
    public async Task A_new_company_gets_every_module_of_its_plan_including_point_of_sale()
    {
        var tenant = await NewTenantAsync("شركة جديدة");
        await AssertAsync(tenant, Shared.Concat(Trade).Concat(Car).Concat(Pos), allowed: true);
    }

    [Fact]
    public async Task Modifying_requests_are_gated_like_reads()
    {
        var tenant = await TenantWithAsync("accounting");
        Assert.Equal(403, (await tenant.Post("/pos/shifts/open", new { openingCash = 100, posTerminalName = "T1" })).Status);
        Assert.Equal(403, (await tenant.Post("/carbrands", new { nameAr = "تويوتا", nameEn = "Toyota" })).Status);
    }
}
