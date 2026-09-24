using ERP.Tests.Infrastructure;

namespace ERP.Tests;

[Collection("api")]
public class CarShowroomTests : TestBase
{
    public CarShowroomTests(ErpFactory f) : base(f) { }

    private async Task<Guid> ReceivedOrderAsync(Client api, Guid supplier, int qty = 2, decimal unitPrice = 80000, string[]? vins = null)
    {
        var order = await api.Post("/carprocurementorders", new
        {
            supplierId = supplier, paymentType = "credit", creditDays = 30, currency = "SAR", exchangeRate = 1, date = DateTime.UtcNow,
            customsDutyFee = 2000, portStorageFee = 1000,
            items = new[] { new { brandName = "تويوتا", modelName = "كامري", trimName = "GLE", year = 2025, quantity = qty, unitPrice, color = "أبيض" } },
        });
        Assert.Equal(201, order.Status);
        var id = order.Data!["id"].S();
        foreach (var stage in new[] { "requisition_approved", "rfq", "rfq_approved", "purchase_order" })
            Assert.Equal(200, (await api.Post($"/carprocurementorders/{id}/advance-stage", new { targetStage = stage })).Status);
        vins ??= Enumerable.Range(0, qty).Select(_ => Client.NewVin('1')).ToArray();
        var rec = await api.Post($"/carprocurementorders/{id}/advance-stage", new
        {
            targetStage = "vin_received", pdiInspectionPassed = true, warehouseLocation = "المعرض",
            vins = vins.Select(v => new { vin = v, engineNumber = "E-" + v[^4..] }).ToArray(),
        });
        Assert.Equal(200, rec.Status);
        return Guid.Parse(id);
    }

    [Fact]
    public async Task Master_data_is_hierarchical_and_protected_from_orphaning()
    {
        var api = await NewTenantAsync();
        var brand = await api.Post("/carbrands", new { nameAr = "تويوتا", nameEn = "Toyota", country = "اليابان" });
        Assert.Equal(409, (await api.Post("/carbrands", new { nameAr = "تويوتا", nameEn = "x", country = "x" })).Status);
        var model = await api.Post("/carmodels", new { brandId = brand.Data!["id"].S(), nameAr = "كامري" });
        var trim = await api.Post("/cartrims", new { modelId = model.Data!["id"].S(), nameAr = "GLE" });
        var year = await api.Post("/caryears", new { trimId = trim.Data!["id"].S(), year = 2025 });
        Assert.Equal(201, year.Status);
        Assert.Equal(409, (await api.Post("/caryears", new { trimId = trim.Data["id"].S(), year = 2025 })).Status);
        Assert.Equal(400, (await api.Post("/carmodels", new { brandId = Guid.NewGuid(), nameAr = "x" })).Status);
        Assert.Equal(409, (await api.Delete($"/carbrands/{brand.Data["id"].S()}")).Status);
        Assert.Equal(409, (await api.Delete($"/carmodels/{model.Data["id"].S()}")).Status);
        Assert.Equal(409, (await api.Delete($"/cartrims/{trim.Data["id"].S()}")).Status);
        Assert.Equal(200, (await api.Delete($"/caryears/{year.Data!["id"].S()}")).Status);
    }

    [Theory]
    [InlineData("standard_15", 80000, 100000, 15000, 115000)]
    [InlineData("profit_margin_15", 80000, 100000, 3000, 103000)]
    [InlineData("exempt", 80000, 100000, 0, 100000)]
    [InlineData("profit_margin_15", 100000, 90000, 0, 90000)] // بيع بخسارة: لا ضريبة على هامش سالب
    public async Task Vat_calculation_matches_the_frontend_rules(string mode, decimal cost, decimal price, decimal vat, decimal total)
    {
        var api = await NewTenantAsync();
        var r = await api.Post("/vehicles/calculate-vat", new { costPrice = cost, sellingPrice = price, mode });
        Assert.Equal(vat, r.Data!["vatAmount"].D()); Assert.Equal(total, r.Data["priceWithVat"].D());
    }

