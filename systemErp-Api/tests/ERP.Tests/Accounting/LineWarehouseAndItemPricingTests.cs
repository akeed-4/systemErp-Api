using ERP.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Tests;

/// <summary>
/// موقع لكل سطر (الهجرة LineWarehouseAndItemPricing): الصرف والاستلام والقيد على موقع كل سطر وحسابات موقعه، والمرتجع لموقع سطر أصله،
/// والحذف يعيد كل سطر لموقعه، والسطر بلا موقع = موقع المستند. وسعر بطاقة الصنف الشامل في نقطة البيع (ItemPricesIncludeVat).
/// لا اتصال بالهيئة (البديل المحلي، والإرسال معطّل في المستندات).
/// </summary>
[Collection("api")]
public class LineWarehouseAndItemPricingTests : TestBase
{
    public LineWarehouseAndItemPricingTests(ErpFactory f) : base(f) { }

    private async Task<decimal> SiteStockAsync(Guid item, Guid warehouse)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>();
        return await db.Set<ERP.Core.Models.Shared.WarehouseStock>().IgnoreQueryFilters()
            .Where(s => s.ItemId == item && s.WarehouseId == warehouse).Select(s => s.Quantity).FirstOrDefaultAsync();
    }

    private async Task<List<Guid?>> StoredLineSitesAsync(string invoiceId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>();
        var id = Guid.Parse(invoiceId);
        return await db.Set<ERP.Core.Models.Accounting.InvoiceItemBase>().IgnoreQueryFilters()
            .Where(l => l.InvoiceId == id).OrderBy(l => l.Quantity).Select(l => l.WarehouseId).ToListAsync();
    }

    private static async Task<decimal> BalanceAsync(Client api, string code) => (await api.Get($"/accounts/ByCode/{code}")).Data!["balance"].D();

    private static async Task AssertBooksReconciledAsync(Client api)
    {
        var (d, c) = Totals(await api.Get("/reports/TrialBalance"));
        Assert.Equal(d, c);
        var stock = (await api.Get("/reports/InventoryReconciliation")).Data!["rows"]!.AsArray().Single(r => r!["key"].S() == "items")!;
        Assert.True(stock["difference"].D() == 0, stock.ToJsonString());
    }

    /// <summary>منشأة بموقعين (الرئيسي + جدة بحساباته) وصنف: 10 بتكلفة 40 في الرئيسي، نُقل منها 5 إلى جدة.</summary>
    private async Task<(Client Api, Guid Main, Guid Jeddah, System.Text.Json.Nodes.JsonNode JeddahSite, Guid Item)> TwoSitesAsync()
    {
        var api = await NewTenantAsync();
        var main = (await api.Get("/warehouses")).Data!["items"]!.AsArray().Single()!["id"].G();
        var jed = await api.Post("/warehouses", new { code = "JED", nameAr = "موقع جدة", nameEn = "Jeddah", location = "جدة", status = "active" });
        Assert.True(jed.Status == 201, jed.Body?.ToJsonString());
        var jeddah = jed.Data!["id"].G();
        var item = await SeedProductAsync(api, stock: 10, cost: 40);
        var transfer = await api.Post("/stocktransfers", new { fromWarehouseId = main, toWarehouseId = jeddah, items = new[] { new { itemId = item, quantity = 5 } } });
        Assert.True(transfer.Status is 200 or 201, transfer.Body?.ToJsonString());
        var site = (await api.Get($"/warehouses/{jeddah}")).Data!;
        return (api, main, jeddah, site, item);
    }

    private static object TwoSiteSale(Guid main, Guid jeddah, Guid item) => new
    {
        kind = "sales", invoiceType = "simplified", paymentMethod = "cash", warehouseId = main, sendToZatca = false,
        items = new object[]
        {
            new { itemId = item, quantity = 2, unitPrice = 100, vatRate = 15 },                        // بلا موقع = موقع المستند
            new { itemId = item, quantity = 3, unitPrice = 100, vatRate = 15, warehouseId = jeddah },  // من جدة
        },
    };

    [Fact]
    public async Task Sale_lines_issue_from_their_own_site_and_post_to_its_accounts()
    {
        var (api, main, jeddah, site, item) = await TwoSitesAsync();
        var jedInventory = site["inventoryAccountCode"].S();
        var jedSales = site["salesAccountCode"].S();
        var jedCogs = site["cogsAccountCode"].S();
        var inventoryBefore = await BalanceAsync(api, jedInventory);

        var sale = await api.PostInvoice(TwoSiteSale(main, jeddah, item));
        Assert.True(sale.Status == 201, sale.Body?.ToJsonString());

        Assert.Equal(3m, await SiteStockAsync(item, main));   // 10 − 5 نقل − 2
        Assert.Equal(2m, await SiteStockAsync(item, jeddah)); // 5 − 3
        // الموقع الفعلي محفوظ على كل سطر، والقراءة تعيده باسمه
        Assert.Equal(new Guid?[] { main, jeddah }, (await StoredLineSitesAsync(sale.Data!["id"].S())).ToArray());
        var lines = (await api.Get(await api.InvoicePath(sale.Data["id"].S()))).Data!["items"]!.AsArray();
        Assert.Contains(lines, l => l!["warehouseId"].G() == jeddah && l["warehouseName"].S() == "موقع جدة");

        // سطر جدة على حسابات جدة: مبيعات 300، تكلفة 3 × 40، ومخزونها ينقص بالقيمة نفسها
        Assert.Equal(300m, Math.Abs(await BalanceAsync(api, jedSales)));
        Assert.Equal(120m, Math.Abs(await BalanceAsync(api, jedCogs)));
        Assert.Equal(inventoryBefore - 120m, await BalanceAsync(api, jedInventory));
        await AssertBooksReconciledAsync(api);
    }

    [Fact]
    public async Task Return_splits_an_item_over_its_source_lines_and_each_part_goes_back_to_its_site()
    {
        var (api, main, jeddah, _, item) = await TwoSitesAsync();
        var sale = await api.PostInvoice(TwoSiteSale(main, jeddah, item));
        Assert.Equal(201, sale.Status);

        var saleId = sale.Data!["id"].S();
        // سطر جدة بعينه (شاشة المرتجع تعرض كل سطر أصل وحده حين يرد الصنف من موقعين)
        var jeddahLine = (await api.Get(await api.InvoicePath(saleId))).Data!["items"]!.AsArray().Single(l => l!["warehouseId"].G() == jeddah)!["id"].G();
        var first = await api.PostReturn(new { originalInvoiceId = saleId, returnReason = "تالف",
            lines = new[] { new { itemId = item, quantity = 2, originalInvoiceItemId = jeddahLine } } });
        Assert.True(first.Status == 200, first.Body?.ToJsonString());
        Assert.Equal(3m, await SiteStockAsync(item, main));
        Assert.Equal(4m, await SiteStockAsync(item, jeddah));
        Assert.Equal(new Guid?[] { jeddah }, (await StoredLineSitesAsync(first.Data!["id"].S())).ToArray());

        // بلا سطر محدد: الكمية على المتبقي من أسطر الصنف (2 في الرئيسي و1 في جدة)، وكل جزء لموقع سطره
        var rest = await api.PostReturn(new { originalInvoiceId = saleId, returnReason = "تالف", lines = new[] { new { itemId = item, quantity = 3 } } });
        Assert.True(rest.Status == 200, rest.Body?.ToJsonString());
        Assert.Equal(5m, await SiteStockAsync(item, main));
        Assert.Equal(5m, await SiteStockAsync(item, jeddah));
        var sites = await StoredLineSitesAsync(rest.Data!["id"].S());
        Assert.Equal(2, sites.Count);
        Assert.Contains(main, sites);
        Assert.Contains(jeddah, sites);

        // لا متبقي
        var none = await api.PostReturn(new { originalInvoiceId = saleId, returnReason = "x", lines = new[] { new { itemId = item, quantity = 1 } } });
        Assert.True(none.Status is 400 or 409, none.Body?.ToJsonString());
        await AssertBooksReconciledAsync(api);
    }

    [Fact]
    public async Task Deleting_a_multi_site_invoice_restores_each_site_and_its_inventory_account()
    {
        var (api, main, jeddah, site, item) = await TwoSitesAsync();
        var jedInventory = site["inventoryAccountCode"].S();
        var before = await BalanceAsync(api, jedInventory);
        var sale = await api.PostInvoice(TwoSiteSale(main, jeddah, item));
        Assert.Equal(201, sale.Status);

        Assert.Equal(200, (await api.Delete(await api.InvoicePath(sale.Data!["id"].S()))).Status);
        Assert.Equal(5m, await SiteStockAsync(item, main));
        Assert.Equal(5m, await SiteStockAsync(item, jeddah));
        Assert.Equal(before, await BalanceAsync(api, jedInventory));
        await AssertBooksReconciledAsync(api);
    }

    [Fact]
    public async Task Purchase_lines_receive_into_their_site_inventory_account()
    {
        var (api, main, jeddah, site, item) = await TwoSitesAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var jedInventory = site["inventoryAccountCode"].S();
        var before = await BalanceAsync(api, jedInventory);
        var purchase = await api.PostInvoice(new
        {
            kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier, warehouseId = main,
            items = new object[]
            {
                new { itemId = item, quantity = 1, unitPrice = 40, vatRate = 15 },
                new { itemId = item, quantity = 4, unitPrice = 40, vatRate = 15, warehouseId = jeddah },
            },
        });
        Assert.True(purchase.Status == 201, purchase.Body?.ToJsonString());
        Assert.Equal(6m, await SiteStockAsync(item, main));
        Assert.Equal(9m, await SiteStockAsync(item, jeddah));
        Assert.Equal(before + 160m, await BalanceAsync(api, jedInventory));
        await AssertBooksReconciledAsync(api);
    }

    [Fact]
    public async Task Inactive_line_site_is_rejected_and_quotation_lines_keep_their_site_on_conversion()
    {
        var (api, main, jeddah, _, item) = await TwoSitesAsync();
        var off = await api.Post("/warehouses", new { code = "OFF", nameAr = "موقع موقوف", nameEn = "Off", location = "-", status = "inactive" });
        Assert.Equal(201, off.Status);
        var rejected = await api.PostInvoice(new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash", warehouseId = main, sendToZatca = false,
            items = new[] { new { itemId = item, quantity = 1, unitPrice = 100, vatRate = 15, warehouseId = off.Data!["id"].G() } },
        });
        Assert.Equal(400, rejected.Status);

        var q = await api.Post("/quotations", new
        {
            type = "sales_quotation", partyName = "عميل", date = DateTime.UtcNow, validUntil = DateTime.UtcNow.AddDays(10), status = "draft", warehouseId = main,
            items = new[] { new { itemId = item, itemName = "منتج", sku = "P1", unit = "PCS", quantity = 1, unitPrice = 100, discount = 10, vatRate = 15, warehouseId = jeddah } },
        });
        Assert.True(q.Status == 201, q.Body?.ToJsonString());
        Assert.Equal(jeddah, q.Data!["items"]![0]!["warehouseId"].G());
        var conv = await api.Post($"/quotations/{q.Data["id"].S()}/ConvertToInvoice");
        Assert.True(conv.Status == 200, conv.Body?.ToJsonString());
        Assert.Equal(new Guid?[] { jeddah }, (await StoredLineSitesAsync(conv.Data!["id"].S())).ToArray());
    }

    [Fact]
    public async Task Pos_sells_vat_inclusive_card_prices_without_adding_vat_twice()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 10, cost: 40, price: 115);
        Assert.Equal(200, (await api.Put("/accounting-settings", new { itemPricesIncludeVat = true })).Status);
        Assert.True((await api.Get("/accounting-settings/pricing-options")).Data!["itemPricesIncludeVat"]!.GetValue<bool>());
        await OpenPosShiftAsync(api);

        var quote = await api.Post("/pos/transactions/quote", new { items = new[] { new { itemId = product, quantity = 3 } }, paymentMethod = "cash" });
        Assert.True(quote.Status == 200, quote.Body?.ToJsonString());
        Assert.Equal("inclusive", quote.Data!["pricingMethod"].S());
        Assert.Equal(345m, quote.Data["grandTotal"].D());

        var tx = await api.Post("/pos/transactions/checkout", new { items = new[] { new { itemId = product, quantity = 3 } }, paymentMethod = "cash", paidCash = 345 });
        Assert.True(tx.Status is 200 or 201, tx.Body?.ToJsonString());
        Assert.Equal(345m, tx.Data!["grandTotal"].D()); // 3 × 115 كما على الرف
        Assert.Equal(45m, tx.Data["vatAmount"].D());    // Round(300 × 15%)
        var invoice = (await api.Get($"/sales-invoices/{tx.Data["invoiceId"].S()}")).Data!;
        Assert.Equal("inclusive", invoice["pricingMethod"].S());
        Assert.Equal(300m, invoice["subtotal"].D());
    }
}
