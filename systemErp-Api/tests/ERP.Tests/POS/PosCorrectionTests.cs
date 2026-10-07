using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>تصحيح المستندات المالية: تعديل/حذف/إلغاء الورديات والمعاملات والمرتجعات والفواتير المرحّلة وحركات المخزون.</summary>
[Collection("api")]
public class PosCorrectionTests : TestBase
{
    public PosCorrectionTests(ErpFactory f) : base(f) { }

    private static async Task OpenShiftAsync(Client api) => Assert.Equal(200, (await api.Post("/pos/shifts/open", new { openingCash = 500, posTerminalName = "POS-1" })).Status);

    private static object Cash(Guid product, decimal qty, decimal paid, Guid? customer = null)
        => new { customerId = customer, items = new[] { new { itemId = product, quantity = qty } }, paymentMethod = "cash", paidCash = paid };

    private static async Task<decimal> StockAsync(Client api, Guid product) => (await api.Get($"/products/{product}")).Data!["currentStock"].D();

    [Fact]
    public async Task Voiding_a_pos_sale_issues_a_full_credit_note_and_reverses_stock_shift_and_ledger()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        await OpenShiftAsync(api);
        var tx = await api.Post("/pos/transactions/checkout", Cash(product, 2, 230));
        var id = tx.Data!["id"].S();
        Assert.Equal(8, await StockAsync(api, product));

        var voided = await api.Post($"/pos/transactions/{id}/void");
        Assert.Equal(200, voided.Status); Assert.Equal("voided", voided.Data!["status"].S());
        Assert.Equal(10, await StockAsync(api, product));
        var shift = (await api.Get("/pos/shifts/active")).Data!;
        Assert.Equal(0, shift["totalCashSales"].D()); Assert.Equal(0, shift["totalGross"].D());
        Assert.Equal(409, (await api.Post($"/pos/transactions/{id}/void")).Status);
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
        Assert.Equal(0, (await api.Get("/accounts/ByCode/1111")).Data!["balance"].D()); // رُدّ النقد

