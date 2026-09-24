using ERP.Tests.Infrastructure;

namespace ERP.Tests;

[Collection("api")]
public class TenantIsolationTests : TestBase
{
    public TenantIsolationTests(ErpFactory f) : base(f) { }

    [Fact]
    public async Task One_tenant_cannot_see_or_touch_another_tenants_data()
    {
        var a = await NewTenantAsync("أ"); var b = await NewTenantAsync("ب");
        var (customerId, _) = await SeedCustomerAsync(a);

        Assert.Equal(1, (await a.Get("/customers")).Data!["totalCount"]!.GetValue<int>());
        Assert.Equal(0, (await b.Get("/customers")).Data!["totalCount"]!.GetValue<int>());
        Assert.Equal(404, (await b.Get($"/customers/{customerId}")).Status);
        Assert.Equal(404, (await b.Delete($"/customers/{customerId}")).Status);
        Assert.Equal(404, (await b.Put($"/customers/{customerId}", new { nameAr = "اختراق", phone = "1", city = "x" })).Status);
    }

    [Fact]
    public async Task Tenant_header_cannot_be_used_to_impersonate_another_tenant()
    {
        var a = await NewTenantAsync(); var b = await NewTenantAsync();
        await SeedCustomerAsync(b);
        var http = NewHttp();
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/customers");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", a.Token);
        req.Headers.Add("X-Tenant-Id", b.TenantId.ToString());
        var resp = await http.SendAsync(req);
        var body = System.Text.Json.Nodes.JsonNode.Parse(await resp.Content.ReadAsStringAsync())!;
        Assert.Equal(0, body["data"]!["totalCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task Document_numbers_are_sequenced_per_tenant()
    {
        var a = await NewTenantAsync(); var b = await NewTenantAsync();
        var pa = await SeedProductAsync(a); var pb = await SeedProductAsync(b);
        object Inv(Guid p) => new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", items = new[] { new { itemId = p, quantity = 1, unitPrice = 100, vatRate = 15 } } };
        Assert.Equal("SINV-000001", (await a.Post("/invoices", Inv(pa))).Data!["invoiceNumber"].S());
        Assert.Equal("SINV-000001", (await b.Post("/invoices", Inv(pb))).Data!["invoiceNumber"].S());
        Assert.Equal("SINV-000002", (await a.Post("/invoices", Inv(pa))).Data!["invoiceNumber"].S());
    }
}
