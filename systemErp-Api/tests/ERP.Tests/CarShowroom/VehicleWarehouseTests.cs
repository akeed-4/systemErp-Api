using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// معرض السيارة: تدخل معرض فاتورة شرائها، وتُباع من معرضها فقط، وتنتقل بالتحويل، وجرد المعرض يتوقع سياراته وحدها.
/// </summary>
[Collection("api")]
public class VehicleWarehouseTests : TestBase
{
    public VehicleWarehouseTests(ErpFactory f) : base(f) { }

    private static async Task<string> WarehouseAsync(Client api, string code)
        => (await api.Post("/warehouses", new { code, nameAr = "معرض " + code, nameEn = code, location = "الرياض", status = "active" })).Data!["id"].S();

    [Fact]
    public async Task Vehicles_live_in_a_showroom_are_sold_from_it_transferred_and_counted_per_showroom()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var riyadh = await WarehouseAsync(api, "RUH");
        var jeddah = await WarehouseAsync(api, "JED");
        var vin = Client.NewVin('7');

        // الشراء إلى معرض الرياض
        var purchase = await api.PostInvoice(new
        {
            kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier, status = "posted", warehouseId = riyadh,
            vehicleLines = new[] { new { vin, brandNameAr = "تويوتا", modelNameAr = "كامري", trimNameAr = "GLE", year = 2025,
                colorExterior = "أبيض", colorInterior = "بيج", vatMode = "standard_15", unitPrice = 1000m, discount = 0m } },
        });
        Assert.Equal(201, purchase.Status);
        var vehicle = (await api.Get("/vehicles")).Data!["items"]!.AsArray().Single(v => v!["chassisNumber"].S() == vin)!;
        var vehicleId = vehicle["id"].S();
        Assert.Equal(riyadh, vehicle["warehouseId"].S());
        Assert.Equal("معرض RUH", vehicle["warehouseName"].S());

        // جرد كل معرض يتوقع سياراته فقط
        Assert.Single((await api.Post("/inventorycounts/snapshot", new { scope = "vehicles", warehouseId = riyadh })).Data!.AsArray());
        Assert.Empty((await api.Post("/inventorycounts/snapshot", new { scope = "vehicles", warehouseId = jeddah })).Data!.AsArray());

        // البيع من معرض آخر مرفوض
        object Sale(string warehouseId) => new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "posted", warehouseId,
            vehicleLines = new[] { new { vehicleId, unitPrice = 1500m, discount = 0m, vatMode = "standard_15" } },
        };
        Assert.Equal(409, (await api.PostInvoice(Sale(jeddah))).Status);

        // التحويل لجدة ثم البيع منها
        var moved = await api.Post("/vehicles/transfer", new { vehicleIds = new[] { vehicleId }, warehouseId = jeddah });
        Assert.Equal(200, moved.Status);
        Assert.Equal(1, moved.Data!["moved"].D());
        Assert.Equal(jeddah, (await api.Get($"/vehicles/{vehicleId}")).Data!["warehouseId"].S());
        Assert.Equal(201, (await api.PostInvoice(Sale(jeddah))).Status);

        // المباعة لا تُحوَّل
        Assert.Equal(409, (await api.Post("/vehicles/transfer", new { vehicleIds = new[] { vehicleId }, warehouseId = riyadh })).Status);
    }

    [Fact]
    public async Task Sales_contract_follows_its_vehicle_showroom_and_reports_filter_by_showroom()
    {
        var api = await NewTenantAsync();
        var riyadh = await WarehouseAsync(api, "RUH");
        var jeddah = await WarehouseAsync(api, "JED");
        async Task<string> CarAsync(char seed, string warehouseId) => (await api.Post("/vehicles", new
        {
            chassisNumber = Client.NewVin(seed), brandNameAr = "تويوتا", modelNameAr = "يارس", year = 2024, condition = "new", purchasePrice = 40000, sellingPrice = 50000,
            vatMode = "standard_15", fuelType = "petrol", transmission = "automatic", colorExterior = "أبيض", colorInterior = "أسود", warehouseId,
        })).Data!["id"].S();
        var inRiyadh = await CarAsync('1', riyadh);
        await CarAsync('2', jeddah);

        // العقد يأخذ معرض سيارته
        var contract = await api.Post("/carsalescontracts", new
        {
            cycleType = "individual", buyerName = "فرد", buyerNationalIdOrCr = "1010101010", buyerPhone = "0550000000",
            vehicleId = inRiyadh, sellingPrice = 50000, vatMode = "standard_15", paymentMethod = "cash", condition = "new",
        });
        Assert.Equal(201, contract.Status);
        Assert.Equal(riyadh, contract.Data!["warehouseId"].S());

        // تقارير المخزون بفلتر المعرض
        var all = (await api.Get("/reports/car/VinInventory")).Data!.AsArray();
        Assert.Equal(2, all.Count);
        var jeddahRows = (await api.Get($"/reports/car/VinInventory?warehouseId={jeddah}")).Data!.AsArray();
        Assert.Equal("معرض JED", Assert.Single(jeddahRows)!["warehouseName"].S());
        Assert.Single((await api.Get($"/reports/car/InventoryAging?warehouseId={riyadh}")).Data!.AsArray());
    }

    [Fact]
    public async Task A_vehicle_found_in_another_showroom_count_moves_there_on_approval()
    {
        var api = await NewTenantAsync();
        var riyadh = await WarehouseAsync(api, "RUH");
        var jeddah = await WarehouseAsync(api, "JED");
        var vin = Client.NewVin('8');
        var created = await api.Post("/vehicles", new { chassisNumber = vin, brandNameAr = "هيونداي", modelNameAr = "سوناتا", year = 2025,
            purchasePrice = 50000, sellingPrice = 60000, vatMode = "standard_15", warehouseId = riyadh });
        Assert.Equal(201, created.Status);
        Assert.Equal(riyadh, created.Data!["warehouseId"].S());

        // جرد جدة يجدها: فرق «نقل» بلا قيمة، والاعتماد ينقلها لجدة
        var count = await api.Post("/inventorycounts", new { scope = "vehicles", countDate = DateTime.UtcNow, warehouseId = jeddah,
            lines = new[] { new { chassisNumber = vin, countedQuantity = 1 } } });
        Assert.Equal(201, count.Status);
        Assert.Equal(0, count.Data!["netVarianceValue"].D());
        var approval = (await api.Post($"/inventorycounts/{count.Data["id"].S()}/submit")).Data!;
        Assert.Equal(200, (await api.Post($"/inventorycountapprovals/{approval["id"].S()}/approve", new { })).Status);
        Assert.Equal(jeddah, (await api.Get($"/vehicles/{created.Data["id"].S()}")).Data!["warehouseId"].S());
    }
}
