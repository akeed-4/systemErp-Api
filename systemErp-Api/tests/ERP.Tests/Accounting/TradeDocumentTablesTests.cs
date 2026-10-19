using ERP.Core.Models.Accounting;
using ERP.Service.Data;
using ERP.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Tests;

/// <summary>
/// مستندات التجارة العامة في جداولها المستقلة (SalesInvoice، PurchaseInvoice، SalesReturn، PurchaseReturn) وبمساراتها،
/// منفصلة عن فواتير المعارض (Invoice): كل مستند في جدوله، ولا يُقبل مستند في غير مساره.
/// </summary>
[Collection("api")]
public class TradeDocumentTablesTests : TestBase
{
    public TradeDocumentTablesTests(ErpFactory f) : base(f) { }

    private static object Doc(string kind, Guid product, decimal qty, string method = "cash", Guid? party = null, decimal price = 100)
        => new { kind, invoiceType = "simplified", paymentMethod = method, partyId = party, items = new[] { new { itemId = product, quantity = qty, unitPrice = price, vatRate = 15 } } };

    /// <summary>جدول المستند في قاعدة البيانات (بلا مرشّح المنشأة) — للتحقق من الفصل الفعلي لا من المسار فقط.</summary>
    private async Task<string?> TableOfAsync(string id)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var guid = Guid.Parse(id);
        var doc = await db.Set<InvoiceBase>().IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(i => i.Id == guid);
        var items = await db.Set<InvoiceItemBase>().IgnoreQueryFilters().AsNoTracking().Where(i => i.InvoiceId == guid).ToListAsync();
        // البنود في جدول البنود المقابل لجدول رأسها
        Assert.All(items, item => Assert.Equal(doc!.GetType().Name + "Item", item.GetType().Name));
        return doc?.GetType().Name;
    }

    [Fact]
    public async Task Each_document_kind_is_stored_in_its_own_table_and_served_by_its_own_endpoint()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 20);
        var (supplier, _) = await SeedSupplierAsync(api);

        var sale = await api.Post("/sales-invoices", Doc("sales", product, 2));
        Assert.Equal(201, sale.Status);
        var saleId = sale.Data!["id"].S();
        Assert.Equal("SalesInvoice", await TableOfAsync(saleId));
        Assert.Equal("posted", sale.Data["status"].S());
        Assert.StartsWith("SINV-", sale.Data["invoiceNumber"].S());

        var purchase = await api.Post("/purchase-invoices", Doc("purchase", product, 5, "credit", supplier, 40));
        Assert.Equal(201, purchase.Status);
        var purchaseId = purchase.Data!["id"].S();
        Assert.Equal("PurchaseInvoice", await TableOfAsync(purchaseId));

        var saleReturn = await api.Post("/sales-returns/from-invoice", new { originalInvoiceId = saleId, returnReason = "تالف", lines = new[] { new { itemId = product, quantity = 1 } } });
        Assert.Equal(200, saleReturn.Status);
        var saleReturnId = saleReturn.Data!["id"].S();
        Assert.Equal("SalesReturn", await TableOfAsync(saleReturnId));
        Assert.Equal(saleId, saleReturn.Data["originalInvoiceId"].S());

        var purchaseReturn = await api.Post("/purchase-returns/from-invoice", new { originalInvoiceId = purchaseId, returnReason = "زائد", lines = new[] { new { itemId = product, quantity = 2 } } });
        Assert.Equal(200, purchaseReturn.Status);
        Assert.Equal("PurchaseReturn", await TableOfAsync(purchaseReturn.Data!["id"].S()));

        // كل مستند على مساره فقط، ولا يظهر في مسار غيره ولا في فواتير المعارض
        Assert.Equal(200, (await api.Get($"/sales-invoices/{saleId}")).Status);
        Assert.Equal(404, (await api.Get($"/purchase-invoices/{saleId}")).Status);
        Assert.Equal(404, (await api.Get($"/sales-returns/{saleId}")).Status);
        Assert.Equal(404, (await api.Get($"/invoices/{saleId}")).Status);
        Assert.Equal(1, (await api.Get("/sales-invoices")).Data!["totalCount"].D());
        Assert.Equal(1, (await api.Get("/sales-returns")).Data!["totalCount"].D());
        Assert.Equal(0, (await api.Get("/invoices")).Data!["totalCount"].D());

        // المرتجع على أصل من جدول آخر يُرفض (مرتجع مبيعات لفاتورة مشتريات)
        Assert.Equal(404, (await api.Post("/sales-returns/from-invoice", new { originalInvoiceId = purchaseId, returnReason = "x" })).Status);

        // الأثر المحاسبي والمخزني كما كان: القيد متوازن والمخزون 20 + 5 − 2 + 1 − 2 = 22
        Assert.Equal(22, (await api.Get($"/products/{product}")).Data!["currentStock"].D());
        var (debit, credit) = Totals(await api.Get("/reports/TrialBalance"));
        Assert.Equal(debit, credit);
    }

    [Fact]
    public async Task General_trade_and_showroom_invoices_are_never_written_to_each_others_tables()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);

        // فاتورة تجارة عامة على مسار المعارض: مرفوضة ولا تُحفظ
        var onShowroom = await api.Post("/invoices", Doc("sales", product, 1));
        Assert.Equal(400, onShowroom.Status);
        Assert.Equal(0, (await api.Get("/sales-invoices")).Data!["totalCount"].D());

        // فاتورة بأسطر سيارات على مسار التجارة العامة: مرفوضة
        var withVehicles = await api.Post("/sales-invoices", new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash",
            vehicleLines = new[] { new { vin = Client.NewVin('S'), unitPrice = 1000, vatMode = "standard_15" } },
        });
        Assert.Equal(400, withVehicles.Status);

        // النوع يتبع المسار: طلب بنوع آخر على مسار المبيعات يُحفظ فاتورة مبيعات
        var forced = await api.Post("/sales-invoices", Doc("purchase", product, 1));
        Assert.Equal(201, forced.Status);
        Assert.Equal("sales", forced.Data!["kind"].S());
        Assert.Equal("SalesInvoice", await TableOfAsync(forced.Data["id"].S()));
    }

    [Fact]
    public async Task Draft_documents_are_edited_posted_and_deleted_in_their_own_table()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        var (supplier, _) = await SeedSupplierAsync(api);

        var draft = await api.Post("/purchase-invoices", new { kind = "purchase", invoiceType = "simplified", paymentMethod = "credit", partyId = supplier, status = "draft",
            items = new[] { new { itemId = product, quantity = 3, unitPrice = 40, vatRate = 15 } } });
        Assert.Equal(201, draft.Status);
        var id = draft.Data!["id"].S();
        Assert.Equal("draft", draft.Data["status"].S());

        var edited = await api.Put($"/purchase-invoices/{id}", new { kind = "purchase", invoiceType = "simplified", paymentMethod = "credit", partyId = supplier, status = "draft",
            items = new[] { new { itemId = product, quantity = 4, unitPrice = 40, vatRate = 15 } } });
        Assert.Equal(200, edited.Status);
        Assert.Equal(160, edited.Data!["subtotal"].D());
        Assert.Equal("PurchaseInvoice", await TableOfAsync(id));

        Assert.Equal(404, (await api.Post($"/sales-invoices/{id}/post")).Status); // ليست في جدول المبيعات
        var posted = await api.Post($"/purchase-invoices/{id}/post");
        Assert.Equal(200, posted.Status);
        Assert.StartsWith("PINV-", posted.Data!["invoiceNumber"].S());
        Assert.Equal(200, (await api.Get($"/purchase-invoices/{id}/journal")).Status);

        var second = await api.Post("/purchase-invoices", new { kind = "purchase", invoiceType = "simplified", paymentMethod = "cash", status = "draft",
            items = new[] { new { itemId = product, quantity = 1, unitPrice = 40, vatRate = 15 } } });
        var secondId = second.Data!["id"].S();
        Assert.Equal(200, (await api.Delete($"/purchase-invoices/{secondId}")).Status);
        Assert.Null(await TableOfAsync(secondId));
    }
}