    [Fact]
    public async Task Vehicle_validates_vin_and_computes_cost_and_vat_on_the_server()
    {
        var api = await NewTenantAsync();
        Assert.Equal(400, (await api.Post("/vehicles", new { chassisNumber = "SHORT", brandNameAr = "x", modelNameAr = "y", year = 2024 })).Status);
        Assert.Equal(400, (await api.Post("/vehicles", new { chassisNumber = "1HGCM82633A00OOOO", brandNameAr = "x", modelNameAr = "y", year = 2024 })).Status); // يحوي O
        var vin = Client.NewVin('3');
        var v = await api.Post("/vehicles", new { chassisNumber = vin, brandNameAr = "هوندا", modelNameAr = "أكورد", year = 2020, condition = "used", mileageKm = 60000,
            purchasePrice = 50000, additionalCosts = 1000, preparationCost = 500, sellingPrice = 60000, vatMode = "profit_margin_15", totalCost = 1, vatAmount = 99999 });
        Assert.Equal(201, v.Status);
        Assert.Equal(51500, v.Data!["totalCost"].D());
        Assert.Equal(1275, v.Data["vatAmount"].D()); // 15% × (60000 - 51500)
        Assert.Equal("available", v.Data["status"].S());
        Assert.Equal(409, (await api.Post("/vehicles", new { chassisNumber = vin, brandNameAr = "x", modelNameAr = "y", year = 2024 })).Status);
    }

    [Fact]
    public async Task Procurement_follows_the_seven_stages_creates_vehicles_and_posts_to_vehicle_inventory()
    {
        var api = await NewTenantAsync();
        var (supplier, supplierAccount) = await SeedSupplierAsync(api, "وكيل تويوتا");
        var order = await api.Post("/carprocurementorders", new
        {
            supplierId = supplier, paymentType = "credit", creditDays = 30, currency = "SAR", exchangeRate = 1, date = DateTime.UtcNow, customsDutyFee = 2000, portStorageFee = 1000,
            items = new[] { new { brandName = "تويوتا", modelName = "كامري", year = 2025, quantity = 2, unitPrice = 80000 } },
        });
        var id = order.Data!["id"].S();
        Assert.Equal(184000, order.Data["grandTotal"].D());
        Assert.Equal("requisition", order.Data["stage"].S());

        // لا تخطي للمراحل
        Assert.Equal(409, (await api.Post($"/carprocurementorders/{id}/advance-stage", new { targetStage = "purchase_order" })).Status);
        foreach (var s in new[] { "requisition_approved", "rfq", "rfq_approved", "purchase_order" })
            Assert.Equal(s, (await api.Post($"/carprocurementorders/{id}/advance-stage", new { targetStage = s })).Data!["stage"].S());

        // استلام: عدد وصيغة الشواسيهات ملزمان
        var bad = await api.Post($"/carprocurementorders/{id}/advance-stage", new { targetStage = "vin_received", pdiInspectionPassed = true, vins = new[] { new { vin = "BAD" }, new { vin = "BAD2" } } });
        Assert.Equal(400, bad.Status);
        var one = await api.Post($"/carprocurementorders/{id}/advance-stage", new { targetStage = "vin_received", pdiInspectionPassed = true, vins = new[] { new { vin = Client.NewVin('1') } } });
        Assert.Equal(400, one.Status); // ناقص
        var notPassed = await api.Post($"/carprocurementorders/{id}/advance-stage", new { targetStage = "vin_received", vins = new[] { new { vin = Client.NewVin('1') }, new { vin = Client.NewVin('2') } } });
        Assert.Equal(400, notPassed.Status); // يلزم تأكيد PDI

        var v1 = Client.NewVin('1'); var v2 = Client.NewVin('2');
        var rec = await api.Post($"/carprocurementorders/{id}/advance-stage", new { targetStage = "vin_received", pdiInspectionPassed = true, warehouseLocation = "المعرض", vins = new[] { new { vin = v1 }, new { vin = v2 } } });
        Assert.Equal("received", rec.Data!["status"].S());
        Assert.Equal(2, rec.Data["receivedVinList"]!.AsArray().Count);
        var vehicles = (await api.Get("/vehicles")).Data!["items"]!.AsArray();
        Assert.Equal(2, vehicles.Count);
        Assert.All(vehicles, v => { Assert.Equal("available", v!["status"].S()); Assert.Equal(81500, v["totalCost"].D()); }); // 80000 + (2000+1000)/2

        var inv = await api.Post($"/carprocurementorders/{id}/advance-stage", new { targetStage = "invoiced", supplierInvoiceNumber = "S-100" });
        Assert.Equal(200, inv.Status);
        Assert.Equal("invoiced", inv.Data!["status"].S()); Assert.Equal("S-100", inv.Data["matchedInvoiceNumber"].S());
        Assert.Equal(163000, (await api.Get("/accounts/by-code/1142")).Data!["balance"].D());  // مخزون السيارات: 160000 + 3000 تكاليف محمّلة
        Assert.Equal(3000, (await api.Get("/accounts/by-code/212")).Data!["balance"].D());     // مستحقات جمارك وموانئ
        Assert.Equal(184000, (await api.Get($"/suppliers/{supplier}")).Data!["currentBalance"].D());
        Assert.Equal(409, (await api.Post($"/carprocurementorders/{id}/advance-stage", new { targetStage = "invoiced" })).Status);
        Assert.Equal(409, (await api.Delete($"/carprocurementorders/{id}")).Status);
        var (d, c) = Totals(await api.Get("/reports/trial-balance")); Assert.Equal(d, c);
    }

