using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// حساب مخزون الموقع: الموقع الرئيسي للمنشأة الجديدة على 114001 (يتبع اسمه)، والموقع المضاف بعده يأخذ الحساب الذي يختاره
/// المستخدم من الدليل ولا يُنشأ له حساب؛ بلا اختيار تُرحَّل حركاته على حساب المخزون في إعدادات الحسابات.
/// </summary>
[Collection("api")]
public class SiteInventoryAccountTests : TestBase
{
    public SiteInventoryAccountTests(ErpFactory f) : base(f) { }

    private static object Purchase(Guid supplier, Guid item, string warehouseId) => new
    {
        kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier, warehouseId,
        items = new[] { new { itemId = item, quantity = 2, unitPrice = 50, vatRate = 15 } },
    };

    private static async Task<decimal> BalanceAsync(Client api, string code) => (await api.Get($"/accounts/ByCode/{code}")).Data!["balance"].D();

    [Fact]
    public async Task Added_sites_use_the_chosen_account_and_no_account_is_created_for_them()
    {
        var api = await NewTenantAsync();
        var main = (await api.Get("/warehouses")).Data!["items"]!.AsArray().Single()!;
        Assert.Equal(MainSiteInventory, main["inventoryAccountCode"].S());
        Assert.Equal("114", (await api.Get($"/accounts/ByCode/{MainSiteInventory}")).Data!["parentCode"].S());

        // تغيير اسم الموقع الرئيسي يغيّر اسم حسابه المرتبط
        Assert.Equal(200, (await api.Put($"/warehouses/{main["id"].S()}", new { code = main["code"].S(), nameAr = "الفرع الرئيسي", nameEn = "Main", isDefault = true, status = "active" })).Status);
        Assert.Equal("مخزون - الفرع الرئيسي", (await api.Get($"/accounts/ByCode/{MainSiteInventory}")).Data!["nameAr"].S());

        // موقع بحساب مختار من الدليل: يحفظه كما هو ولا يُنشئ حساباً
        var accountsBefore = (await api.Get("/accounts?pageSize=1000")).Data!["totalCount"].D();
        await AccountingSettingsTests.AccountAsync(api, "1149", "asset", "114");
        var chosen = await api.Post("/warehouses", new { code = "JED", nameAr = "معرض جدة", nameEn = "Jeddah", location = "جدة", status = "active", inventoryAccountCode = "1149" });
        Assert.Equal(201, chosen.Status);
        Assert.Equal("1149", chosen.Data!["inventoryAccountCode"].S());
        Assert.Equal(accountsBefore + 1, (await api.Get("/accounts?pageSize=1000")).Data!["totalCount"].D()); // 1149 وحده

        // موقع بلا اختيار: لا حساب تلقائي باسمه
        var plain = await api.Post("/warehouses", new { code = "DMM", nameAr = "معرض الدمام", nameEn = "Dammam", location = "الدمام", status = "active" });
        Assert.Equal(201, plain.Status);
        Assert.Null(plain.Data!["inventoryAccountCode"]?.GetValue<string?>());
        Assert.Equal(404, (await api.Get("/accounts/ByCode/114002")).Status);

        // مشتريات كل موقع على حسابه: المختار، أو حساب المخزون في الإعدادات
        var (supplier, _) = await SeedSupplierAsync(api);
        var item = await SeedProductAsync(api, stock: 0);
        // حساب المخزون في الإعدادات (غير المضبوط = الافتراضي 1141)
        var settingsInventory = (await api.Get("/accounting-settings")).Data!["inventoryAccountCode"]?.GetValue<string?>() ?? "1141";
        var settingsBefore = await BalanceAsync(api, settingsInventory);
        Assert.Equal(201, (await api.PostInvoice(Purchase(supplier, item, chosen.Data["id"].S()))).Status);
        Assert.Equal(100m, await BalanceAsync(api, "1149"));
        Assert.Equal(201, (await api.PostInvoice(Purchase(supplier, item, plain.Data["id"].S()))).Status);
        Assert.Equal(settingsBefore + 100m, await BalanceAsync(api, settingsInventory));
    }
}
