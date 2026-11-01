using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// حساب مخزون لكل موقع تلقائياً: المنشأة الجديدة بموقعها الرئيسي على 114001، وكل موقع يُضاف بعده بحساب تالٍ تحت 114،
/// واسم الحساب يتبع اسم الموقع، ومستندات الموقع ترحَّل على حسابه.
/// </summary>
[Collection("api")]
public class SiteInventoryAccountTests : TestBase
{
    public SiteInventoryAccountTests(ErpFactory f) : base(f) { }

    [Fact]
    public async Task Every_site_gets_its_own_inventory_account_under_114()
    {
        var api = await NewTenantAsync();
        var main = (await api.Get("/warehouses")).Data!["items"]!.AsArray().Single()!;
        Assert.Equal(MainSiteInventory, main["inventoryAccountCode"].S());
        var account = (await api.Get($"/accounts/ByCode/{MainSiteInventory}")).Data!;
        Assert.Equal("114", account["parentCode"].S());
        Assert.Equal("مخزون - المستودع الرئيسي", account["nameAr"].S());

        var created = await api.Post("/warehouses", new { code = "JED", nameAr = "معرض جدة", nameEn = "Jeddah", location = "جدة", status = "active" });
        Assert.Equal(201, created.Status);
        Assert.Equal("114002", created.Data!["inventoryAccountCode"].S());
        Assert.Equal("مخزون - معرض جدة", (await api.Get("/accounts/ByCode/114002")).Data!["nameAr"].S());

        // تغيير اسم الموقع يغيّر اسم حسابه
        var id = created.Data["id"].S();
        Assert.Equal(200, (await api.Put($"/warehouses/{id}", new { code = "JED", nameAr = "معرض جدة الجديد", nameEn = "Jeddah New", location = "جدة", status = "active" })).Status);
        Assert.Equal("مخزون - معرض جدة الجديد", (await api.Get("/accounts/ByCode/114002")).Data!["nameAr"].S());

        // حساب يحدده المستخدم عند الإنشاء يبقى كما هو
        await AccountingSettingsTests.AccountAsync(api, "1149", "asset", "114");
        var manual = await api.Post("/warehouses", new { code = "OWN", nameAr = "موقع بحساب محدد", nameEn = "Own", location = "الدمام", status = "active", inventoryAccountCode = "1149" });
        Assert.Equal("1149", manual.Data!["inventoryAccountCode"].S());

        // مشتريات الموقع على حسابه
        var (supplier, _) = await SeedSupplierAsync(api);
        var item = await SeedProductAsync(api, stock: 0);
        var purchase = await api.PostInvoice(new
        {
            kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier, warehouseId = id,
            items = new[] { new { itemId = item, quantity = 2, unitPrice = 50, vatRate = 15 } },
        });
        Assert.Equal(201, purchase.Status);
        Assert.Equal(100m, (await api.Get("/accounts/ByCode/114002")).Data!["balance"].D());
    }
}
