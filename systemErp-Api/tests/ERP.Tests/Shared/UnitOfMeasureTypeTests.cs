using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// وحدات القياس بأنواعها: لكل نوع وحدة أساس، والوحدة الفرعية تُربط بأساس نوعها، وتغيير الأساس بترقية وحدة مربوطة
/// يعيد ربط النوع كله بها بمعاملات محسوبة؛ وإلغاء الأساس وحده مرفوض برسالة توضّح الطريقة.
/// </summary>
[Collection("api")]
public class UnitOfMeasureTypeTests : TestBase
{
    public UnitOfMeasureTypeTests(ErpFactory f) : base(f) { }

    private static object Unit(string code, string type, bool isBase = false, string? baseCode = null, decimal factor = 1)
        => new { code, nameAr = "وحدة " + code, nameEn = code, symbol = code, unitType = type, isBaseUnit = isBase, baseUnitCode = baseCode, conversionFactor = factor, status = "active" };

    private static async Task<Dictionary<string, (string Id, bool IsBase, string? Base, decimal Factor, string Type)>> UnitsAsync(Client api)
        => (await api.Get("/unitsofmeasure?pageSize=200")).Data!["items"]!.AsArray().ToDictionary(u => u!["code"].S(),
            u => (u!["id"].S(), u["isBaseUnit"]!.GetValue<bool>(), u["baseUnitCode"]?.GetValue<string?>(), u["conversionFactor"].D(), u["unitType"].S()));

    [Fact]
    public async Task Each_type_has_its_own_base_and_the_base_can_be_switched_to_a_linked_unit()
    {
        var api = await NewTenantAsync();
        // أول وحدة في كل نوع أساسه
        Assert.Equal(201, (await api.Post("/unitsofmeasure", Unit("PCS", "count"))).Status);
        Assert.Equal(201, (await api.Post("/unitsofmeasure", Unit("G", "weight"))).Status);
        var units = await UnitsAsync(api);
        Assert.True(units["PCS"].IsBase);
        Assert.True(units["G"].IsBase);

        // الفرعية تُربط بأساس نوعها، ولا تُربط بأساس نوع آخر
        Assert.Equal(201, (await api.Post("/unitsofmeasure", Unit("KG", "weight", baseCode: "G", factor: 1000))).Status);
        Assert.Equal(201, (await api.Post("/unitsofmeasure", Unit("TON", "weight", factor: 1000000))).Status); // الفارغ = أساس النوع
        Assert.Equal(400, (await api.Post("/unitsofmeasure", Unit("CTN", "count", baseCode: "G", factor: 12))).Status);
        Assert.Equal(400, (await api.Post("/unitsofmeasure", Unit("X", "volume_bad"))).Status);
        // نوع له أساس: وحدة جديدة لا تُنشأ أساساً ثانياً
        Assert.Equal(400, (await api.Post("/unitsofmeasure", Unit("MG", "weight", isBase: true))).Status);

        // إلغاء الأساس وحده مرفوض
        units = await UnitsAsync(api);
        Assert.Equal(400, (await api.Put($"/unitsofmeasure/{units["G"].Id}", Unit("G", "weight", isBase: false, baseCode: "KG", factor: 0.001m))).Status);

        // ترقية الكيلو: يصبح الأساس، والجرام 0.001 كيلو، والطن 1000 كيلو
        Assert.Equal(200, (await api.Put($"/unitsofmeasure/{units["KG"].Id}", Unit("KG", "weight", isBase: true))).Status);
        units = await UnitsAsync(api);
        Assert.True(units["KG"].IsBase);
        Assert.Equal(1, units["KG"].Factor);
        Assert.False(units["G"].IsBase);
        Assert.Equal("KG", units["G"].Base);
        Assert.Equal(0.001m, units["G"].Factor);
        Assert.Equal("KG", units["TON"].Base);
        Assert.Equal(1000m, units["TON"].Factor);
        // نوع العدد لم يتأثر
        Assert.True(units["PCS"].IsBase);
        Assert.Equal("count", units["PCS"].Type);
    }
}
