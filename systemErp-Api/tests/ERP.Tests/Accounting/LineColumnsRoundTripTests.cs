using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// أعمدة جدول الأصناف الموحد في الواجهة: كل مستند يحفظ مركز تكلفة السطر وموقعه («موقع الصرف») ويعيدهما في القراءة،
/// مع الخصم والضريبة والصافي على السطر — فاتورة البيع والشراء، عرض السعر، الأمر التجاري، بيان التسليم.
/// </summary>
[Collection("api")]
public class LineColumnsRoundTripTests : TestBase
{
    public LineColumnsRoundTripTests(ErpFactory f) : base(f) { }

    private static async Task<Guid> CenterAsync(Client api, string code)
    {
        var created = await api.Post("/costcenters", new { code, nameAr = "مركز " + code, nameEn = code, isActive = true });
        Assert.Equal(201, created.Status);
        return created.Data!["id"].G();
    }

    /// <summary>منشأة بموقعين ومركز تكلفة وصنف برصيد في الموقعين.</summary>
    private async Task<(Client Api, Guid Main, Guid Second, Guid Center, Guid Item)> SetupAsync()
    {
        var api = await NewTenantAsync();
        var main = (await api.Get("/warehouses")).Data!["items"]!.AsArray().Single()!["id"].G();
        var second = await api.Post("/warehouses", new { code = "W2", nameAr = "موقع ثان", nameEn = "Second", location = "جدة", status = "active" });
        Assert.True(second.Status == 201, second.Body?.ToJsonString());
        var site = second.Data!["id"].G();
        var item = await SeedProductAsync(api, stock: 20, cost: 40);
        var transfer = await api.Post("/stocktransfers", new { fromWarehouseId = main, toWarehouseId = site, items = new[] { new { itemId = item, quantity = 10 } } });
        Assert.True(transfer.Status is 200 or 201, transfer.Body?.ToJsonString());
        return (api, main, site, await CenterAsync(api, "LC1"), item);
    }

    private static JsonNode SingleLine(Res doc) => doc.Data!["items"]!.AsArray().Single()!;

    private static void AssertDimensions(JsonNode line, Guid site, Guid center)
    {
        Assert.Equal(site, line["warehouseId"].G());
        Assert.Equal(center, line["costCenterId"].G());
    }

    [Fact]
    public async Task Sales_invoice_line_keeps_its_cost_center_site_discount_and_vat()
    {
        var (api, main, site, center, item) = await SetupAsync();
        var sale = await api.PostInvoice(new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash", warehouseId = main, sendToZatca = false,
            items = new[] { new { itemId = item, quantity = 2, unitPrice = 100, discount = 20, vatRate = 15, warehouseId = site, costCenterId = center } },
        });
        Assert.True(sale.Status == 201, sale.Body?.ToJsonString());
        var line = SingleLine(await api.Get(await api.InvoicePath(sale.Data!["id"].S())));
        AssertDimensions(line, site, center);
        Assert.Equal(20m, line["discount"].D());
        Assert.Equal(15m, line["vatRate"].D());
        Assert.Equal(180m, line["totalBeforeVat"].D());
        Assert.Equal(27m, line["vatAmount"].D());
        Assert.Equal(207m, line["totalAfterVat"].D());
    }

    [Fact]
    public async Task Purchase_invoice_line_keeps_its_cost_center_and_site()
    {
        var (api, main, site, center, item) = await SetupAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var purchase = await api.PostInvoice(new
        {
            kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier, warehouseId = main,
            items = new[] { new { itemId = item, quantity = 4, unitPrice = 40, discount = 10, vatRate = 15, warehouseId = site, costCenterId = center } },
        });
        Assert.True(purchase.Status == 201, purchase.Body?.ToJsonString());
        var line = SingleLine(await api.Get(await api.InvoicePath(purchase.Data!["id"].S())));
        AssertDimensions(line, site, center);
        Assert.Equal(150m, line["totalBeforeVat"].D());
    }

    [Fact]
    public async Task Quotation_line_keeps_its_cost_center_and_site()
    {
        var (api, main, site, center, item) = await SetupAsync();
        var q = await api.Post("/quotations", new
        {
            type = "sales_quotation", partyName = "عميل", date = DateTime.UtcNow, validUntil = DateTime.UtcNow.AddDays(10), status = "draft", warehouseId = main,
            items = new[] { new { itemId = item, itemName = "منتج", sku = "P1", unit = "PCS", quantity = 1, unitPrice = 100, discount = 10, vatRate = 15, warehouseId = site, costCenterId = center } },
        });
        Assert.True(q.Status == 201, q.Body?.ToJsonString());
        AssertDimensions(SingleLine(await api.Get($"/quotations/{q.Data!["id"].S()}")), site, center);
    }

    [Fact]
    public async Task Commercial_order_line_keeps_its_cost_center_and_site()
    {
        var (api, main, site, center, item) = await SetupAsync();
        var order = await api.Post("/commercialorders", new
        {
            type = "sales_order", partyType = "customer", partyName = "عميل", orderDate = DateTime.UtcNow, expectedDeliveryDate = DateTime.UtcNow, status = "draft", warehouseId = main,
            items = new[] { new { itemId = item, itemName = "منتج", sku = "P1", unit = "PCS", quantity = 1, unitPrice = 100, discount = 10, vatRate = 15, warehouseId = site, costCenterId = center } },
        });
        Assert.True(order.Status is 200 or 201, order.Body?.ToJsonString());
        AssertDimensions(SingleLine(await api.Get($"/commercialorders/{order.Data!["id"].S()}")), site, center);
    }

    [Fact]
    public async Task Delivery_note_line_keeps_its_cost_center_and_site()
    {
        var (api, main, site, center, item) = await SetupAsync();
        var note = await api.Post("/deliverynotes", new
        {
            type = "sales_delivery", partyName = "عميل", date = DateTime.UtcNow, warehouseId = main,
            items = new[] { new { itemId = item, itemName = "منتج", sku = "P1", unit = "PCS", contractQty = 2, deliveredQty = 2, unitPrice = 100, discount = 10, warehouseId = site, costCenterId = center } },
        });
        Assert.True(note.Status is 200 or 201, note.Body?.ToJsonString());
        AssertDimensions(SingleLine(await api.Get($"/deliverynotes/{note.Data!["id"].S()}")), site, center);
    }
}
