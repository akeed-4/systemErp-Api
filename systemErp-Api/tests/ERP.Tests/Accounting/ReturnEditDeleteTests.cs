using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// تعديل المرتجعات المرحّلة وحذفها قبل إرسالها للهيئة: التعديل يُعاد بناؤه من الأصل (الكميات ضمن المتبقي بلا المرتجع نفسه)
/// ويُرحَّل برقمه وقيده، والحذف يعكس قيده وحركاته فتعود كمياته متاحة للإرجاع؛ والدفاتر والمخزون متطابقان بعد كل عملية.
/// المرسل للهيئة يُرفض تعديله وحذفه، والتعديل العام (PUT {id}) لا يمر على المرتجع.
/// </summary>
[Collection("api")]
public class ReturnEditDeleteTests : TestBase
{
    public ReturnEditDeleteTests(ErpFactory f) : base(f) { }

    private static object Sale(Guid product, decimal qty) => new
    {
        kind = "sales", invoiceType = "simplified", paymentMethod = "cash",
        items = new[] { new { itemId = product, quantity = qty, unitPrice = 100, vatRate = 15 } },
    };

    private static object ReturnBody(string originalId, Guid product, decimal qty, string reason = "تالف") => new
    {
        originalInvoiceId = originalId, returnReason = reason, lines = new[] { new { itemId = product, quantity = qty } },
    };

    private static async Task<decimal> StockAsync(Client api, Guid product) => (await api.Get($"/products/{product}")).Data!["currentStock"].D();

    private static async Task AssertBooksReconciledAsync(Client api)
    {
        var (d, c) = Totals(await api.Get("/reports/TrialBalance"));
        Assert.Equal(d, c);
        var stock = (await api.Get("/reports/InventoryReconciliation")).Data!["rows"]!.AsArray().Single(r => r!["key"].S() == "items")!;
        Assert.Equal(0, stock["difference"].D());
    }

