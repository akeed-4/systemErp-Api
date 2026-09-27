using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>فواتير الشراء/البيع متعددة السيارات (InvoiceVehicleLine): إنشاء المركبات عند الترحيل، البيع، التحقق، والعكس.</summary>
[Collection("api")]
public class VehicleInvoiceTests : TestBase
{
    public VehicleInvoiceTests(ErpFactory f) : base(f) { }

    private static object Line(string? vin, decimal price, decimal discount = 0, string vatMode = "standard_15", string? tempRef = null) => new
    {
        vin, tempRef, brandNameAr = "تويوتا", modelNameAr = "كامري", trimNameAr = "GLE", year = 2025,
        colorExterior = "أبيض", colorInterior = "بيج", vatMode, unitPrice = price, discount,
    };

    private static object Purchase(Guid supplier, string status, params object[] lines) => new
    {
        kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier, status,
        inventoryAccountCode = "1142", vehicleLines = lines,
    };

    [Fact]
    public async Task Purchase_invoice_with_several_vehicle_lines_creates_vehicles_and_posts_once()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var v1 = Client.NewVin('1'); var v2 = Client.NewVin('2'); var v3 = Client.NewVin('3');

        var r = await api.Post("/invoices", Purchase(supplier, "posted",
            Line(v1, 100000), Line(v2, 90000, discount: 5000), Line(v3, 80000, vatMode: "exempt")));
        Assert.Equal(201, r.Status);
        Assert.Equal("posted", r.Data!["status"].S());
        Assert.Equal(3, r.Data["vehicleLines"]!.AsArray().Count);
        Assert.Equal(3, r.Data["items"]!.AsArray().Count);
        // الصافي: 100000 + 85000 + 80000 = 265000 ، الضريبة 15% على أول سطرين فقط = 15000 + 12750
        Assert.Equal(265000, r.Data["subtotal"].D());
        Assert.Equal(27750, r.Data["vatTotal"].D());
        Assert.Equal(292750, r.Data["grandTotal"].D());

        var vehicles = (await api.Get("/vehicles")).Data!["items"]!.AsArray();
        Assert.Equal(3, vehicles.Count);
        Assert.All(vehicles, v => { Assert.Equal("available", v!["status"].S()); Assert.Equal(r.Data["id"].S(), v["purchaseInvoiceId"].S()); });
        Assert.Contains(vehicles, v => v!["chassisNumber"].S() == v2 && v["totalCost"].D() == 85000);

        Assert.Equal(265000, (await api.Get("/accounts/by-code/1142")).Data!["balance"].D());
        var (d, c) = Totals(await api.Get("/reports/trial-balance")); Assert.Equal(d, c);

