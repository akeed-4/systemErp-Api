namespace ERP.Tests.Infrastructure;

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ErpFactory> { }

/// <summary>قاعدة الاختبارات: يسجّل كل اختبار منشأة جديدة معزولة عن غيره.</summary>
public abstract class TestBase
{
    protected readonly ErpFactory Factory;
    protected TestBase(ErpFactory factory) => Factory = factory;

    protected HttpClient NewHttp() => Factory.CreateClient();
    protected async Task<Client> NewTenantAsync(string name = "شركة اختبار") => await new Client(NewHttp()).RegisterAsync(name);

    /// <summary>صنف بسعر 100 وضريبة 15% ومخزون افتتاحي بتكلفة معلومة.</summary>
    protected static async Task<Guid> SeedProductAsync(Client api, string sku = "P1", decimal stock = 10, decimal cost = 40, decimal price = 100)
    {
        await api.Post("/productcategories", new { code = "C1", nameAr = "تصنيف" });
        await api.Post("/unitsofmeasure", new { code = "PCS", nameAr = "قطعة", isBaseUnit = true, conversionFactor = 1 });
        var p = await api.Post("/products", new { sku, nameAr = "منتج " + sku, category = "C1", unit = "PCS", sellingPrice = price, vatRate = 15 });
        Assert.Equal(201, p.Status);
        var id = p.Data!["id"].G();
        if (stock > 0)
        {
            var m = await api.Post("/stockmovements/adjust", new { itemId = id, type = "adjustment_in", quantity = stock, unitCost = cost, referenceNumber = "OPEN" });
            Assert.Equal(200, m.Status);
        }
        return id;
    }

    protected static async Task<(Guid Id, string AccountCode)> SeedCustomerAsync(Client api, decimal creditLimit = 100000, string name = "عميل آجل")
    {
        var c = await api.Post("/customers", new { nameAr = name, phone = "050", city = "الرياض", creditLimit, creditPeriodDays = 30, status = "active" });
        Assert.Equal(201, c.Status);
        return (c.Data!["id"].G(), c.Data["accountCode"].S());
    }

    protected static async Task<(Guid Id, string AccountCode)> SeedSupplierAsync(Client api, string name = "مورد")
    {
        var s = await api.Post("/suppliers", new { nameAr = name, phone = "050", city = "جدة", status = "active", vatNumber = Client.NewVat() });
        Assert.Equal(201, s.Status);
        return (s.Data!["id"].G(), s.Data["accountCode"].S());
    }

    protected static (decimal Debit, decimal Credit) Totals(Res trialBalance)
    {
        decimal d = 0, c = 0;
        foreach (var row in trialBalance.Data!.AsArray()) { d += row!["periodDebit"].D(); c += row["periodCredit"].D(); }
        return (d, c);
    }
}