    [Fact]
    public async Task Posted_sales_return_is_edited_from_its_original_keeping_number_and_entry_and_respecting_remaining_quantity()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 10, cost: 40);
        var sale = await api.Post("/sales-invoices", Sale(product, 5));
        Assert.Equal(201, sale.Status);
        var saleId = sale.Data!["id"].S();

        var ret = await api.Post("/sales-returns/from-invoice", ReturnBody(saleId, product, 2));
        Assert.Equal(200, ret.Status);
        var id = ret.Data!["id"].S();
        var number = ret.Data["invoiceNumber"].S();
        Assert.Equal(7, await StockAsync(api, product));
        var entryBefore = (await api.Get($"/sales-returns/{id}/journal")).Data!["entryNumber"].S();

        // التعديل إلى 3: يُعاد بناؤه من الأصل بالرقم والقيد نفسيهما، ويعود المخزون بالفرق
        var edited = await api.Put($"/sales-returns/{id}/return", ReturnBody(saleId, product, 3, "تالف ومعدّل"));
        Assert.Equal(200, edited.Status);
        Assert.Equal(number, edited.Data!["invoiceNumber"].S());
        Assert.Equal("posted", edited.Data["status"].S());
        Assert.Equal(345, edited.Data["grandTotal"].D());
        Assert.Equal("تالف ومعدّل", edited.Data["returnReason"].S());
        Assert.Equal(8, await StockAsync(api, product));
        Assert.Equal(entryBefore, (await api.Get($"/sales-returns/{id}/journal")).Data!["entryNumber"].S());
        await AssertBooksReconciledAsync(api);

        // المتبقي يُحسب بلا المرتجع نفسه: مرتجع آخر بواحدة ثم تعديل الأول إلى 4 (5 - 1) مسموح وإلى 5 مرفوض
        Assert.Equal(200, (await api.Post("/sales-returns/from-invoice", ReturnBody(saleId, product, 1))).Status);
        Assert.Equal(200, (await api.Put($"/sales-returns/{id}/return", ReturnBody(saleId, product, 4))).Status);
        Assert.Equal(400, (await api.Put($"/sales-returns/{id}/return", ReturnBody(saleId, product, 5))).Status);
        Assert.Equal(10, await StockAsync(api, product));
        await AssertBooksReconciledAsync(api);

        // التعديل العام لا يمر على المرتجع: يُعدَّل من أصله فقط
        Assert.Equal(409, (await api.Put($"/sales-returns/{id}", new { kind = "sales_return", invoiceType = "simplified", paymentMethod = "cash",
            items = new[] { new { itemId = product, quantity = 1, unitPrice = 100, vatRate = 15 } } })).Status);
    }

    [Fact]
    public async Task Deleting_a_posted_sales_return_reverses_stock_and_books_and_frees_its_quantity()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 10, cost: 40);
        var sale = await api.Post("/sales-invoices", Sale(product, 5));
        var saleId = sale.Data!["id"].S();
        var ret = await api.Post("/sales-returns/from-invoice", ReturnBody(saleId, product, 5));
        Assert.Equal(200, ret.Status);
        Assert.Equal(10, await StockAsync(api, product));

        // الأصل الذي عليه مرتجع لا يُحذف، وبعد حذف المرتجع تعود كمياته للأصل
        Assert.Equal(409, (await api.Delete($"/sales-invoices/{saleId}")).Status);
        Assert.Equal(200, (await api.Delete($"/sales-returns/{ret.Data!["id"].S()}")).Status);
        Assert.Equal(404, (await api.Get($"/sales-returns/{ret.Data["id"].S()}")).Status);
        Assert.Equal(5, await StockAsync(api, product));
        await AssertBooksReconciledAsync(api);
        Assert.Equal(200, (await api.Post("/sales-returns/from-invoice", ReturnBody(saleId, product, 5))).Status);
        await AssertBooksReconciledAsync(api);
    }

    [Fact]
    public async Task Posted_purchase_return_is_edited_and_deleted_with_stock_and_books_reconciled()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 10, cost: 40);
        var (supplier, _) = await SeedSupplierAsync(api);
        var purchase = await api.Post("/purchase-invoices", new
        {
            kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier,
            items = new[] { new { itemId = product, quantity = 5, unitPrice = 50, vatRate = 15 } },
        });
        Assert.Equal(201, purchase.Status);
        var purchaseId = purchase.Data!["id"].S();
        Assert.Equal(15, await StockAsync(api, product));

        var ret = await api.Post("/purchase-returns/from-invoice", ReturnBody(purchaseId, product, 2));
        Assert.Equal(200, ret.Status);
        var id = ret.Data!["id"].S();
        Assert.Equal(13, await StockAsync(api, product));

        var edited = await api.Put($"/purchase-returns/{id}/return", ReturnBody(purchaseId, product, 1));
        Assert.Equal(200, edited.Status);
        Assert.Equal(ret.Data["invoiceNumber"].S(), edited.Data!["invoiceNumber"].S());
        Assert.Equal(14, await StockAsync(api, product));
        await AssertBooksReconciledAsync(api);

        Assert.Equal(200, (await api.Delete($"/purchase-returns/{id}")).Status);
        Assert.Equal(15, await StockAsync(api, product));
        await AssertBooksReconciledAsync(api);
    }

    [Fact]
    public async Task Return_sent_to_zatca_cannot_be_edited_or_deleted()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 10);
        var sale = await api.Post("/sales-invoices", Sale(product, 3));
        var saleId = sale.Data!["id"].S();
        var ret = await api.Post("/sales-returns/from-invoice", ReturnBody(saleId, product, 1));
        Assert.Equal(200, ret.Status);
        var id = ret.Data!["id"].S();
        await MarkSalesReportedToZatcaAsync(api);

        Assert.Equal(409, (await api.Put($"/sales-returns/{id}/return", ReturnBody(saleId, product, 2))).Status);
        Assert.Equal(409, (await api.Delete($"/sales-returns/{id}")).Status);
        Assert.Equal(8, await StockAsync(api, product));
        Assert.Equal(1, (await api.Get($"/sales-returns/{id}")).Data!["items"]!.AsArray().Sum(i => i!["quantity"].D()));
    }

    private static async Task<string> SeedCarAsync(Client api)
    {
        var car = await api.Post("/vehicles", new
        {
            chassisNumber = Client.NewVin('8'), brandNameAr = "تويوتا", modelNameAr = "كامري", year = 2025, condition = "new", purchasePrice = 80000, sellingPrice = 100000,
            vatMode = "standard_15", fuelType = "petrol", transmission = "automatic", colorExterior = "أبيض", colorInterior = "بيج", location = "المعرض",
        });
        Assert.Equal(201, car.Status);
        return car.Data!["id"].S();
    }

    private static async Task<string> VehicleStatusAsync(Client api, string id) => (await api.Get($"/vehicles/{id}")).Data!["status"].S();

    [Fact]
    public async Task Vehicle_sales_return_is_edited_to_another_car_and_deleted_restoring_vehicle_status()
    {
        var api = await NewTenantAsync();
        var carA = await SeedCarAsync(api);
        var carB = await SeedCarAsync(api);
        var sale = await api.PostInvoice(new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "posted",
            vehicleLines = new[] { carA, carB }.Select(v => new { vehicleId = v, unitPrice = 100000, discount = 0, vatMode = "standard_15" }).ToArray(),
        });
        Assert.Equal(201, sale.Status);
        var saleId = sale.Data!["id"].S();

        var ret = await api.PostReturn(new { originalInvoiceId = saleId, returnReason = "عيب", vehicleIds = new[] { carA } });
        Assert.Equal(200, ret.Status);
        var id = ret.Data!["id"].S();
        Assert.Equal("available", await VehicleStatusAsync(api, carA));

        // تعديل المرتجع إلى السيارة الأخرى: الأولى تعود مباعة والثانية متاحة، بالرقم نفسه
        var edited = await api.Put($"/invoices/returns/{id}", new { originalInvoiceId = saleId, returnReason = "عيب", vehicleIds = new[] { carB } });
        Assert.Equal(200, edited.Status);
        Assert.Equal(ret.Data["invoiceNumber"].S(), edited.Data!["invoiceNumber"].S());
        Assert.Equal("sold", await VehicleStatusAsync(api, carA));
        Assert.Equal("available", await VehicleStatusAsync(api, carB));
        var (d, c) = Totals(await api.Get("/reports/TrialBalance"));
        Assert.Equal(d, c);

        // الحذف يعيدها مباعة، ويمكن إرجاعها من جديد
        Assert.Equal(200, (await api.Delete($"/invoices/{id}")).Status);
        Assert.Equal("sold", await VehicleStatusAsync(api, carB));
        (d, c) = Totals(await api.Get("/reports/TrialBalance"));
        Assert.Equal(d, c);
        Assert.Equal(200, (await api.PostReturn(new { originalInvoiceId = saleId, returnReason = "عيب", vehicleIds = new[] { carB } })).Status);
    }
}
