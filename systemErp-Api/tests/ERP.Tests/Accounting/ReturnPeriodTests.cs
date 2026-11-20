using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// فترة الإرجاع من إعدادات المنشأة: منع نهائي لأي مرتجع بعد انقضائها من تاريخ الفاتورة، ولكل من المبيعات والمشتريات مدته.
/// </summary>
[Collection("api")]
public class ReturnPeriodTests : TestBase
{
    public ReturnPeriodTests(ErpFactory f) : base(f) { }

    private static object Doc(string kind, Guid product, DateTime date, string method = "cash", Guid? party = null, decimal price = 100)
        => new { kind, invoiceType = "simplified", paymentMethod = method, partyId = party, issueDate = date, items = new[] { new { itemId = product, quantity = 2, unitPrice = price, vatRate = 15 } } };

    private static object ReturnOf(string invoiceId, Guid product)
        => new { originalInvoiceId = invoiceId, returnReason = "تالف", lines = new[] { new { itemId = product, quantity = 1 } } };

    [Fact]
    public async Task Returns_after_the_tenant_return_period_are_blocked()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 50);
        var (supplier, _) = await SeedSupplierAsync(api);
        var today = DateTime.UtcNow.AddHours(3).Date;

        // بلا إعداد: لا حد
        var oldSale = await api.Post("/sales-invoices", Doc("sales", product, today.AddDays(-10)));
        Assert.Equal(201, oldSale.Status);

        // مدة إرجاع المبيعات 7 أيام، والمشتريات بلا حد
        var saved = await api.Put("/accounting-settings", new { salesReturnPeriodDays = 7, purchaseReturnPeriodDays = 0 });
        Assert.Equal(200, saved.Status);
        Assert.Equal(7, saved.Data!["salesReturnPeriodDays"].D());

        // فاتورة عمرها 10 أيام: الإرجاع ممنوع
        var late = await api.Post("/sales-returns/from-invoice", ReturnOf(oldSale.Data!["id"].S(), product));
        Assert.Equal(409, late.Status);

        // فاتورة عمرها 7 أيام بالضبط: ضمن المدة
        var edgeSale = await api.Post("/sales-invoices", Doc("sales", product, today.AddDays(-7)));
        Assert.Equal(200, (await api.Post("/sales-returns/from-invoice", ReturnOf(edgeSale.Data!["id"].S(), product))).Status);

        // المشتريات بلا حد: فاتورة قديمة تُرجَع
        var oldPurchase = await api.Post("/purchase-invoices", Doc("purchase", product, today.AddDays(-60), "credit", supplier, 40));
        Assert.Equal(201, oldPurchase.Status);
        Assert.Equal(200, (await api.Post("/purchase-returns/from-invoice", ReturnOf(oldPurchase.Data!["id"].S(), product))).Status);

        // قيمة خارج النطاق مرفوضة
        Assert.Equal(400, (await api.Put("/accounting-settings", new { salesReturnPeriodDays = 4000 })).Status);
    }

    [Fact]
    public async Task Preview_enforces_the_period_and_an_existing_return_stays_editable_after_the_period_is_shortened()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 50);
        var today = DateTime.UtcNow.AddHours(3).Date;
        var sale = await api.Post("/sales-invoices", Doc("sales", product, today.AddDays(-10)));
        Assert.Equal(201, sale.Status);
        var saleId = sale.Data!["id"].S();

        // مرتجع داخل المدة (بلا حد بعد)، ثم تُقصَّر المدة إلى 7 أيام
        var ret = await api.Post("/sales-returns/from-invoice", new { originalInvoiceId = saleId, returnReason = "تالف", returnDate = today, lines = new[] { new { itemId = product, quantity = 1 } } });
        Assert.Equal(200, ret.Status);
        var retId = ret.Data!["id"].S();
        Assert.Equal(200, (await api.Put("/accounting-settings", new { salesReturnPeriodDays = 7 })).Status);

        // الشاشة تعرف المدة، والمعاينة لمرتجع جديد تُرفض قبل الاختيار
        Assert.Equal(7, (await api.Get("/sales-returns/return-period")).Data!["days"].D());
        Assert.Equal(409, (await api.Post("/sales-returns/return-preview", new { originalInvoiceId = saleId, returnDate = today })).Status);

        // المرتجع القائم بتاريخه نفسه يبقى قابلاً للمعاينة والتعديل (أُنشئ داخل المدة)
        Assert.Equal(200, (await api.Post("/sales-returns/return-preview", new { originalInvoiceId = saleId, returnDate = today, excludeReturnId = retId })).Status);
        var edited = await api.Put($"/sales-returns/{retId}/return", new { originalInvoiceId = saleId, returnReason = "تالف", returnDate = today, lines = new[] { new { itemId = product, quantity = 2 } } });
        Assert.Equal(200, edited.Status);

        // تغيير تاريخه يعيد فحص المدة: بعد 7 أيام من الفاتورة مرفوض
        Assert.Equal(409, (await api.Put($"/sales-returns/{retId}/return", new { originalInvoiceId = saleId, returnReason = "تالف", returnDate = today.AddDays(-1), lines = new[] { new { itemId = product, quantity = 2 } } })).Status);
    }
}
