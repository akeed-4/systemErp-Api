using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>العنوان الوطني المطلوب للهيئة (يبدأ بالدولة) على العميل والمورد والبنك.</summary>
[Collection("api")]
public class PartyAddressTests : TestBase
{
    public PartyAddressTests(ErpFactory f) : base(f) { }

    private static object Address(string country = "SA", string buildingNo = "1234", string postalCode = "12345", string additionalNo = "5678") => new
    {
        nameAr = "طرف بعنوان", phone = "050", status = "active", iban = "SA0380000000123456789012",
        country, city = "الرياض", district = "العليا", street = "طريق الملك فهد", buildingNo, postalCode, additionalNo,
    };

    [Theory]
    [InlineData("customers")]
    [InlineData("suppliers")]
    [InlineData("banks")]
    public async Task Full_national_address_is_saved_and_returned(string resource)
    {
        var api = await NewTenantAsync();
        var created = await api.Post($"/{resource}", Address(country: "sa"));
        Assert.True(created.Status == 201, created.Body?.ToString());
        var saved = (await api.Get($"/{resource}/{created.Data!["id"].S()}")).Data!;
        Assert.Equal("SA", saved["country"].S()); // يُوحَّد بحروف كبيرة
        Assert.Equal("الرياض", saved["city"].S()); Assert.Equal("العليا", saved["district"].S()); Assert.Equal("طريق الملك فهد", saved["street"].S());
        Assert.Equal("1234", saved["buildingNo"].S()); Assert.Equal("12345", saved["postalCode"].S()); Assert.Equal("5678", saved["additionalNo"].S());

        // بلا دولة: السعودية افتراضياً
        var plain = await api.Post($"/{resource}", new { nameAr = "طرف بلا عنوان", phone = "050", status = "active", city = "جدة" });
        Assert.True(plain.Status == 201, plain.Body?.ToString());
        Assert.Equal("SA", plain.Data!["country"].S());
    }

    [Theory]
    [InlineData("customers")]
    [InlineData("suppliers")]
    [InlineData("banks")]
    public async Task Saudi_address_numbers_are_validated_but_foreign_ones_are_free(string resource)
    {
        var api = await NewTenantAsync();
        Assert.Equal(400, (await api.Post($"/{resource}", Address(buildingNo: "12"))).Status);
        Assert.Equal(400, (await api.Post($"/{resource}", Address(postalCode: "1234"))).Status);
        Assert.Equal(400, (await api.Post($"/{resource}", Address(additionalNo: "abcd"))).Status);
        Assert.Equal(400, (await api.Post($"/{resource}", Address(country: "SAU"))).Status);
        var foreign = await api.Post($"/{resource}", Address(country: "AE", buildingNo: "12", postalCode: "00000-1", additionalNo: ""));
        Assert.True(foreign.Status == 201, foreign.Body?.ToString());
        Assert.Equal("AE", foreign.Data!["country"].S());
    }

    [Fact]
    public async Task Foreign_customer_vat_number_is_not_held_to_the_saudi_format()
    {
        var api = await NewTenantAsync();
        Assert.Equal(400, (await api.Post("/customers", new { nameAr = "محلي", phone = "050", city = "الرياض", status = "active", vatNumber = "123" })).Status);
        Assert.Equal(201, (await api.Post("/customers", new { nameAr = "أجنبي", phone = "050", city = "دبي", status = "active", country = "AE", vatNumber = "100123456700003" })).Status);
    }
}