        // الفاتورة الضريبية باقية ومرحّلة، وأُلغيت بإشعار دائن كامل؛ العملية باقية في السجل ولا تُحذف
        var kept = (await api.Get($"/pos/transactions/{id}")).Data!;
        Assert.Equal("voided", kept["status"].S());
        var invoiceId = kept["invoiceId"].S();
        Assert.Equal("posted", (await api.Get($"/invoices/{invoiceId}")).Data!["status"].S());
        var creditNote = Assert.Single((await api.Get("/invoices?kind=sales_return")).Data!["items"]!.AsArray())!;
        Assert.Equal(invoiceId, creditNote["originalInvoiceId"].S()); Assert.Equal(230, creditNote["grandTotal"].D());
        Assert.Equal(405, (await api.Delete($"/pos/transactions/{id}")).Status);
    }

    [Fact]
    public async Task Pos_sale_and_its_issued_invoice_cannot_be_edited_or_deleted()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        await OpenShiftAsync(api);
        var tx = await api.Post("/pos/transactions/checkout", Cash(product, 1, 115));
        var id = tx.Data!["id"].S();
        Assert.Equal(405, (await api.Put($"/pos/transactions/{id}", new { customerName = "أحمد", customerPhone = "0500000000", customerTaxNumber = "300000000000003" })).Status);
        var invoiceId = (await api.Get($"/pos/transactions/{id}")).Data!["invoiceId"].S();
        Assert.Equal(409, (await api.Delete($"/invoices/{invoiceId}")).Status);
    }

    [Fact]
    public async Task Sale_with_returns_cannot_be_voided_and_an_issued_return_is_not_deleted()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        await OpenShiftAsync(api);
        var tx = await api.Post("/pos/transactions/checkout", Cash(product, 3, 345));
        var id = tx.Data!["id"].S();
        var ret = await api.Post("/pos/returns", new { originalTransactionId = id, returnReason = "عيب", refundMethod = "cash", items = new[] { new { itemId = product, returnQuantity = 1 } } });
        Assert.Equal(200, ret.Status);
        var retId = ret.Data!["id"].S();
        Assert.Equal(8, await StockAsync(api, product));
        Assert.Equal(409, (await api.Post($"/pos/transactions/{id}/void")).Status);

        var upd = await api.Put($"/pos/returns/{retId}", new { returnReason = "سبب مصحّح" });
        Assert.Equal("سبب مصحّح", upd.Data!["returnReason"].S());

        // المرتجع إشعار دائن صادر: لا يُحذف، والعملية الأصلية لا تُلغى بعده
        Assert.Equal(405, (await api.Delete($"/pos/returns/{retId}")).Status);
        Assert.Equal(409, (await api.Post($"/pos/transactions/{id}/void")).Status);
        Assert.Equal(8, await StockAsync(api, product));
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
    }

    [Fact]
    public async Task Shift_update_recomputes_variance_and_a_shift_with_sales_cannot_be_deleted()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        await OpenShiftAsync(api);
        var shiftId = (await api.Get("/pos/shifts/active")).Data!["id"].S();
        await api.Post("/pos/transactions/checkout", Cash(product, 1, 115));
        Assert.Equal(200, (await api.Post("/pos/shifts/close", new { closingCashActual = 600 })).Status); // 500 + 115 = 615 → فرق -15

        var upd = await api.Put($"/pos/shifts/{shiftId}", new { posTerminalName = "POS-9", openingCash = 500, closingCashActual = 615, closingNotes = "بعد الجرد" });
        Assert.Equal(200, upd.Status); Assert.Equal("POS-9", upd.Data!["posTerminalName"].S()); Assert.Equal(0, upd.Data["cashVariance"].D());

        // وردية صدرت فيها فواتير لا تُحذف حتى مع cascade
        Assert.Equal(409, (await api.Delete($"/pos/shifts/{shiftId}")).Status);
        Assert.Equal(409, (await api.Delete($"/pos/shifts/{shiftId}?cascade=true")).Status);
        Assert.Equal(200, (await api.Get($"/pos/shifts/{shiftId}")).Status);
        Assert.Equal(9, await StockAsync(api, product));
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
    }

    [Fact]
    public async Task Empty_shift_can_be_deleted_directly()
    {
        var api = await NewTenantAsync();
        await OpenShiftAsync(api);
        var shiftId = (await api.Get("/pos/shifts/active")).Data!["id"].S();
        Assert.Equal(200, (await api.Delete($"/pos/shifts/{shiftId}")).Status);
        Assert.Null((await api.Get("/pos/shifts/active")).Data);
    }

    // ---------- الفواتير المرحّلة ----------
    [Fact]
    public async Task Posted_invoice_with_a_return_cannot_be_deleted()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        var inv = await api.Post("/invoices", new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", items = new[] { new { itemId = product, quantity = 2, unitPrice = 100, vatRate = 15 } } });
        var id = inv.Data!["id"].S();
        var ret = await api.Post("/invoices/returns", new { originalInvoiceId = id, returnReason = "x", refundPaymentMethod = "cash", lines = new[] { new { itemId = product, quantity = 1 } } });
        Assert.Equal(200, ret.Status);
        Assert.Equal(409, (await api.Delete($"/invoices/{id}")).Status);
    }

    // ---------- حركات المخزون ----------
    [Fact]
    public async Task Manual_stock_movements_can_be_updated_and_deleted_with_replay_and_negative_guard()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api); // 10 @ 40
        var opening = (await api.Get($"/stockmovements?itemId={product}")).Data!["items"]![0]!["id"].S();
        var upd = await api.Put($"/stockmovements/{opening}", new { itemId = product, type = "adjustment_in", quantity = 15, unitCost = 40, referenceNumber = "OPEN" });
        Assert.Equal(200, upd.Status);
        Assert.Equal(15, await StockAsync(api, product));

        await api.Post("/invoices", new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", items = new[] { new { itemId = product, quantity = 12, unitPrice = 100, vatRate = 15 } } });
        Assert.Equal(3, await StockAsync(api, product));
        // خفض الرصيد الافتتاحي تحت المباع يُرفض، وكذلك حذفه
        Assert.Equal(409, (await api.Put($"/stockmovements/{opening}", new { itemId = product, type = "adjustment_in", quantity = 5, unitCost = 40, referenceNumber = "OPEN" })).Status);
        Assert.Equal(409, (await api.Delete($"/stockmovements/{opening}")).Status);
        Assert.Equal(3, await StockAsync(api, product));

        // حركة النظام (من الفاتورة) لا تُعدَّل مباشرة
        var sys = (await api.Get($"/stockmovements?itemId={product}")).Data!["items"]!.AsArray().First(m => m!["sourceType"].S() == "invoice")!["id"].S();
        Assert.Equal(409, (await api.Delete($"/stockmovements/{sys}")).Status);
    }
}
