using ERP.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Tests;

/// <summary>
/// وحدات الصنف في فاتورة البيع (الهجرة ProductUnitsAndLineUnitFactor): الصنف بوحدة أساسية (قطعة) ووحدة بديلة (كرتون = 12 قطعة).
/// السطر يحفظ وحدته ومعاملها، والمخزون والتكلفة بالوحدة الأساسية، والمرتجع والحذف يعودان بالكمية الأساسية نفسها، والصنف بلا
/// وحدات بديلة يبقى كما كان. لا اتصال بالهيئة (الإرسال معطّل في المستندات).
/// </summary>
[Collection("api")]
public class SalesInvoiceUnitsTests : TestBase
{
    public SalesInvoiceUnitsTests(ErpFactory f) : base(f) { }

    private async Task<decimal> StockAsync(Guid item)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>();
        return await db.Set<ERP.Core.Models.Shared.Product>().IgnoreQueryFilters().Where(p => p.Id == item).Select(p => p.CurrentStock).FirstAsync();
    }

    private static async Task<decimal> BalanceAsync(Client api, string code) => (await api.Get($"/accounts/ByCode/{code}")).Data!["balance"].D();

    private static async Task AssertBooksReconciledAsync(Client api)
    {
        var (d, c) = Totals(await api.Get("/reports/TrialBalance"));
        Assert.Equal(d, c);
        var stock = (await api.Get("/reports/InventoryReconciliation")).Data!["rows"]!.AsArray().Single(r => r!["key"].S() == "items")!;
        Assert.True(stock["difference"].D() == 0, stock.ToJsonString());
    }

    /// <summary>صنف بـ 30 قطعة بتكلفة 5، ووحدة بديلة «كرتون» = 12 قطعة هي وحدة بيعه الافتراضية.</summary>
    private async Task<(Client Api, Guid Item)> CartonItemAsync(decimal stock = 30)
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api, stock: stock, cost: 5, price: 10);
        Assert.Equal(201, (await api.Post("/unitsofmeasure", new { code = "CTN", nameAr = "كرتون", nameEn = "Carton", unitType = "count", conversionFactor = 12 })).Status);
        var current = (await api.Get($"/products/{item}")).Data!;
        var put = await api.Put($"/products/{item}", new
        {
            sku = current["sku"].S(), nameAr = current["nameAr"].S(), category = "C1", unit = "PCS", sellingPrice = 10, vatRate = 15,
            salesUnit = "CTN", units = new[] { new { unitCode = "CTN", factor = 12 } },
        });
        Assert.True(put.Status == 200, put.Body?.ToJsonString());
        return (api, item);
    }

    private static object Sale(Guid item, string unit, decimal quantity, decimal price) => new
    {
        kind = "sales", invoiceType = "simplified", paymentMethod = "cash", sendToZatca = false,
        items = new[] { new { itemId = item, unit, quantity, unitPrice = price, vatRate = 15 } },
    };

    [Fact]
    public async Task Product_keeps_its_linked_units_and_default_sales_unit()
    {
        var (api, item) = await CartonItemAsync();
        var p = (await api.Get($"/products/{item}")).Data!;
        Assert.Equal("CTN", p["salesUnit"].S());
        var unit = Assert.Single(p["units"]!.AsArray());
        Assert.Equal("CTN", unit!["unitCode"].S());
        Assert.Equal(12m, unit["factor"].D());
        // القائمة تحملها أيضاً (تقرؤها شاشة الفاتورة عند اختيار الصنف)
        var listed = (await api.Get("/products")).Data!["items"]!.AsArray().Single(x => x!["id"].G() == item)!;
        Assert.Single(listed["units"]!.AsArray());
    }

    [Fact]
    public async Task Product_rejects_invalid_units()
    {
        var (api, item) = await CartonItemAsync();
        async Task<int> PutAsync(object units, string? salesUnit = null) => (await api.Put($"/products/{item}", new
        {
            sku = "P1", nameAr = "منتج P1", category = "C1", unit = "PCS", sellingPrice = 10, vatRate = 15, salesUnit, units,
        })).Status;
        Assert.Equal(400, await PutAsync(new[] { new { unitCode = "PCS", factor = 1 } }));                                     // الأساسية نفسها
        Assert.Equal(400, await PutAsync(new[] { new { unitCode = "CTN", factor = 12 }, new { unitCode = "CTN", factor = 6 } })); // مكررة
        Assert.Equal(400, await PutAsync(new[] { new { unitCode = "CTN", factor = 0 } }));                                     // معامل صفر
        Assert.Equal(400, await PutAsync(new[] { new { unitCode = "BOX", factor = 6 } }));                                     // ليست في الدليل
        Assert.Equal(400, await PutAsync(new[] { new { unitCode = "CTN", factor = 12 } }, salesUnit: "BOX"));                  // وحدة بيع غير مرتبطة
    }

    [Fact]
    public async Task Sale_in_an_alternative_unit_issues_base_quantity_and_costs_it_correctly()
    {
        var (api, item) = await CartonItemAsync();
        var mainId = (await api.Get("/warehouses")).Data!["items"]!.AsArray().Single()!["id"].G();
        var cogs = (await api.Get($"/warehouses/{mainId}")).Data!["cogsAccountCode"].S();
        var cogsBefore = await BalanceAsync(api, cogs);

        var sale = await api.PostInvoice(Sale(item, "CTN", 2, 120));
        Assert.True(sale.Status == 201, sale.Body?.ToJsonString());

        Assert.Equal(6m, await StockAsync(item)); // 30 − 2 × 12
        var line = (await api.Get(await api.InvoicePath(sale.Data!["id"].S()))).Data!["items"]!.AsArray().Single()!;
        Assert.Equal("CTN", line["unit"].S());
        Assert.Equal(12m, line["unitFactor"].D());
        Assert.Equal(2m, line["quantity"].D());
        Assert.Equal(60m, line["unitCost"].D());        // تكلفة الكرتون = 12 × 5
        Assert.Equal(240m, line["totalBeforeVat"].D()); // الأسعار والضريبة على وحدة السطر كما هي
        Assert.Equal(120m, Math.Abs(await BalanceAsync(api, cogs) - cogsBefore)); // تكلفة المبيعات = 24 قطعة × 5
        await AssertBooksReconciledAsync(api);
    }

    [Fact]
    public async Task Sale_accepts_the_unit_by_its_catalogue_name_and_empty_unit_is_the_base_unit()
    {
        var (api, item) = await CartonItemAsync();
        Assert.Equal(201, (await api.PostInvoice(Sale(item, "كرتون", 1, 120))).Status);
        Assert.Equal(18m, await StockAsync(item));
        var plain = await api.PostInvoice(Sale(item, "", 3, 10));
        Assert.Equal(201, plain.Status);
        Assert.Equal(15m, await StockAsync(item));
        var line = (await api.Get(await api.InvoicePath(plain.Data!["id"].S()))).Data!["items"]!.AsArray().Single()!;
        Assert.Equal("PCS", line["unit"].S());
        Assert.Equal(1m, line["unitFactor"].D());
    }

    [Fact]
    public async Task Sale_rejects_a_unit_not_linked_to_the_item()
    {
        var (api, item) = await CartonItemAsync();
        Assert.Equal(201, (await api.Post("/unitsofmeasure", new { code = "DZN", nameAr = "درزن", unitType = "count", conversionFactor = 12 })).Status);
        var sale = await api.PostInvoice(Sale(item, "DZN", 1, 100));
        Assert.Equal(400, sale.Status);
        Assert.Equal(30m, await StockAsync(item));
    }

    [Fact]
    public async Task Sale_beyond_stock_in_base_units_is_rejected()
    {
        var (api, item) = await CartonItemAsync(stock: 20);
        // كرتونان = 24 قطعة والمتوفر 20
        var sale = await api.PostInvoice(Sale(item, "CTN", 2, 120));
        Assert.Equal(409, sale.Status);
        Assert.Equal(20m, await StockAsync(item));
    }

    [Fact]
    public async Task Return_and_delete_restore_the_base_quantity()
    {
        var (api, item) = await CartonItemAsync();
        var sale = await api.PostInvoice(Sale(item, "CTN", 2, 120));
        Assert.Equal(201, sale.Status);
        var saleId = sale.Data!["id"].S();

        var ret = await api.PostReturn(new { originalInvoiceId = saleId, returnReason = "تالف", lines = new[] { new { itemId = item, quantity = 1 } } });
        Assert.True(ret.Status == 200, ret.Body?.ToJsonString());
        Assert.Equal(18m, await StockAsync(item)); // 6 + كرتون واحد
        var retLine = (await api.Get(await api.InvoicePath(ret.Data!["id"].S()))).Data!["items"]!.AsArray().Single()!;
        Assert.Equal("CTN", retLine["unit"].S());
        Assert.Equal(12m, retLine["unitFactor"].D());
        Assert.Equal(60m, retLine["unitCost"].D());
        await AssertBooksReconciledAsync(api);

        Assert.Equal(200, (await api.Delete(await api.InvoicePath(ret.Data["id"].S()))).Status);
        Assert.Equal(6m, await StockAsync(item));
        Assert.Equal(200, (await api.Delete(await api.InvoicePath(saleId))).Status);
        Assert.Equal(30m, await StockAsync(item));
        await AssertBooksReconciledAsync(api);
    }

    [Fact]
    public async Task Item_sold_in_two_units_returns_per_source_line()
    {
        var (api, item) = await CartonItemAsync();
        var sale = await api.PostInvoice(new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash", sendToZatca = false,
            items = new object[]
            {
                new { itemId = item, unit = "CTN", quantity = 1, unitPrice = 120, vatRate = 15 },
                new { itemId = item, unit = "PCS", quantity = 5, unitPrice = 10, vatRate = 15 },
            },
        });
        Assert.Equal(201, sale.Status);
        Assert.Equal(13m, await StockAsync(item)); // 30 − 12 − 5
        var saleId = sale.Data!["id"].S();
        var pieces = (await api.Get(await api.InvoicePath(saleId))).Data!["items"]!.AsArray().Single(l => l!["unit"].S() == "PCS")!["id"].G();

        var ret = await api.PostReturn(new { originalInvoiceId = saleId, returnReason = "x",
            lines = new[] { new { itemId = item, quantity = 2, originalInvoiceItemId = pieces } } });
        Assert.True(ret.Status == 200, ret.Body?.ToJsonString());
        Assert.Equal(15m, await StockAsync(item)); // قطعتان لا كرتونان
        await AssertBooksReconciledAsync(api);
    }

    [Fact]
    public async Task Item_without_alternative_units_keeps_the_legacy_behaviour()
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api, stock: 10, cost: 5, price: 10);
        // نص وحدة حر كما كانت الشاشات القديمة ترسله: يُحفظ كما هو بمعامل 1
        var sale = await api.PostInvoice(Sale(item, "حبة", 4, 10));
        Assert.Equal(201, sale.Status);
        Assert.Equal(6m, await StockAsync(item));
        var line = (await api.Get(await api.InvoicePath(sale.Data!["id"].S()))).Data!["items"]!.AsArray().Single()!;
        Assert.Equal("حبة", line["unit"].S());
        Assert.Equal(1m, line["unitFactor"].D());
    }
}