        // مركبة واردة من فاتورة لا تُحذف منفردة
        Assert.Equal(409, (await api.Delete($"/vehicles/{vehicles[0]!["id"].S()}")).Status);
        // حذف الفاتورة يعكس الأثر ويحذف المركبات
        Assert.Equal(200, (await api.Delete($"/invoices/{r.Data["id"].S()}")).Status);
        Assert.Equal(0, (await api.Get("/vehicles")).Data!["totalCount"].D());
        Assert.Equal(0, (await api.Get("/accounts/by-code/1142")).Data!["balance"].D());
    }

    [Fact]
    public async Task Purchase_invoice_validates_vins_and_rejects_duplicates()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var vin = Client.NewVin('4');

        Assert.Equal(400, (await api.Post("/invoices", Purchase(supplier, "posted", Line("BAD", 1000)))).Status);                  // صيغة
        Assert.Equal(400, (await api.Post("/invoices", Purchase(supplier, "posted", Line(vin, 1000), Line(vin, 1000)))).Status);   // تكرار بالمستند
        Assert.Equal(400, (await api.Post("/invoices", Purchase(supplier, "posted", Line(null, 1000)))).Status);                   // VIN مطلوب للترحيل
        Assert.Equal(400, (await api.Post("/invoices", Purchase(supplier, "posted", Line(vin, 1000, discount: 2000)))).Status);    // خصم أكبر من السعر
        Assert.Equal(0, (await api.Get("/invoices?hasVehicleLines=true")).Data!["totalCount"].D());

        Assert.Equal(201, (await api.Post("/invoices", Purchase(supplier, "posted", Line(vin, 1000)))).Status);
        Assert.Equal(409, (await api.Post("/invoices", Purchase(supplier, "posted", Line(vin, 1000)))).Status);                    // موجود بالمخزون
        Assert.Equal(1, (await api.Get("/invoices?hasVehicleLines=true")).Data!["totalCount"].D());
        Assert.Equal(0, (await api.Get("/invoices?hasVehicleLines=false")).Data!["totalCount"].D());
    }

    [Fact]
    public async Task Draft_purchase_allows_temp_references_and_posting_requires_real_vins()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var draft = await api.Post("/invoices", Purchase(supplier, "draft", Line(null, 50000, tempRef: "TEMP-0001"), Line(null, 50000, tempRef: "TEMP-0002")));
        Assert.Equal(201, draft.Status);
        Assert.Equal("draft", draft.Data!["status"].S());
        Assert.Equal(0, (await api.Get("/vehicles")).Data!["totalCount"].D()); // المسودة لا تنشئ مركبات
        var id = draft.Data["id"].S();

        Assert.Equal(400, (await api.Post($"/invoices/{id}/post", new { })).Status); // بلا VIN حقيقي

        var v1 = Client.NewVin('5'); var v2 = Client.NewVin('6');
        var upd = await api.Put($"/invoices/{id}", new
        {
            kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier, status = "posted", inventoryAccountCode = "1142",
            vehicleLines = new[] { Line(v1, 50000, tempRef: "TEMP-0001"), Line(v2, 50000, tempRef: "TEMP-0002") },
        });
        Assert.Equal(200, upd.Status);
        Assert.Equal("posted", upd.Data!["status"].S());
        Assert.Equal(2, (await api.Get("/vehicles")).Data!["totalCount"].D());
    }

    [Fact]
    public async Task Sales_invoice_sells_selected_vehicles_with_cogs_and_unposting_restores_them()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var p = await api.Post("/invoices", Purchase(supplier, "posted", Line(Client.NewVin('7'), 80000), Line(Client.NewVin('8'), 70000)));
        Assert.Equal(201, p.Status);
        var cars = (await api.Get("/vehicles")).Data!["items"]!.AsArray();
        var ids = cars.Select(c => c!["id"].S()).ToArray();

        object SaleLine(string vehicleId, decimal price) => new { vehicleId, unitPrice = price, discount = 0, vatMode = "standard_15" };
        object Sale(string status, params object[] lines) => new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status,
            revenueAccountCode = "412", cogsAccountCode = "512", inventoryAccountCode = "1142", vehicleLines = lines,
        };

        Assert.Equal(400, (await api.Post("/invoices", Sale("posted", SaleLine(ids[0], 100000), SaleLine(ids[0], 100000)))).Status); // نفس المركبة مرتين
        Assert.Equal(400, (await api.Post("/invoices", Sale("posted", SaleLine(Guid.NewGuid().ToString(), 1)))).Status);             // مركبة غير موجودة

        var s = await api.Post("/invoices", Sale("posted", SaleLine(ids[0], 100000), SaleLine(ids[1], 90000)));
        Assert.Equal(201, s.Status);
        Assert.Equal(190000, s.Data!["subtotal"].D());
        Assert.Equal(28500, s.Data["vatTotal"].D());
        Assert.Equal(150000, s.Data["totalCost"].D());
        Assert.All((await api.Get("/vehicles")).Data!["items"]!.AsArray(), v => Assert.Equal("sold", v!["status"].S()));
        // المركبة المباعة لا تُباع مرة ثانية
        Assert.Equal(400, (await api.Post("/invoices", Sale("posted", SaleLine(ids[0], 100000)))).Status);
        var (d, c) = Totals(await api.Get("/reports/trial-balance")); Assert.Equal(d, c);

        // إلغاء الترحيل عبر الحذف يعيد المركبات متاحة
        Assert.Equal(200, (await api.Delete($"/invoices/{s.Data["id"].S()}")).Status);
        Assert.All((await api.Get("/vehicles")).Data!["items"]!.AsArray(), v => Assert.Equal("available", v!["status"].S()));
        // فاتورة شراء لا تُلغى ترحيلها وسيارتها مباعة
        var sold = await api.Post("/invoices", Sale("posted", SaleLine(ids[0], 100000)));
        Assert.Equal(201, sold.Status);
        Assert.Equal(409, (await api.Delete($"/invoices/{p.Data!["id"].S()}")).Status);
    }

    private static decimal Side(System.Text.Json.Nodes.JsonNode journal, string code, string side)
        => journal["lines"]!.AsArray().Where(l => l!["accountCode"].S() == code).Sum(l => l![side].D());

    [Fact]
    public async Task Journal_preview_shows_inventory_and_cost_entries_without_saving_anything()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var journalsBefore = (await api.Get("/journalentries")).Data!["totalCount"].D();

        // شراء: مسودة بلا شاسيه تكفي للمعاينة — مدين مخزون السيارات والضريبة، دائن المورد
        var pv = await api.Post("/invoices/preview-journal", Purchase(supplier, "draft", Line(null, 100000, tempRef: "R1")));
        Assert.Equal(200, pv.Status);
        Assert.True(pv.Data!["isPreview"]!.GetValue<bool>());
        Assert.Equal(100000, Side(pv.Data, "1142", "debit"));
        Assert.Equal(15000, Side(pv.Data, "1131", "debit"));
        Assert.Equal(115000, pv.Data["totalCredit"].D());
        Assert.Equal(pv.Data["totalDebit"].D(), pv.Data["totalCredit"].D());

        // لا أثر: لا فواتير ولا مركبات ولا قيود
        Assert.Equal(0, (await api.Get("/invoices")).Data!["totalCount"].D());
        Assert.Equal(0, (await api.Get("/vehicles")).Data!["totalCount"].D());
        Assert.Equal(journalsBefore, (await api.Get("/journalentries")).Data!["totalCount"].D());

        // بيع: المعاينة تُظهر قيد تكلفة الإيرادات (مدين 512 / دائن 1142 بتكلفة السيارة) وتطابق القيد الفعلي
        Assert.Equal(201, (await api.Post("/invoices", Purchase(supplier, "posted", Line(Client.NewVin('9'), 80000)))).Status);
        var vid = (await api.Get("/vehicles")).Data!["items"]!.AsArray()[0]!["id"].S();
        var sale = new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "posted",
            vehicleLines = new[] { new { vehicleId = vid, unitPrice = 100000m, discount = 0m, vatMode = "standard_15" } },
        };
        var sp = await api.Post("/invoices/preview-journal", sale);
        Assert.Equal(200, sp.Status);
        Assert.Equal(80000, Side(sp.Data!, "512", "debit"));
        Assert.Equal(80000, Side(sp.Data!, "1142", "credit"));
        Assert.Equal(100000, Side(sp.Data!, "412", "credit"));
        Assert.Equal(15000, Side(sp.Data!, "213", "credit"));
        Assert.Equal(20000, sp.Data!["grossProfit"].D());
        Assert.Equal("available", (await api.Get($"/vehicles/{vid}")).Data!["status"].S()); // المعاينة لا تبيع المركبة

        var posted = await api.Post("/invoices", sale);
        Assert.Equal(201, posted.Status);
        var actual = await api.Get($"/invoices/{posted.Data!["id"].S()}/journal");
        Assert.Equal(200, actual.Status);
        Assert.False(actual.Data!["isPreview"]!.GetValue<bool>());
        foreach (var (code, side) in new[] { ("512", "debit"), ("1142", "credit"), ("412", "credit"), ("213", "credit") })
            Assert.Equal(Side(sp.Data!, code, side), Side(actual.Data!, code, side));
    }

    private static async Task<Res> SetVatModeAsync(Client api, string vehicleId, string vatMode)
    {
        var body = (await api.Get($"/vehicles/{vehicleId}")).Data!.AsObject();
        body["vatMode"] = vatMode;
        return await api.Put($"/vehicles/{vehicleId}", body);
    }

    [Fact]
    public async Task Margin_scheme_switch_on_the_vehicle_drives_vat_in_the_sales_invoice()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        // سيارة مستعملة مشتراة بدون ضريبة مدخلات (معفى) ← يُسمح بهامش الربح
        Assert.Equal(201, (await api.Post("/invoices", Purchase(supplier, "posted", Line(Client.NewVin('5'), 80000, vatMode: "exempt")))).Status);
        var id = (await api.Get("/vehicles")).Data!["items"]!.AsArray()[0]!["id"].S();

        var upd = await SetVatModeAsync(api, id, "profit_margin_15");
        Assert.Equal(200, upd.Status);
        Assert.Equal("profit_margin_15", upd.Data!["vatMode"].S());

        // العميل يرسل قياسي؛ الخادم يعتمد نمط المركبة
        var s = await api.Post("/invoices", new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "posted",
            revenueAccountCode = "412", cogsAccountCode = "512", inventoryAccountCode = "1142",
            vehicleLines = new[] { new { vehicleId = id, unitPrice = 100000m, discount = 0m, vatMode = "standard_15" } },
        });
        Assert.Equal(201, s.Status);
        // الهامش 20000 × 15/115 = 2608.70 مضمَّنة: الإيراد 97391.30 والإجمالي = سعر البيع
        Assert.Equal(2608.70m, s.Data!["vatTotal"].D());
        Assert.Equal(97391.30m, s.Data["subtotal"].D());
        Assert.Equal(100000, s.Data["grandTotal"].D());
        var line = s.Data["vehicleLines"]!.AsArray()[0]!;
        Assert.Equal("profit_margin_15", line["vatMode"].S());
        Assert.Equal(100000, line["totalAfterVat"].D());
        var (d, c) = Totals(await api.Get("/reports/trial-balance")); Assert.Equal(d, c);

        // لا يتغير النمط بعد البيع
        Assert.Equal(409, (await SetVatModeAsync(api, id, "standard_15")).Status);
    }

    [Fact]
    public async Task Margin_scheme_is_rejected_for_a_vehicle_bought_with_input_vat()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        Assert.Equal(201, (await api.Post("/invoices", Purchase(supplier, "posted", Line(Client.NewVin('6'), 80000)))).Status); // قياسي 15%
        var id = (await api.Get("/vehicles")).Data!["items"]!.AsArray()[0]!["id"].S();

        Assert.Equal(400, (await SetVatModeAsync(api, id, "profit_margin_15")).Status);
        Assert.Equal("standard_15", (await api.Get($"/vehicles/{id}")).Data!["vatMode"].S());
    }
}
