using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// نطاق ترقيم المستندات من إعداد المنشأة: تسلسل واحد للمنشأة (الافتراضي)، أو لكل مستودع برمزه، أو لكل مستخدم برمزه.
/// رموز العملاء والموردين تبقى للمنشأة في كل الأحوال.
/// </summary>
[Collection("api")]
public class DocumentNumberingScopeTests : TestBase
{
    public DocumentNumberingScopeTests(ErpFactory f) : base(f) { }

    private static object Sale(string? warehouseId) => new
    {
        kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "posted", warehouseId,
        items = new[] { new { itemName = "خدمة", quantity = 1, unitPrice = 100, vatRate = 15 } },
    };

    private static async Task<string> WarehouseAsync(Client api, string code)
        => (await api.Post("/warehouses", new { code, nameAr = "معرض " + code, nameEn = code, location = "الرياض", status = "active" })).Data!["id"].S();

    [Fact]
    public async Task Documents_are_numbered_per_tenant_site_or_user_as_configured()
    {
        var api = await NewTenantAsync();
        var riyadh = await WarehouseAsync(api, "RUH");
        var jeddah = await WarehouseAsync(api, "JED");

        // الافتراضي: تسلسل واحد للمنشأة
        Assert.Equal("tenant", (await api.Get("/warehouse-session")).Data!["documentNumberingScope"].S());
        var first = await api.PostInvoice(Sale(null));
        Assert.Equal("SINV-000001", first.Data!["invoiceNumber"].S());

        // لكل مستودع: كل معرض بتسلسله ورمزه، بحسب مستودع الفاتورة
        Assert.Equal(200, (await api.Put("/warehouse-session/numbering-scope", new { documentNumberingScope = "warehouse" })).Status);
        Assert.Equal("SINV-RUH-000001", (await api.PostInvoice(Sale(riyadh))).Data!["invoiceNumber"].S());
        Assert.Equal("SINV-JED-000001", (await api.PostInvoice(Sale(jeddah))).Data!["invoiceNumber"].S());
        var ruhSale = await api.PostInvoice(Sale(riyadh));
        Assert.Equal("SINV-RUH-000002", ruhSale.Data!["invoiceNumber"].S());
        // قيد الفاتورة بتسلسل مستودعها، والجرد بمستودعه ولو كانت الجلسة على «كل المواقع»
        var journal = await api.Get($"/journalentries/{ruhSale.Data["journalEntryId"].S()}");
        Assert.StartsWith("JE-RUH-", journal.Data!["entryNumber"].S());
        var product = await SeedProductAsync(api, stock: 0);
        var count = await api.Post("/inventorycounts", new { scope = "items", countDate = DateTime.UtcNow, warehouseId = jeddah,
            lines = new[] { new { itemId = product, countedQuantity = 0 } } });
        Assert.Equal("IC-JED-000001", count.Data!["countNumber"].S());

        // لكل مستخدم: رمز يُمنح للمستخدم عند أول مستند
        Assert.Equal(200, (await api.Put("/warehouse-session/numbering-scope", new { documentNumberingScope = "user" })).Status);
        var byUser = (await api.PostInvoice(Sale(riyadh))).Data!["invoiceNumber"].S();
        Assert.Matches(@"^SINV-U\d{2}-000001$", byUser);
        var next = (await api.PostInvoice(Sale(jeddah))).Data!["invoiceNumber"].S();
        Assert.Equal(byUser[..^6] + "000002", next);

        // البيانات الأساسية تبقى بتسلسل المنشأة
        var (_, _) = await SeedCustomerAsync(api);
        var customers = (await api.Get("/customers")).Data!["items"]!.AsArray();
        Assert.All(customers, c => Assert.DoesNotContain("-U", c!["code"].S()));

        // العودة للمنشأة: يكمل تسلسلها العام
        Assert.Equal(200, (await api.Put("/warehouse-session/numbering-scope", new { documentNumberingScope = "tenant" })).Status);
        Assert.Equal("SINV-000002", (await api.PostInvoice(Sale(riyadh))).Data!["invoiceNumber"].S());
        Assert.Equal(400, (await api.Put("/warehouse-session/numbering-scope", new { documentNumberingScope = "branch" })).Status);
    }
}
