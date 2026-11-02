using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// الدورات كاملة بمعرضين بعد ربط السيارة بالمعرض: أمر شراء بمعرض استلام حتى الاستلام، عقد بيع بكل مراحله حتى الفاتورة والمرتجع،
/// فواتير سيارات مباشرة (شراء وبيع ومرتجع)، وتقارير المعارض بفلتر المعرض.
/// </summary>
[Collection("api")]
public class ShowroomCyclesEndToEndTests : TestBase
{
    public ShowroomCyclesEndToEndTests(ErpFactory f) : base(f) { }

    private static async Task<string> WarehouseAsync(Client api, string code)
        => (await api.Post("/warehouses", new { code, nameAr = "معرض " + code, nameEn = code, location = "الرياض", status = "active" })).Data!["id"].S();

    private static async Task<int> CountAsync(Client api, string report, string warehouseId)
    {
        var r = await api.Get($"/reports/car/{report}?warehouseId={warehouseId}");
        Assert.True(r.Status == 200, $"{report} → {r.Status}: {r.Body}");
        return r.Data!.AsArray().Count;
    }

    [Fact]
    public async Task Procurement_and_sales_contract_cycles_run_end_to_end_within_their_showroom()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var riyadh = await WarehouseAsync(api, "RUH");
        var jeddah = await WarehouseAsync(api, "JED");

        // ---- دورة الشراء: أمر بمعرض استلام جدة حتى استلام الشواصي
        var order = await api.Post("/carprocurementorders", new
        {
            supplierId = supplier, paymentType = "credit", creditDays = 30, currency = "SAR", exchangeRate = 1, date = DateTime.UtcNow, warehouseId = jeddah,
            items = new[] { new { brandName = "تويوتا", modelName = "كامري", trimName = "GLE", year = 2025, quantity = 1, unitPrice = 80000m, color = "أبيض" } },
        });
        Assert.True(order.Status == 201, order.Body?.ToJsonString());
        var orderId = order.Data!["id"].S();
        Assert.Equal(jeddah, order.Data["warehouseId"].S());
        foreach (var stage in new[] { "requisition_approved", "rfq", "rfq_approved", "purchase_order" })
        {
            var adv = await api.Post($"/carprocurementorders/{orderId}/AdvanceStage", new { targetStage = stage });
            Assert.True(adv.Status == 200, $"{stage}: {adv.Body}");
        }
        var vin = Client.NewVin('5');
        var receipt = await api.Post($"/carprocurementorders/{orderId}/AdvanceStage", new
        {
            targetStage = "vin_received", pdiInspectionPassed = true, vins = new[] { new { vin, engineNumber = "E-1" } },
        });
        Assert.True(receipt.Status == 200, receipt.Body?.ToJsonString());
        var vehicle = (await api.Get("/vehicles")).Data!["items"]!.AsArray().Single(v => v!["chassisNumber"].S() == vin)!;
        var vehicleId = vehicle["id"].S();
        Assert.Equal(jeddah, vehicle["warehouseId"].S()); // دخلت معرض الاستلام
        Assert.Equal(1, await CountAsync(api, "ProcurementTracking", jeddah));
        Assert.Equal(0, await CountAsync(api, "ProcurementTracking", riyadh));
        Assert.Equal(1, await CountAsync(api, "VinInventory", jeddah));

        // ---- دورة البيع: عقد على سيارة جدة بكل مراحله
        var contract = await api.Post("/carsalescontracts", new
        {
            cycleType = "individual", date = DateTime.UtcNow, buyerName = "مشتري", buyerNationalIdOrCr = "1010101010", buyerPhone = "0550000000",
            vehicleId, sellingPrice = 100000, vatMode = "standard_15", paymentMethod = "cash", condition = "new",
        });
        Assert.True(contract.Status == 201, contract.Body?.ToJsonString());
        var contractId = contract.Data!["id"].S();
        Assert.Equal(jeddah, contract.Data["warehouseId"].S()); // العقد في معرض سيارته
        foreach (var status in new[] { "approved", "allocated" })
        {
            var adv = await api.Post($"/carsalescontracts/{contractId}/AdvanceStatus", new { targetStatus = status });
            Assert.True(adv.Status == 200, $"{status}: {adv.Body}");
        }
        var delivered = await api.Post($"/carsalescontracts/{contractId}/AdvanceStatus",
            new { targetStatus = "delivered", handoverProtocolNumber = "H-1", handoverSignee = "مشتري", handoverSigneeNationalId = "1010101010" });
        Assert.True(delivered.Status == 200, delivered.Body?.ToJsonString());
        var invoiced = await api.Post($"/carsalescontracts/{contractId}/AdvanceStatus", new { targetStatus = "invoiced" });
        Assert.True(invoiced.Status == 200, invoiced.Body?.ToJsonString());
        var invoice = (await api.Get(await api.InvoicePath(invoiced.Data!["invoiceId"].S()))).Data!;
        Assert.Equal(jeddah, invoice["warehouseId"].S()); // الفاتورة من معرض السيارة
        Assert.Equal(115000, invoice["grandTotal"].D());
        Assert.Equal("sold", (await api.Get($"/vehicles/{vehicleId}")).Data!["status"].S());

