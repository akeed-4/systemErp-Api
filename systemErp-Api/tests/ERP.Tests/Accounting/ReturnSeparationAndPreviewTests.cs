using ERP.Core.Models.Accounting;
using ERP.Core.Models.Shared;
using ERP.Service.Data;
using ERP.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Tests;

/// <summary>
/// فصل المرتجعات بين الوحدتين ومعاينة المرتجع قبل إصداره:
/// شاشة مرتجعات المعارض تعرض مرتجعات السيارات وحدها (أسطر سيارات)، لا صفوف التجارة العامة القديمة في جدول المعارض،
/// ومرتجعات المحاسبة في جدولها لا تظهر فيها سيارات؛ والمعاينة تسعّر كما يسعّر الإصدار ولا تترك أثراً.
/// </summary>
[Collection("api")]
public class ReturnSeparationAndPreviewTests : TestBase
{
    public ReturnSeparationAndPreviewTests(ErpFactory f) : base(f) { }

    private static async Task<string> SeedCarAsync(Client api)
    {
        var car = await api.Post("/vehicles", new
        {
            chassisNumber = Client.NewVin('7'), brandNameAr = "تويوتا", modelNameAr = "كامري", year = 2025, condition = "new", purchasePrice = 80000, sellingPrice = 100000,
            vatMode = "standard_15", fuelType = "petrol", transmission = "automatic", colorExterior = "أبيض", colorInterior = "بيج", location = "المعرض",
        });
        Assert.Equal(201, car.Status);
        return car.Data!["id"].S();
    }

    private static object CarSale(params (string VehicleId, decimal Price)[] cars) => new
    {
        kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "posted",
        vehicleLines = cars.Select(c => new { vehicleId = c.VehicleId, unitPrice = c.Price, discount = 0, vatMode = "standard_15" }).ToArray(),
    };

    private static HashSet<string> Ids(Res list) => list.Data!["items"]!.AsArray().Select(i => i!["id"].S()).ToHashSet();

