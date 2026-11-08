using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// تفرّد الباركود داخل المنشأة (بالمفتاح المفهرس BarcodeLookupKey مع مطابقة القيمة الأصلية)، والبحث الدقيق بالباركود
/// كما يرسله قارئ الباركود في الواجهة (barcodeLookupKey = القيمة).
/// </summary>
[Collection("api")]
public class ProductBarcodeTests : TestBase
{
    public ProductBarcodeTests(ErpFactory f) : base(f) { }

    private static object Product(string sku, string? barcode)
        => new { sku, barcode, nameAr = "صنف " + sku, category = "C1", unit = "PCS", sellingPrice = 10, vatRate = 15 };

    private static async Task SetupAsync(Client api)
    {
        await api.Post("/productcategories", new { code = "C1", nameAr = "تصنيف" });
        await api.Post("/unitsofmeasure", new { code = "PCS", nameAr = "قطعة", isBaseUnit = true, conversionFactor = 1 });
    }

    [Fact]
    public async Task Duplicate_barcode_is_rejected_within_a_tenant_but_allowed_across_tenants()
    {
        var a = await NewTenantAsync(); var b = await NewTenantAsync();
        await SetupAsync(a); await SetupAsync(b);

        var first = await a.Post("/products", Product("B1", "6281000000017"));
        Assert.Equal(201, first.Status);
        var id = first.Data!["id"].G();

        Assert.Equal(409, (await a.Post("/products", Product("B2", "6281000000017"))).Status);
        // حفظ الصنف نفسه بباركوده لا يُعدّ تكراراً
        Assert.Equal(200, (await a.Put($"/products/{id}", Product("B1", "6281000000017"))).Status);
        // صنف آخر لا يأخذ باركود صنف قائم بالتعديل
        var other = (await a.Post("/products", Product("B3", "6281000000024"))).Data!["id"].G();
        Assert.Equal(409, (await a.Put($"/products/{other}", Product("B3", "6281000000017"))).Status);
        // منشأة أخرى مستقلة
        Assert.Equal(201, (await b.Post("/products", Product("B1", "6281000000017"))).Status);
        // بلا باركود: لا فحص تفرّد
        Assert.Equal(201, (await a.Post("/products", Product("N1", null))).Status);
        Assert.Equal(201, (await a.Post("/products", Product("N2", null))).Status);
    }

    [Fact]
    public async Task Barcodes_longer_than_the_lookup_key_are_compared_in_full()
    {
        var api = await NewTenantAsync();
        await SetupAsync(api);
        var prefix = new string('7', 450);

        Assert.Equal(201, (await api.Post("/products", Product("L1", prefix + "A"))).Status);
        // نفس أول 450 حرفاً وباقٍ مختلف: باركود مختلف
        Assert.Equal(201, (await api.Post("/products", Product("L2", prefix + "B"))).Status);
        // الباركود القصير المساوي للمفتاح وحده ليس تكراراً للطويل
        Assert.Equal(201, (await api.Post("/products", Product("L3", prefix))).Status);
        Assert.Equal(409, (await api.Post("/products", Product("L4", prefix + "A"))).Status);
        Assert.Equal(409, (await api.Post("/products", Product("L5", prefix))).Status);
    }

    [Fact]
    public async Task Exact_barcode_lookup_finds_the_product_through_the_load_endpoint()
    {
        var a = await NewTenantAsync(); var b = await NewTenantAsync();
        await SetupAsync(a); await SetupAsync(b);
        var id = (await a.Post("/products", Product("S1", "6289999999995"))).Data!["id"].G();
        await b.Post("/products", Product("S1", "6289999999995"));

        var filter = Uri.EscapeDataString("[[\"sku\",\"=\",\"6289999999995\"],\"or\",[\"barcodeLookupKey\",\"=\",\"6289999999995\"]]");
        var found = await a.Get($"/products/load?take=1&filter={filter}");
        Assert.Equal(200, found.Status);
        var rows = found.Body!["data"]!.AsArray();
        Assert.Single(rows);
        Assert.Equal(id, rows[0]!["id"].G());
    }
}
