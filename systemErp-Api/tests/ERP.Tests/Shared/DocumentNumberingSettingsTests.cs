using ERP.Core.Models.Shared;
using ERP.Service.Data;
using ERP.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Tests;

/// <summary>
/// إعداد ترقيم المستندات (المرحلة الأولى): تلقائي (بادئة، طول، رقم تالٍ يُزاد فقط، نطاق لكل نوع، تصفير بالسنة المالية)
/// ويدوي (إلزامي، صيغة، بلا تكرار، يُقفل بعد الترحيل) لأنواع تدعمه فقط، ومستندات النظام تبقى تلقائية.
/// </summary>
[Collection("api")]
public class DocumentNumberingSettingsTests : TestBase
{
    public DocumentNumberingSettingsTests(ErpFactory f) : base(f) { }

    private static object Setting(string mode = "auto", string prefix = "SINV-", int padding = 6, bool resetYearly = false, bool isActive = true,
        long? nextNumber = null, string? scope = null, string? warehouseId = null)
        => new { mode, prefix, padding, resetYearly, isActive, nextNumber, scope, warehouseId };

    private static object Sale(string? number = null, string status = "posted", DateTime? issueDate = null, string? warehouseId = null) => new
    {
        kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status, invoiceNumber = number ?? "", issueDate = issueDate ?? DateTime.UtcNow, warehouseId,
        items = new[] { new { itemName = "خدمة", quantity = 1, unitPrice = 100, vatRate = 15 } },
    };

    private async Task SetFinancialYearStartAsync(Client api, string start)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var tenant = await db.Set<Tenant>().IgnoreQueryFilters().FirstAsync(t => t.Id == api.TenantId);
        tenant.FinancialYearStart = start;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Settings_list_phase_one_types_and_automatic_numbering_follows_prefix_length_and_next_number()
    {
        var api = await NewTenantAsync();
        var list = (await api.Get("/document-numbering")).Data!.AsArray();
        Assert.Equal(10, list.Count);
        var sales = list.Single(t => t!["key"].S() == "sales_invoice")!;
        Assert.Equal("SINV-000001", sales["nextFormatted"].S());
        Assert.Equal("auto", sales["mode"].S());
        Assert.True(sales["manualAllowed"]!.GetValue<bool>());

        // بادئة وطول ورقم تالٍ: الفاتورة التالية تأخذها، والمعاينة تطابقها
        var saved = await api.Put("/document-numbering/sales_invoice", Setting(prefix: "INV-", padding: 5, nextNumber: 100));
        Assert.Equal(200, saved.Status);
        Assert.Equal("INV-00100", saved.Data!["nextFormatted"].S());
        Assert.Equal("INV-00100", (await api.PostInvoice(Sale())).Data!["invoiceNumber"].S());
        Assert.Equal("INV-00101", (await api.PostInvoice(Sale())).Data!["invoiceNumber"].S());

        // الرقم التالي يُزاد فقط، والبادئة والطول بصيغة صحيحة
        Assert.Equal(400, (await api.Put("/document-numbering/sales_invoice", Setting(prefix: "INV-", padding: 5, nextNumber: 50))).Status);
        Assert.Equal(400, (await api.Put("/document-numbering/sales_invoice", Setting(prefix: "IN V", padding: 5))).Status);
        Assert.Equal(400, (await api.Put("/document-numbering/sales_invoice", Setting(prefix: "INV-", padding: 0))).Status);
        // يدوي لا يُسمح لنوع خارج القائمة أو لنوع تلقائي فقط
        Assert.Equal(404, (await api.Put("/document-numbering/pos_invoice", Setting(mode: "manual", prefix: "POS-"))).Status);

        // التعطيل يعيد الترقيم التلقائي الافتراضي (البادئة الافتراضية) دون منع الإنشاء
        Assert.Equal(200, (await api.Put("/document-numbering/sales_invoice", Setting(prefix: "INV-", padding: 5, isActive: false))).Status);
        Assert.StartsWith("SINV-", (await api.PostInvoice(Sale())).Data!["invoiceNumber"].S());

        // عزل المنشآت: منشأة أخرى على إعدادها الافتراضي
        var other = await NewTenantAsync();
        Assert.Equal("SINV-000001", (await other.PostInvoice(Sale())).Data!["invoiceNumber"].S());
    }