        // تقارير المبيعات بمعرض الفاتورة
        Assert.Equal(1, await CountAsync(api, "SalesPerformance", jeddah));
        Assert.Equal(0, await CountAsync(api, "SalesPerformance", riyadh));
        Assert.Equal(1, await CountAsync(api, "ProfitLoss", jeddah));
        Assert.Equal(0, await CountAsync(api, "VinInventory", jeddah)); // بيعت

        // مرتجع العقد: السيارة تعود متاحة لمعرض جدة
        var ret = await api.Post($"/carsalescontracts/{contractId}/return", new { reason = "عيب مصنعي" });
        Assert.True(ret.Status == 200, ret.Body?.ToJsonString());
        var back = (await api.Get($"/vehicles/{vehicleId}")).Data!;
        Assert.Equal("available", back["status"].S());
        Assert.Equal(jeddah, back["warehouseId"].S());

        // الدفاتر متوازنة بعد الدورتين
        var (debit, credit) = Totals(await api.Get("/reports/TrialBalance"));
        Assert.Equal(debit, credit);
    }

    [Fact]
    public async Task Direct_vehicle_invoices_buy_into_sell_from_and_return_to_their_showroom()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var riyadh = await WarehouseAsync(api, "RUH");
        var jeddah = await WarehouseAsync(api, "JED");
        var vinA = Client.NewVin('6');
        var vinB = Client.NewVin('7');

        object Line(string vin) => new { vin, brandNameAr = "هيونداي", modelNameAr = "سوناتا", trimNameAr = "GL", year = 2025,
            colorExterior = "أسود", colorInterior = "رمادي", vatMode = "standard_15", unitPrice = 60000m, discount = 0m };

        // شراء مباشر: سيارة لكل معرض
        foreach (var (vin, wh) in new[] { (vinA, riyadh), (vinB, jeddah) })
        {
            var p = await api.PostInvoice(new { kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier, status = "posted", warehouseId = wh, vehicleLines = new[] { Line(vin) } });
            Assert.True(p.Status == 201, p.Body?.ToJsonString());
        }
        var cars = (await api.Get("/vehicles")).Data!["items"]!.AsArray();
        var carA = cars.Single(v => v!["chassisNumber"].S() == vinA)!;
        var carB = cars.Single(v => v!["chassisNumber"].S() == vinB)!;
        Assert.Equal(riyadh, carA["warehouseId"].S());
        Assert.Equal(jeddah, carB["warehouseId"].S());

        object Sale(string vehicleId, string wh) => new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "posted", warehouseId = wh,
            vehicleLines = new[] { new { vehicleId, unitPrice = 75000m, discount = 0m, vatMode = "standard_15" } },
        };

        // بيع سيارة جدة من فاتورة الرياض مرفوض، ومن معرضها مقبول (بلا عميل: نقدية مبسطة)
        Assert.Equal(409, (await api.PostInvoice(Sale(carB["id"].S(), riyadh))).Status);
        var sale = await api.PostInvoice(Sale(carA["id"].S(), riyadh));
        Assert.True(sale.Status == 201, sale.Body?.ToJsonString());
        Assert.Equal("sold", (await api.Get($"/vehicles/{carA["id"].S()}")).Data!["status"].S());

        // مرتجع الفاتورة: تعود لمعرض الفاتورة
        var ret = await api.PostReturn(new { originalInvoiceId = sale.Data!["id"].S(), returnReason = "تراجع العميل" });
        Assert.True(ret.Status is 200 or 201, ret.Body?.ToJsonString());
        var back = (await api.Get($"/vehicles/{carA["id"].S()}")).Data!;
        Assert.Equal("available", back["status"].S());
        Assert.Equal(riyadh, back["warehouseId"].S());

        // تحويل سيارة جدة للرياض ثم بيعها من الرياض
        Assert.Equal(200, (await api.Post("/vehicles/transfer", new { vehicleIds = new[] { carB["id"].S() }, warehouseId = riyadh })).Status);
        var moved = await api.PostInvoice(Sale(carB["id"].S(), riyadh));
        Assert.True(moved.Status == 201, moved.Body?.ToJsonString());

        var (debit, credit) = Totals(await api.Get("/reports/TrialBalance"));
        Assert.Equal(debit, credit);
    }
}