    /// <summary>مرتجع تجارة عامة قديم بقي في جدول فواتير المعارض من قبل فصل الجداول (لا ينقله الترحيل): بلا أسطر سيارات.</summary>
    private async Task<string> SeedLegacyTradeReturnAsync(Client api)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var legacy = new Invoice
        {
            TenantId = api.TenantId, Kind = InvoiceKind.SalesReturn, InvoiceType = InvoiceType.CreditNote, IsReturn = true,
            InvoiceNumber = "SRET-LEGACY-1", Status = "posted", PartyName = "عميل قديم", IssueDate = DateTime.Today,
            Subtotal = 100, VatTotal = 15, GrandTotal = 115,
        };
        legacy.Items.Add(new InvoiceItem { TenantId = api.TenantId, ItemName = "صنف عام", Quantity = 1, UnitPrice = 100, VatRate = 15, VatAmount = 15, TotalAfterVat = 115 });
        db.Add(legacy);
        await db.SaveChangesAsync();
        return legacy.Id.ToString();
    }

    [Fact]
    public async Task Showroom_returns_screen_lists_car_returns_only_and_accounting_returns_stay_isolated()
    {
        var api = await NewTenantAsync();

        // مرتجع بيع سيارة (جدول المعارض بأسطر سيارات)
        var car = await SeedCarAsync(api);
        var carSale = await api.PostInvoice(CarSale((car, 100000)));
        Assert.Equal(201, carSale.Status);
        var carReturn = await api.PostReturn(new { originalInvoiceId = carSale.Data!["id"].S(), returnReason = "عيب مصنعي" });
        Assert.Equal(200, carReturn.Status);
        var carReturnId = carReturn.Data!["id"].S();

        // مرتجع تجارة عامة (جدول SalesReturn) ومرتجع تجارة عامة قديم في جدول المعارض
        var product = await SeedProductAsync(api, stock: 10);
        var sale = await api.Post("/sales-invoices", new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", items = new[] { new { itemId = product, quantity = 2, unitPrice = 100, vatRate = 15 } } });
        Assert.Equal(201, sale.Status);
        var tradeReturn = await api.Post("/sales-returns/from-invoice", new { originalInvoiceId = sale.Data!["id"].S(), returnReason = "تالف", lines = new[] { new { itemId = product, quantity = 1 } } });
        Assert.Equal(200, tradeReturn.Status);
        var tradeReturnId = tradeReturn.Data!["id"].S();
        var legacyId = await SeedLegacyTradeReturnAsync(api);

        // جدول المعارض يحوي الصف القديم فعلاً (لذلك يلزم المرشّح)
        Assert.Contains(legacyId, Ids(await api.Get("/invoices?kind=sales_return&pageSize=500")));

        // ما تطلبه شاشة مرتجعات المعارض: مرتجعات بأسطر سيارات فقط
        var showroom = Ids(await api.Get("/invoices?kind=sales_return&hasVehicleLines=true&pageSize=500"));
        Assert.Contains(carReturnId, showroom);
        Assert.DoesNotContain(legacyId, showroom);
        Assert.DoesNotContain(tradeReturnId, showroom);

        // مرتجعات المحاسبة: جدولها وحده، بلا مرتجع السيارة ولا صف المعارض القديم
        var accounting = Ids(await api.Get("/sales-returns?pageSize=500"));
        Assert.Contains(tradeReturnId, accounting);
        Assert.DoesNotContain(carReturnId, accounting);
        Assert.DoesNotContain(legacyId, accounting);
        Assert.Equal(404, (await api.Get($"/sales-returns/{carReturnId}")).Status);
    }

    [Fact]
    public async Task Trade_return_preview_shows_remaining_quantities_prices_like_the_issued_return_and_has_no_side_effects()
    {
        var api = await NewTenantAsync();
        var a = await SeedProductAsync(api, "A", stock: 20);
        var b = await SeedProductAsync(api, "B", stock: 20);
        var sale = await api.Post("/sales-invoices", new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash", invoiceDiscount = 30,
            items = new object[]
            {
                new { itemId = a, quantity = 4, unitPrice = 100, discount = 20, vatRate = 15 },
                new { itemId = b, quantity = 3, unitPrice = 50, vatRate = 15 },
            },
        });
        Assert.Equal(201, sale.Status);
        var saleId = sale.Data!["id"].S();
        Assert.Equal(200, (await api.Post("/sales-returns/from-invoice", new { originalInvoiceId = saleId, returnReason = "أول", lines = new[] { new { itemId = a, quantity = 1 } } })).Status);

        var stockBefore = (await api.Get($"/products/{a}")).Data!["currentStock"].D();
        var countBefore = (await api.Get("/sales-returns")).Data!["totalCount"].D();
        var body = new { originalInvoiceId = saleId, lines = new object[] { new { itemId = a, quantity = 2 }, new { itemId = b, quantity = 1 } } };
        var preview = await api.Post("/sales-returns/return-preview", body);
        Assert.Equal(200, preview.Status);

        // الأصل كاملاً والمتبقي لكل صنف بعد المرتجع الأول
        Assert.Equal(saleId, preview.Data!["original"]!["id"].S());
        var lineA = preview.Data["lines"]!.AsArray().Single(l => l!["itemId"].S() == a.ToString())!;
        Assert.Equal(4, lineA["originalQuantity"].D());
        Assert.Equal(1, lineA["returnedQuantity"].D());
        Assert.Equal(3, lineA["remainingQuantity"].D());
        Assert.Equal(2, lineA["quantity"].D());

        // بلا أثر: لا مرتجع جديد ولا حركة مخزون
        Assert.Equal(countBefore, (await api.Get("/sales-returns")).Data!["totalCount"].D());
        Assert.Equal(stockBefore, (await api.Get($"/products/{a}")).Data!["currentStock"].D());

        // الإصدار بالاختيار نفسه يطابق المعاينة (الخصم النسبي وحصة خصم الفاتورة والضريبة)
        var issued = await api.Post("/sales-returns/from-invoice", new { originalInvoiceId = saleId, returnReason = "ثانٍ", lines = body.lines });
        Assert.Equal(200, issued.Status);
        var totals = preview.Data["totals"]!;
        Assert.Equal(issued.Data!["grandTotal"].D(), totals["grandTotal"].D());
        Assert.Equal(issued.Data["vatTotal"].D(), totals["vatTotal"].D());
        Assert.True(totals["invoiceDiscount"].D() > 0);

        // بلا اختيار: الصفوف بلا إجماليات، والكمية فوق المتبقي مرفوضة
        var empty = await api.Post("/sales-returns/return-preview", new { originalInvoiceId = saleId });
        Assert.Equal(200, empty.Status);
        Assert.Null(empty.Data!["totals"]);
        Assert.Equal(400, (await api.Post("/sales-returns/return-preview", new { originalInvoiceId = saleId, lines = new[] { new { itemId = a, quantity = 5 } } })).Status);
    }

    [Fact]
    public async Task Car_return_preview_marks_returned_cars_and_prices_like_the_issued_return()
    {
        var api = await NewTenantAsync();
        var first = await SeedCarAsync(api);
        var second = await SeedCarAsync(api);
        var sale = await api.PostInvoice(CarSale((first, 100000), (second, 120000)));
        Assert.Equal(201, sale.Status);
        var saleId = sale.Data!["id"].S();
        Assert.Equal(200, (await api.PostReturn(new { originalInvoiceId = saleId, returnReason = "عيب", vehicleIds = new[] { first } })).Status);

        var preview = await api.Post("/invoices/returns/preview", new { originalInvoiceId = saleId, vehicleIds = new[] { first, second } });
        Assert.Equal(200, preview.Status);
        Assert.Equal(new[] { first }, preview.Data!["returnedVehicleIds"]!.AsArray().Select(v => v!.GetValue<string>()).ToArray());
        Assert.Equal(2, preview.Data["original"]!["vehicleLines"]!.AsArray().Count);

        // المرتجعة من قبل لا تدخل الإجمالي؛ الإصدار للسيارة الباقية يطابق المعاينة
        var issued = await api.PostReturn(new { originalInvoiceId = saleId, returnReason = "عيب", vehicleIds = new[] { second } });
        Assert.Equal(200, issued.Status);
        Assert.Equal(issued.Data!["grandTotal"].D(), preview.Data["totals"]!["grandTotal"].D());
        Assert.Equal(issued.Data["vatTotal"].D(), preview.Data["totals"]!["vatTotal"].D());

        // فاتورة السيارات ليست على مسار مرتجعات المحاسبة
        Assert.Equal(404, (await api.Post("/sales-returns/return-preview", new { originalInvoiceId = saleId })).Status);
    }
}