    [Fact]
    public async Task Yearly_reset_follows_the_company_fiscal_year_and_the_document_date()
    {
        var api = await NewTenantAsync();
        await SetFinancialYearStartAsync(api, "07-01");
        // بلا سنة في البادئة يتكرر الرقم بين السنوات: مرفوض
        Assert.Equal(400, (await api.Put("/document-numbering/sales_invoice", Setting(prefix: "INV-", resetYearly: true))).Status);
        Assert.Equal(200, (await api.Put("/document-numbering/sales_invoice", Setting(prefix: "INV-{YYYY}-", padding: 4, resetYearly: true))).Status);

        string Number(Res r) => r.Data!["invoiceNumber"].S();
        Assert.Equal("INV-2025-0001", Number(await api.PostInvoice(Sale(issueDate: new DateTime(2026, 6, 30)))));
        Assert.Equal("INV-2026-0001", Number(await api.PostInvoice(Sale(issueDate: new DateTime(2026, 7, 1)))));
        Assert.Equal("INV-2026-0002", Number(await api.PostInvoice(Sale(issueDate: new DateTime(2026, 7, 2)))));
        Assert.Equal("INV-2025-0002", Number(await api.PostInvoice(Sale(issueDate: new DateTime(2026, 6, 15)))));
    }

    [Fact]
    public async Task Manual_invoice_numbers_are_required_validated_unique_and_locked_after_posting()
    {
        var api = await NewTenantAsync();
        Assert.Equal(200, (await api.Put("/document-numbering/sales_invoice", Setting(mode: "manual"))).Status);
        Assert.Contains("sales_invoice", (await api.Get("/document-numbering-modes")).Data!.AsArray().Select(k => k!.S()));

        Assert.Equal("A-1001", (await api.PostInvoice(Sale("A-1001"))).Data!["invoiceNumber"].S());
        Assert.Equal(409, (await api.PostInvoice(Sale("A-1001"))).Status);   // مكرر
        Assert.Equal(400, (await api.PostInvoice(Sale())).Status);           // مطلوب
        Assert.Equal(400, (await api.PostInvoice(Sale("A 1002"))).Status);   // صيغة

        // المسودة: يُعتمد رقمها ويُغيَّر قبل الترحيل، ثم يُقفل
        var draft = await api.PostInvoice(Sale("D-1", status: "draft"));
        Assert.Equal("D-1", draft.Data!["invoiceNumber"].S());
        var id = draft.Data["id"].S();
        var path = await api.InvoicePath(id, "");
        Assert.Equal("D-2", (await api.Put(path, Sale("D-2", status: "draft"))).Data!["invoiceNumber"].S());
        Assert.Equal(200, (await api.Post(path + "/post")).Status);
        Assert.Equal("D-2", (await api.Get(path)).Data!["invoiceNumber"].S());
        Assert.Equal(409, (await api.Put(path, Sale("D-3"))).Status);

        // العودة للتلقائي: يتجاوز رقماً أُدخل يدوياً بالصيغة نفسها
        Assert.Equal(201, (await api.PostInvoice(Sale("SINV-000001"))).Status);
        Assert.Equal(200, (await api.Put("/document-numbering/sales_invoice", Setting())).Status);
        Assert.Equal("SINV-000002", (await api.PostInvoice(Sale())).Data!["invoiceNumber"].S());
    }