    [Fact]
    public async Task Failed_pdi_rejects_the_order_and_creates_no_vehicles()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var order = await api.Post("/carprocurementorders", new { supplierId = supplier, paymentType = "cash", currency = "SAR", exchangeRate = 1, date = DateTime.UtcNow,
            items = new[] { new { brandName = "تويوتا", modelName = "كامري", year = 2025, quantity = 1, unitPrice = 80000 } } });
        var id = order.Data!["id"].S();
        foreach (var s in new[] { "requisition_approved", "rfq", "rfq_approved", "purchase_order" }) await api.Post($"/carprocurementorders/{id}/advance-stage", new { targetStage = s });
        var r = await api.Post($"/carprocurementorders/{id}/advance-stage", new { targetStage = "vin_received", pdiInspectionPassed = false, rejectionReason = "ضرر في الهيكل", vins = Array.Empty<object>() });
        Assert.Equal("rejected", r.Data!["status"].S());
        Assert.Equal(0, (await api.Get("/vehicles")).Data!["totalCount"].D());
    }

    [Fact]
    public async Task Sales_contract_cycle_reserves_delivers_invoices_and_posts_car_accounts()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        await ReceivedOrderAsync(api, supplier, qty: 1);
        var invoicedOrder = (await api.Get("/carprocurementorders")).Data!["items"]![0]!["id"].S();
        await api.Post($"/carprocurementorders/{invoicedOrder}/advance-stage", new { targetStage = "invoiced" });

        var vehicle = (await api.Get("/vehicles")).Data!["items"]![0]!;
        var vid = vehicle["id"].S();
        await api.Put($"/vehicles/{vid}", new { chassisNumber = vehicle["chassisNumber"].S(), brandNameAr = "تويوتا", modelNameAr = "كامري", year = 2025, condition = "new", purchasePrice = 80000, additionalCosts = 3000,
            sellingPrice = 100000, vatMode = "standard_15", minSellingPrice = 90000, fuelType = "petrol", transmission = "automatic", colorExterior = "أبيض", colorInterior = "بيج", location = "المعرض" });

        var contract = await api.Post("/carsalescontracts", new { cycleType = "individual", date = DateTime.UtcNow, buyerName = "مشتري", buyerNationalIdOrCr = "1010101010", buyerPhone = "0550000000",
            vehicleId = vid, sellingPrice = 100000, vatMode = "standard_15", paymentMethod = "cash", condition = "new", costPrice = 1, totalWithVat = 5 }); // أرقام العميل تُتجاهل
        Assert.Equal(201, contract.Status);
        Assert.Equal(83000, contract.Data!["costPrice"].D()); Assert.Equal(115000, contract.Data["totalWithVat"].D());
        var id = contract.Data["id"].S();

        Assert.Equal(400, (await api.Post("/carsalescontracts", new { cycleType = "individual", buyerName = "م", buyerNationalIdOrCr = "1", buyerPhone = "1", vehicleId = vid, sellingPrice = 100000, discountAmount = 20000,
            vatMode = "standard_15", paymentMethod = "cash", condition = "new" })).Status); // أقل من الحد الأدنى
        Assert.Equal(409, (await api.Post($"/carsalescontracts/{id}/advance-status", new { targetStatus = "invoiced" })).Status);

        Assert.Equal("approved", (await api.Post($"/carsalescontracts/{id}/advance-status", new { targetStatus = "approved" })).Data!["status"].S());
        Assert.Equal("reserved", (await api.Get($"/vehicles/{vid}")).Data!["status"].S());
        // مركبة محجوزة لا تُباع في عقد ثانٍ
        Assert.Equal(409, (await api.Post("/carsalescontracts", new { cycleType = "individual", buyerName = "آخر", buyerNationalIdOrCr = "2", buyerPhone = "2", vehicleId = vid, sellingPrice = 100000, vatMode = "standard_15", paymentMethod = "cash", condition = "new" })).Status);
        Assert.Equal("allocated", (await api.Post($"/carsalescontracts/{id}/advance-status", new { targetStatus = "allocated" })).Data!["status"].S());
        Assert.Equal(400, (await api.Post($"/carsalescontracts/{id}/advance-status", new { targetStatus = "delivered" })).Status); // بلا محضر
        Assert.Equal(200, (await api.Post($"/carsalescontracts/{id}/advance-status", new { targetStatus = "delivered", handoverProtocolNumber = "H-1", handoverSignee = "مشتري", handoverSigneeNationalId = "1010101010" })).Status);

        var done = await api.Post($"/carsalescontracts/{id}/advance-status", new { targetStatus = "invoiced" });
        Assert.Equal(200, done.Status);
        Assert.Equal("sold", (await api.Get($"/vehicles/{vid}")).Data!["status"].S());
        var invoice = (await api.Get($"/invoices/{done.Data!["invoiceId"].S()}")).Data!;
        Assert.Equal(115000, invoice["grandTotal"].D()); Assert.Equal(15000, invoice["vatTotal"].D()); Assert.Equal(83000, invoice["totalCost"].D());

        var accounts = (await api.Get("/accounts?pageSize=500")).Data!["items"]!.AsArray();
        decimal Bal(string code) => accounts.First(a => a!["code"].S() == code)!["balance"].D();
        Assert.Equal(100000, Bal("412")); Assert.Equal(83000, Bal("512")); Assert.Equal(0, Bal("1142"));

        var pl = (await api.Get("/reports/car/profit-loss")).Data!.AsArray();
        Assert.Single(pl); Assert.Equal(17000, pl[0]!["profitAmount"].D());
        Assert.Equal(409, (await api.Post($"/carsalescontracts/{id}/cancel", new { reason = "x" })).Status);
        var (d, c) = Totals(await api.Get("/reports/trial-balance")); Assert.Equal(d, c);
    }

    [Fact]
    public async Task Used_car_margin_scheme_charges_vat_on_the_margin_only()
    {
        var api = await NewTenantAsync();
        var car = await api.Post("/vehicles", new { chassisNumber = Client.NewVin('3'), brandNameAr = "هوندا", modelNameAr = "أكورد", year = 2020, condition = "used", mileageKm = 60000,
            purchasePrice = 50000, sellingPrice = 60000, vatMode = "profit_margin_15", fuelType = "petrol", transmission = "automatic", colorExterior = "أسود", colorInterior = "بيج", location = "المعرض" });
        var vid = car.Data!["id"].S();
        var c = await api.Post("/carsalescontracts", new { cycleType = "individual", buyerName = "م", buyerNationalIdOrCr = "2", buyerPhone = "2", vehicleId = vid, sellingPrice = 60000, vatMode = "profit_margin_15", paymentMethod = "bank_transfer", condition = "used" });
        var id = c.Data!["id"].S();
        foreach (var s in new[] { "approved", "allocated" }) await api.Post($"/carsalescontracts/{id}/advance-status", new { targetStatus = s });
        await api.Post($"/carsalescontracts/{id}/advance-status", new { targetStatus = "delivered", handoverProtocolNumber = "H", handoverSignee = "م", handoverSigneeNationalId = "2" });
        var done = await api.Post($"/carsalescontracts/{id}/advance-status", new { targetStatus = "invoiced" });
        var inv = (await api.Get($"/invoices/{done.Data!["invoiceId"].S()}")).Data!;
        Assert.Equal(1500, inv["vatTotal"].D()); Assert.Equal(61500, inv["grandTotal"].D());
        var report = (await api.Get("/reports/car/zatca-margin-tax")).Data!.AsArray();
        Assert.Single(report); Assert.Equal(1500, report[0]!["vatAmount15Percent"].D());
    }

    [Fact]
    public async Task Cancelling_a_contract_releases_the_reservation()
    {
        var api = await NewTenantAsync();
        var car = await api.Post("/vehicles", new { chassisNumber = Client.NewVin('4'), brandNameAr = "تويوتا", modelNameAr = "يارس", year = 2024, condition = "new", purchasePrice = 40000, sellingPrice = 50000,
            vatMode = "standard_15", fuelType = "petrol", transmission = "automatic", colorExterior = "أبيض", colorInterior = "أسود", location = "المعرض" });
        var vid = car.Data!["id"].S();
        var c = await api.Post("/carsalescontracts", new { cycleType = "individual", buyerName = "م", buyerNationalIdOrCr = "1", buyerPhone = "1", vehicleId = vid, sellingPrice = 50000, vatMode = "standard_15", paymentMethod = "cash", condition = "new" });
        var id = c.Data!["id"].S();
        await api.Post($"/carsalescontracts/{id}/advance-status", new { targetStatus = "approved" });
        Assert.Equal("reserved", (await api.Get($"/vehicles/{vid}")).Data!["status"].S());
        Assert.Equal(200, (await api.Post($"/carsalescontracts/{id}/cancel", new { reason = "تراجع العميل" })).Status);
        Assert.Equal("available", (await api.Get($"/vehicles/{vid}")).Data!["status"].S());
        Assert.Equal(200, (await api.Delete($"/carsalescontracts/{id}")).Status);
    }

    [Fact]
    public async Task Financed_sale_splits_down_payment_from_receivable()
    {
        var api = await NewTenantAsync();
        var (customer, _) = await SeedCustomerAsync(api, creditLimit: 500000);
        var car = await api.Post("/vehicles", new { chassisNumber = Client.NewVin('5'), brandNameAr = "تويوتا", modelNameAr = "لاندكروزر", year = 2025, condition = "new", purchasePrice = 200000, sellingPrice = 250000,
            vatMode = "standard_15", fuelType = "petrol", transmission = "automatic", colorExterior = "أبيض", colorInterior = "بيج", location = "المعرض" });
        var vid = car.Data!["id"].S();
        var c = await api.Post("/carsalescontracts", new { cycleType = "bank_lease", buyerName = "م", buyerNationalIdOrCr = "1", buyerPhone = "1", customerId = customer, vehicleId = vid, sellingPrice = 250000, vatMode = "standard_15",
            paymentMethod = "bank_finance", condition = "new", downPaymentAmount = 50000, financingBankName = "بنك", financedAmount = 237500 });
        var id = c.Data!["id"].S();
        Assert.Equal(287500, c.Data["totalWithVat"].D());
        foreach (var s in new[] { "approved", "allocated" }) Assert.Equal(200, (await api.Post($"/carsalescontracts/{id}/advance-status", new { targetStatus = s })).Status);
        await api.Post($"/carsalescontracts/{id}/advance-status", new { targetStatus = "delivered", handoverProtocolNumber = "H", handoverSignee = "م", handoverSigneeNationalId = "1" });
        Assert.Equal(200, (await api.Post($"/carsalescontracts/{id}/advance-status", new { targetStatus = "invoiced" })).Status);
        Assert.Equal(237500, (await api.Get($"/customers/{customer}")).Data!["currentBalance"].D()); // المتبقي بعد الدفعة المقدمة
        var installments = (await api.Get("/reports/car/installments-receivable")).Data!.AsArray();
        Assert.Single(installments);
    }
}
