using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// نوع فاتورة بيع المعارض من الحالة الضريبية للعميل لا من الشاشة: مبسطة (B2C) افتراضاً ولغير المسجَّل وللبيع بلا عميل،
/// وضريبية (B2B) للعميل المسجَّل في ضريبة القيمة المضافة (ويلزمه رقمه الضريبي).
/// </summary>
[Collection("api")]
public class ShowroomInvoiceTypeTests : TestBase
{
    public ShowroomInvoiceTypeTests(ErpFactory f) : base(f) { }

    private static async Task<string> CarAsync(Client api, char seed)
    {
        var car = await api.Post("/vehicles", new { chassisNumber = Client.NewVin(seed), brandNameAr = "تويوتا", modelNameAr = "كامري", year = 2026, condition = "new",
            purchasePrice = 80000, sellingPrice = 100000, vatMode = "standard_15", location = "المعرض" });
        Assert.Equal(201, car.Status);
        return car.Data!["id"].S();
    }

    private static object Customer(string name, bool registered, string? vat) => new
    {
        nameAr = name, phone = "0500000000", city = "الرياض", status = "active", isVatRegistered = registered, vatNumber = vat,
        street = "طريق الملك فهد", buildingNo = "1234", district = "العليا", postalCode = "12345",
    };

    private static object Sale(string vehicleId, string? partyId, string invoiceType = "tax_invoice") => new
    {
        kind = "sales", invoiceType, paymentMethod = "cash", status = "posted", partyId,
        vehicleLines = new[] { new { vehicleId, unitPrice = 100000m, discount = 0m, vatMode = "standard_15" } },
    };

    [Fact]
    public async Task Showroom_sale_type_follows_the_customer_vat_registration_and_defaults_to_simplified()
    {
        var api = await NewTenantAsync();

        // المسجَّل يلزمه رقمه الضريبي
        Assert.Equal(400, (await api.Post("/customers", Customer("شركة بلا رقم", true, null))).Status);

        var individual = await api.Post("/customers", Customer("فرد", false, null));
        var company = await api.Post("/customers", Customer("شركة مسجلة", true, "300000000000003"));
        Assert.Equal(201, individual.Status); Assert.Equal(201, company.Status);
        Assert.False(individual.Data!["isVatRegistered"]!.GetValue<bool>());
        Assert.True(company.Data!["isVatRegistered"]!.GetValue<bool>());

        // الشاشة ترسل «ضريبية»؛ العميل غير المسجَّل يجعلها مبسطة (ولا تُطلب بيانات المشتري الكاملة)
        var b2c = await api.PostInvoice(Sale(await CarAsync(api, '1'), individual.Data["id"].S()));
        Assert.Equal(201, b2c.Status);
        Assert.Equal("simplified", b2c.Data!["invoiceType"].S());

        // والمسجَّل يجعلها ضريبية ولو أرسلت الشاشة «مبسطة»
        var b2b = await api.PostInvoice(Sale(await CarAsync(api, '2'), company.Data["id"].S(), "simplified"));
        Assert.Equal(201, b2b.Status);
        Assert.Equal("tax_invoice", b2b.Data!["invoiceType"].S());
        Assert.Equal("300000000000003", b2b.Data["partyVatNumber"].S());

        // بلا عميل: مبسطة
        var cash = await api.PostInvoice(Sale(await CarAsync(api, '3'), null));
        Assert.Equal(201, cash.Status);
        Assert.Equal("simplified", cash.Data!["invoiceType"].S());
    }
}