    [Fact]
    public async Task Manual_numbering_for_returns_vouchers_journals_quotations_and_purchase_orders_while_system_documents_stay_automatic()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 20);
        var customer = (await api.Get("/customers")).Data;
        foreach (var key in new[] { "sales_return", "receipt_voucher", "journal_entry", "sales_quotation", "purchase_order" })
            Assert.Equal(200, (await api.Put($"/document-numbering/{key}", Setting(mode: "manual", prefix: "X-"))).Status);
        // أوامر البيع تلقائية فقط في هذه المرحلة
        Assert.Equal(404, (await api.Put("/document-numbering/sales_order", Setting(mode: "manual", prefix: "SO-"))).Status);

        // فاتورة تلقائية وقيدها الآلي تلقائي رغم القيود اليدوية
        var sale = await api.PostInvoice(new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "posted",
            items = new[] { new { itemId = product, quantity = 2, unitPrice = 100, vatRate = 15 } } });
        Assert.Equal(201, sale.Status);
        var saleJournal = (await api.Get($"/journalentries/{sale.Data!["journalEntryId"].S()}")).Data!["entryNumber"].S();
        Assert.StartsWith("X-", saleJournal); // النوع مضبوط ببادئة X- لكنه تلقائي للقيد الآلي
        Assert.Matches(@"^X-\d{6}$", saleJournal);

        // مرتجع يدوي
        var ret = await api.PostReturn(new { originalInvoiceId = sale.Data["id"].S(), returnReason = "تالف", manualNumber = "SR-77", lines = new[] { new { itemId = product, quantity = 1 } } });
        Assert.True(ret.Status is 200 or 201, ret.Body?.ToJsonString());
        Assert.Equal("SR-77", ret.Data!["invoiceNumber"].S());

        // قيد يدوي برقمه، ومكرره مرفوض
        object Journal(string? n) => new { entryNumber = n ?? "", date = DateTime.UtcNow, description = "x",
            lines = new[] { new { accountCode = "1111", debit = 10m, credit = 0m }, new { accountCode = "31", debit = 0m, credit = 10m } } };
        var je = await api.Post("/journalentries", Journal("MJ-1"));
        Assert.Equal("MJ-1", je.Data!["entryNumber"].S());
        Assert.Equal(409, (await api.Post("/journalentries", Journal("MJ-1"))).Status);
        Assert.Equal(400, (await api.Post("/journalentries", Journal(null))).Status);

        // سند قبض يدوي
        var (cust, account) = await SeedCustomerAsync(api);
        var voucher = await api.Post("/vouchers", new { voucherNumber = "RC-5", type = "receipt", amount = 50, partyId = cust, partyName = "عميل",
            partyAccountCode = account, treasuryAccountCode = "1111", paymentMethod = "cash" });
        Assert.True(voucher.Status is 200 or 201, voucher.Body?.ToJsonString());
        Assert.Equal("RC-5", voucher.Data!["voucherNumber"].S());

        // عرض سعر وأمر شراء يدويان
        var q = await api.Post("/quotations", new { quotationNumber = "Q-9", type = "sales_quotation", partyName = "عميل", date = DateTime.UtcNow, validUntil = DateTime.UtcNow.AddDays(10), status = "draft",
            items = new[] { new { itemId = product, itemName = "منتج", sku = "P1", unit = "PCS", quantity = 1, unitPrice = 100, vatRate = 15, discount = 0 } } });
        Assert.Equal("Q-9", q.Data!["quotationNumber"].S());
        var po = await api.Post("/commercialorders", new { orderNumber = "PO-X1", type = "purchase_order", partyName = "مورد", orderDate = DateTime.UtcNow, expectedDeliveryDate = DateTime.UtcNow.AddDays(3), status = "draft",
            items = new[] { new { itemId = product, itemName = "منتج", sku = "P1", unit = "PCS", quantity = 1, unitPrice = 50, vatRate = 15 } } });
        Assert.True(po.Status is 200 or 201, po.Body?.ToJsonString());
        Assert.Equal("PO-X1", po.Data!["orderNumber"].S());

        // نقطة البيع ومرتجعها تلقائيان رغم المرتجعات اليدوية
        await OpenPosShiftAsync(api);
        var tx = await api.Post("/pos/transactions/checkout", new { items = new[] { new { itemId = product, quantity = 1 } }, paymentMethod = "cash", paidCash = 115m });
        Assert.Equal(201, tx.Status);
        Assert.StartsWith("POS-", tx.Data!["invoiceNumber"].S());
        _ = customer;
    }

    [Fact]
    public async Task Conversions_to_invoices_stay_automatic_when_invoices_are_manual()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 20);
        foreach (var key in new[] { "sales_invoice", "purchase_invoice" })
            Assert.Equal(200, (await api.Put($"/document-numbering/{key}", Setting(mode: "manual", prefix: "M-"))).Status);
        var items = new[] { new { itemId = product, itemName = "منتج", sku = "P1", unit = "PCS", quantity = 1, unitPrice = 100, vatRate = 15, discount = 0 } };

        // عرض سعر ← فاتورة مسودة ثم ترحيلها برقم تلقائي (لا يُطلب رقم يدوي للتحويل)
        var q = await api.Post("/quotations", new { type = "sales_quotation", partyName = "عميل", date = DateTime.UtcNow, validUntil = DateTime.UtcNow.AddDays(10), status = "draft", items });
        var fromQuote = await api.Post($"/quotations/{q.Data!["id"].S()}/ConvertToInvoice");
        Assert.Equal(200, fromQuote.Status);
        var posted = await api.Post(await api.InvoicePath(fromQuote.Data!["id"].S(), "/post"));
        Assert.Equal("posted", posted.Data!["status"].S());
        Assert.DoesNotContain("DRAFT", posted.Data["invoiceNumber"].S());

        // أمر بيع معتمد ← فاتورة بيع (مسار تحويل الأوامر نفسه)
        var po = await api.Post("/commercialorders", new { type = "sales_order", partyName = "عميل", orderDate = DateTime.UtcNow, expectedDeliveryDate = DateTime.UtcNow.AddDays(3), status = "confirmed", items });
        Assert.True(po.Status is 200 or 201, po.Body?.ToJsonString());
        var fromOrder = await api.Post($"/commercialorders/{po.Data!["id"].S()}/ConvertToInvoice");
        Assert.True(fromOrder.Status == 200, fromOrder.Body?.ToJsonString());

        // فاتورة من الشاشة ما زالت تطلب الرقم اليدوي
        Assert.Equal(400, (await api.PostInvoice(Sale())).Status);
    }

    [Fact]
    public async Task Per_type_warehouse_scope_concurrency_and_permissions()
    {
        var api = await NewTenantAsync();
        var riyadh = (await api.Post("/warehouses", new { code = "RUH", nameAr = "معرض RUH", nameEn = "RUH", location = "الرياض", status = "active" })).Data!["id"].S();
        // نطاق المستودع لنوع واحد مع بقاء المنشأة على تسلسلها العام
        Assert.Equal(200, (await api.Put("/document-numbering/sales_invoice", Setting(scope: "warehouse"))).Status);
        Assert.Equal("SINV-RUH-000001", (await api.PostInvoice(Sale(warehouseId: riyadh))).Data!["invoiceNumber"].S());
        Assert.Equal("PINV-000001", (await api.PostInvoice(new { kind = "purchase", invoiceType = "simplified", paymentMethod = "cash", status = "posted",
            items = new[] { new { itemName = "خدمة", quantity = 1, unitPrice = 100, vatRate = 15 } } })).Data!["invoiceNumber"].S());

        // تزامن: فواتير متوازية بأرقام فريدة
        var parallel = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => api.PostInvoice(Sale(warehouseId: riyadh))));
        Assert.All(parallel, r => Assert.Equal(201, r.Status));
        Assert.Equal(8, parallel.Select(r => r.Data!["invoiceNumber"].S()).Distinct().Count());

        // الصلاحيات: مندوب بلا صلاحية الإعدادات لا يعدّل الترقيم
        var email = $"rep{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await api.Post("/users", new { name = "rep", email, phone = "1", password = "Passw0rd!", role = "sales_rep" })).Status);
        var rep = await api.LoginAsAsync(NewHttp(), email, "Passw0rd!");
        Assert.Equal(403, (await rep.Put("/document-numbering/sales_invoice", Setting(prefix: "HACK-"))).Status);
        Assert.Equal(200, (await rep.Get("/document-numbering-modes")).Status); // قراءة الأنواع اليدوية لشاشاته
    }
}
