using System.Net.Http.Headers;
using ERP.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Tests;

/// <summary>
/// متابعة التوريد والاستلام في المشتريات (المرحلة الثانية):
/// المسار الأول اتفاقية الشراء ← سند الاستلام ← مرتجع الاستلام ← الفوترة (قائم، يُدقَّق ويُحافظ عليه)،
/// والمسار الثاني فاتورة مشتريات مرحّلة ← سند استلام تشغيلي ← مرتجع الاستلام (بلا مخزون ولا قيد ولا فاتورة).
/// الكميات المقبولة والمرفوضة والمرتجعة تشغيلياً ومالياً، والتزامن، والمتابعة وسجلها ومرفقاتها، والصلاحيات، وتوافق البيانات القديمة.
/// </summary>
[Collection("api")]
public class PurchaseReceiptTrackingTests : TestBase
{
    public PurchaseReceiptTrackingTests(ErpFactory f) : base(f) { }

    // ---------------- أدوات ----------------

    private static void Is(int expected, Res r, string what) => Assert.True(r.Status == expected, $"{what}: {r.Status} {r.Body?.ToJsonString()}");

    private async Task<string> PurchaseAgreementAsync(Client api, Guid supplier, Guid product, decimal qty)
    {
        var created = await api.Post("/commercialcontracts", new
        {
            title = "اتفاقية شراء", contractType = "supply_materials", workflow = "simple", requiresGuarantee = false,
            partyType = "supplier", partyName = "مورد", partyId = supplier,
            contractValue = qty * 40, vatRate = 15, startDate = DateTime.UtcNow.Date, endDate = DateTime.UtcNow.Date.AddMonths(3),
            items = new object[] { new { itemId = product, description = "صنف", unit = "PCS", quantity = qty, unitPrice = 40, vatRate = 15 } },
            milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
        });
        Is(201, created, "purchase agreement");
        var id = created.Data!["id"].S();
        Is(200, await api.Post($"/commercialcontracts/{id}/move", new { toStatus = "active" }), "activate");
        return id;
    }

    private static object AgreementReceipt(string contractId, Guid supplier, Guid item, decimal accepted, decimal rejected = 0, string? reason = null) => new
    {
        type = "purchase_delivery", partyName = "مورد", partyId = supplier, partyType = "supplier", contractId, date = DateTime.UtcNow,
        items = new[] { new { itemId = item, itemName = "صنف", unit = "PCS", contractQty = 0, deliveredQty = accepted, rejectedQty = rejected, rejectionReason = reason, unitPrice = 40, vatRate = 15 } },
    };

    private static object InvoiceReceipt(string invoiceId, params (string Line, decimal Accepted, decimal Rejected, string? Reason, Guid? Warehouse)[] lines) => new
    {
        type = "purchase_delivery", sourceType = "purchase_invoice", sourceId = invoiceId, partyName = "يتجاهله الخادم", date = DateTime.UtcNow,
        items = lines.Select(l => new
        {
            sourceLineId = l.Line, itemId = Guid.NewGuid(), itemName = "x", deliveredQty = l.Accepted, rejectedQty = l.Rejected, rejectionReason = l.Reason,
            unitPrice = 1, vatRate = 0, warehouseId = l.Warehouse, unit = "BOX",
        }).ToArray(),
    };

    private static object ReceiptReturn(string noteId, Guid item, decimal qty) => new
    {
        type = "purchase_delivery_return", deliveryNoteId = noteId, returnReason = "تالف", items = new[] { new { itemId = item, quantity = qty } },
    };

    private async Task<(int Journals, int Movements, int Invoices)> FootprintAsync(Client api)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>();
        return (await db.Set<ERP.Core.Models.Accounting.JournalEntry>().IgnoreQueryFilters().CountAsync(j => j.TenantId == api.TenantId),
            await db.Set<ERP.Core.Models.Shared.StockMovement>().IgnoreQueryFilters().CountAsync(m => m.TenantId == api.TenantId),
            await db.Set<ERP.Core.Models.Accounting.InvoiceBase>().IgnoreQueryFilters().CountAsync(i => i.TenantId == api.TenantId));
    }

    private static async Task<decimal> StockAsync(Client api, Guid product) => (await api.Get($"/products/{product}")).Data!["currentStock"].D();

    private async Task<Client> UserWithAsync(Client owner, string label, params (string Screen, string[] Actions)[] screens)
    {
        var role = (await owner.Post("/roles", new { nameAr = label })).Data!["code"].S();
        Is(200, await owner.Post("/permissions", new
        {
            roleId = role,
            permissions = screens.Select(s => new { screenId = s.Screen, screenNameAr = "", screenNameEn = "", actions = s.Actions }).ToArray(),
        }), label);
        var roleId = (await owner.Get("/roles")).Data!.AsArray().First(r => r!["code"].S() == role)!["id"].G();
        var email = $"u{Guid.NewGuid():N}@test.com";
        Is(200, await owner.Post("/users", new { name = label, email, phone = "1", password = "Passw0rd!", role = "sales_rep", customRoleId = roleId }), label + " user");
        return await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");
    }

    /// <summary>فاتورة مشتريات مرحّلة: سطر صنف مخزني بكمية وسطر خدمة.</summary>
    private async Task<(string Invoice, string StockLine, string ServiceLine, Guid Product, Guid Supplier)> PostedPurchaseAsync(Client api, decimal qty = 10, string sku = "P1")
    {
        var (supplier, _) = await SeedSupplierAsync(api, "مورد " + sku);
        var product = await SeedProductAsync(api, sku: sku, stock: 0);
        var service = (await api.Post("/products", new { sku = "SRV" + sku, nameAr = "نقل", category = "C1", unit = "PCS", sellingPrice = 50, vatRate = 15, itemType = "service" })).Data!["id"].G();
        var purchase = await api.PostInvoice(new
        {
            kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier, status = "posted",
            items = new object[]
            {
                new { itemId = product, quantity = qty, unitPrice = 40, vatRate = 15 },
                new { itemId = service, quantity = 1, unitPrice = 50, vatRate = 15 },
            },
        });
        Is(201, purchase, "purchase invoice");
        var items = purchase.Data!["items"]!.AsArray();
        return (purchase.Data["id"].S(), items.First(i => i!["itemId"].G() == product)!["id"].S(),
            items.First(i => i!["itemId"].G() == service)!["id"].S(), product, supplier);
    }

    private static async Task<System.Text.Json.Nodes.JsonNode> InvoiceLineAsync(Client api, string invoice, string line)
        => (await api.Get($"/purchase-receipt-tracking/sources/purchase-invoice/{invoice}")).Data!["lines"]!.AsArray().First(l => l!["sourceLineId"].S() == line)!;

    private static async Task<System.Text.Json.Nodes.JsonNode> TrackingOfAsync(Client api, string sourceNumber)
    {
        var list = await api.Get($"/purchase-receipt-tracking?search={Uri.EscapeDataString(sourceNumber)}&pageSize=50");
        Is(200, list, "tracking list");
        var row = list.Data!["items"]!.AsArray().First(t => t!["sourceNumber"].S() == sourceNumber)!;
        return (await api.Get($"/purchase-receipt-tracking/{row["id"].S()}")).Data!;
    }

    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };

    private async Task<int> UploadAsync(Client api, string ownerType, string ownerId)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Png);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", "receipt.png");
        form.Add(new StringContent("photo"), "category");
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/attachments/{ownerType}/{ownerId}") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", api.Token);
        return (int)(await NewHttp().SendAsync(request)).StatusCode;
    }

    // ---------------- المسار الأول: الاتفاقية ← السند ← المرتجع ← الفوترة ----------------

    [Fact]
    public async Task Agreement_path_keeps_its_cycle_with_partial_receipts_rejections_returns_and_single_billing()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var product = await SeedProductAsync(api, stock: 0);
        var contract = await PurchaseAgreementAsync(api, supplier, product, 10);
        async Task<System.Text.Json.Nodes.JsonNode> Line() => (await api.Get($"/purchase-receipt-tracking/sources/contract/{contract}")).Data!["lines"]![0]!;

        Is(400, await api.Post("/deliverynotes", AgreementReceipt(contract, supplier, product, 4, 1)), "rejection needs a reason");
        var r1 = await api.Post("/deliverynotes", AgreementReceipt(contract, supplier, product, 4, 1, "كسر"));
        Is(200, r1, "partial receipt 1");
        Assert.Equal("manual", r1.Data!["sourceType"].S());                       // مصدر الاتفاقية كما كان، والاعتماد على ContractId
        var item = r1.Data["items"]![0]!;
        Assert.Equal(4, item["deliveredQty"].D());
        Assert.Equal(1, item["rejectedQty"].D());
        Assert.Equal(5, item["receivedQty"].D());
        Assert.Equal(6, (await Line())["remainingQty"].D());                       // المرفوض لم يُقبل فيبقى متبقياً

        var r2 = await api.Post("/deliverynotes", AgreementReceipt(contract, supplier, product, 6));
        Is(200, r2, "receipt 2 (multiple receipts per agreement)");
        Is(400, await api.Post("/deliverynotes", AgreementReceipt(contract, supplier, product, 1)), "over agreement");

        var r1Id = r1.Data["id"].S();
        var ret = await api.Post("/deliverynotes/returns", ReceiptReturn(r1Id, product, 2));
        Is(200, ret, "receipt return");
        Assert.Equal(2, (await Line())["remainingQty"].D());                       // المرتجع يعود للمتبقي
        Is(200, await api.Post("/deliverynotes", AgreementReceipt(contract, supplier, product, 2)), "re-supply");
        Assert.Equal(0, (await Line())["remainingQty"].D());

        // نقص مثبت أُصلح: حذف المرتجع بعد إعادة التوريد كان يرفع صافي المستلم فوق الاتفاقية
        Is(409, await api.Delete($"/deliverynotes/returns/{ret.Data!["id"].S()}"), "deleting the return would exceed the agreement");

        var before = await FootprintAsync(api);
        var bill = await api.Post($"/deliverynotes/{r1Id}/Invoice", new { });
        Is(200, bill, "bill receipt 1");
        Assert.Equal(2, bill.Data!["items"]![0]!["quantity"].D());                  // المقبول − المرتجع، بلا المرفوض
        Is(409, await api.Post($"/deliverynotes/{r1Id}/Invoice", new { }), "no double billing");
        var after = await FootprintAsync(api);
        Assert.Equal(before.Invoices + 1, after.Invoices);
        Assert.Equal(2, await StockAsync(api, product));                           // المخزون من الفاتورة وحدها، مرة واحدة

        var line = await Line();
        Assert.Equal(10, line["netAcceptedQty"].D());
        Assert.Equal(1, line["rejectedQty"].D());
        Assert.Equal(2, line["operationalReturnedQty"].D());
        Assert.Equal(2, line["invoicedQty"].D());
        Assert.Equal(0, line["awaitingFinancialReturnQty"].D());                    // مرفوض الاتفاقية لم يُفوتر

        // متابعة الاتفاقية تُفتح يدوياً، وحالتها من الكميات
        var opened = await api.Post("/purchase-receipt-tracking/open", new { sourceType = "contract", sourceId = contract });
        Is(200, opened, "open agreement tracking");
        Assert.Equal("received", opened.Data!["status"].S());
        Assert.Equal(3, opened.Data["receiptsCount"].GetValue<int>());
    }

    // ---------------- المسار الثاني: فاتورة مشتريات مرحّلة ← سند ----------------

    [Fact]
    public async Task Invoice_path_receipts_are_operational_only_partial_and_locked_to_the_invoice_lines()
    {
        var api = await NewTenantAsync();
        var (invoice, stockLine, serviceLine, product, supplier) = await PostedPurchaseAsync(api, qty: 10);
        var source = (await api.Get($"/purchase-receipt-tracking/sources/purchase-invoice/{invoice}")).Data!;
        Assert.False(source["lines"]!.AsArray().First(l => l!["sourceLineId"].S() == serviceLine)!["receivable"]!.GetValue<bool>());
        Assert.Equal(10, (await InvoiceLineAsync(api, invoice, stockLine))["remainingQty"].D());
        Assert.Null(source["trackingId"]);

        var stockBefore = await StockAsync(api, product);
        Assert.Equal(10, stockBefore);                                              // الفاتورة المرحّلة أدخلت المخزون
        var before = await FootprintAsync(api);

        Is(400, await api.Post("/deliverynotes", InvoiceReceipt(invoice, (serviceLine, 1, 0, null, null))), "service line excluded");
        Is(400, await api.Post("/deliverynotes", InvoiceReceipt(invoice, (Guid.NewGuid().ToString(), 1, 0, null, null))), "line not in invoice");
        Is(400, await api.Post("/deliverynotes", InvoiceReceipt(invoice, (stockLine, 0, 0, null, null))), "zero received");

        var otherSite = (await api.Post("/warehouses", new { code = "W2", nameAr = "موقع آخر" })).Data?["id"].S();
        var first = await api.Post("/deliverynotes", InvoiceReceipt(invoice, (stockLine, 4, 0, null, string.IsNullOrEmpty(otherSite) ? null : Guid.Parse(otherSite!))));
        Is(200, first, "first partial receipt");
        var line = first.Data!["items"]![0]!;
        var invoiceLine = await InvoiceLineAsync(api, invoice, stockLine);
        Assert.Equal("purchase_invoice", first.Data["sourceType"].S());
        Assert.Equal(invoice, first.Data["sourceId"].S());
        Assert.Equal(stockLine, line["sourceLineId"].S());
        Assert.Equal(supplier, first.Data["partyId"].G());
        Assert.Equal(product, line["itemId"].G());                                 // من الفاتورة لا من العميل
        Assert.Equal(invoiceLine["warehouseId"].S(), line["warehouseId"].S());     // المستودع الموروث لا يتغير
        Assert.Equal(invoiceLine["unit"].S(), line["unit"].S());                   // ولا الوحدة
        Assert.Equal(40, line["unitPrice"].D());
        Assert.StartsWith("GRN-", first.Data["deliveryNumber"].S());

        Is(400, await api.Post("/deliverynotes", InvoiceReceipt(invoice, (stockLine, 7, 0, null, null))), "over remaining 6");
        var second = await api.Post("/deliverynotes", InvoiceReceipt(invoice, (stockLine, 6, 0, null, null)));
        Is(200, second, "second receipt completes the line");
        Is(400, await api.Post("/deliverynotes", InvoiceReceipt(invoice, (stockLine, 1, 0, null, null))), "nothing left");

        // لا حركة ولا قيد ولا فاتورة ولا تغير رصيد
        Assert.Equal(before, await FootprintAsync(api));
        Assert.Equal(stockBefore, await StockAsync(api, product));

        var firstId = first.Data["id"].S();
        Is(409, await api.Post($"/deliverynotes/{firstId}/Invoice", new { }), "invoice-sourced receipt is not billable");
        Is(409, await api.Post($"/deliverynotes/{firstId}/MilestoneInvoice", new { milestoneId = Guid.NewGuid() }), "nor by milestone");
        Assert.Equal(before, await FootprintAsync(api));

        // الفاتورة المرتبطة بسند قائم لا تُعدَّل ولا تُحذف
        var full = (await api.Get($"/purchase-invoices/{invoice}")).Data!;
        Is(409, await api.Put($"/purchase-invoices/{invoice}", new
        {
            kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier, status = "posted",
            items = full["items"]!.AsArray().Select(i => new { itemId = i!["itemId"].G(), quantity = i["quantity"].D(), unitPrice = 41, vatRate = 15 }).ToArray(),
        }), "edit invoice with receipts");
        Is(409, await api.Delete($"/purchase-invoices/{invoice}"), "delete invoice with receipts");

        // المتابعة أُنشئت تلقائياً وحالتها من الكميات
        var tracking = await TrackingOfAsync(api, source["sourceNumber"].S());
        Assert.Equal("received", tracking["status"].S());
        Assert.Equal(2, tracking["receiptsCount"].GetValue<int>());
        var events = tracking["events"]!.AsArray().Select(e => e!["eventType"].S()).ToList();
        Assert.Equal(new[] { "opened", "receipt_created", "receipt_created" }, events);

        // حذف السندات يحرر الفاتورة
        Is(200, await api.Delete($"/deliverynotes/{firstId}"), "delete receipt");
        Is(200, await api.Delete($"/deliverynotes/{second.Data!["id"].S()}"), "delete receipt 2");
        Assert.Equal("pending", (await TrackingOfAsync(api, source["sourceNumber"].S()))["status"].S());
        Assert.Equal(10, (await InvoiceLineAsync(api, invoice, stockLine))["remainingQty"].D());
        var deleted = await api.Delete($"/purchase-invoices/{invoice}");
        Assert.True(deleted.Status != 409 || !(deleted.Body?.ToJsonString() ?? "").Contains("GRN-"), "invoice is no longer blocked by receipts");
    }

    [Fact]
    public async Task Rejected_quantity_waits_for_one_financial_return_and_is_never_counted_twice()
    {
        var api = await NewTenantAsync();
        var (invoice, stockLine, _, product, _) = await PostedPurchaseAsync(api, qty: 10);
        var receipt = await api.Post("/deliverynotes", InvoiceReceipt(invoice, (stockLine, 7, 3, "تالف عند الوصول", null)));
        Is(200, receipt, "receipt with rejection");
        var receiptId = receipt.Data!["id"].S();
        var line = await InvoiceLineAsync(api, invoice, stockLine);
        Assert.Equal(10, line["receivedQty"].D());
        Assert.Equal(7, line["acceptedQty"].D());
        Assert.Equal(3, line["rejectedQty"].D());
        Assert.Equal(3, line["awaitingFinancialReturnQty"].D());
        Assert.Equal(0, line["remainingQty"].D());                                 // المرفوض ينتظر المرتجع المالي لا إعادة التوريد
        Assert.Equal(10, await StockAsync(api, product));                          // الرفض بلا حركة مخزون

        var before = await FootprintAsync(api);
        Is(409, await api.PostReturn(new { originalInvoiceId = invoice, returnReason = "مرفوض", lines = new[] { new { itemId = product, originalInvoiceItemId = stockLine, quantity = 4 } } }),
            "financial return beyond the rejected quantity");
        var fin = await api.PostReturn(new { originalInvoiceId = invoice, returnReason = "مرفوض", lines = new[] { new { itemId = product, originalInvoiceItemId = stockLine, quantity = 3 } } });
        Assert.True(fin.Success, fin.Body?.ToJsonString());
        var after = await FootprintAsync(api);
        Assert.Equal(before.Invoices + 1, after.Invoices);
        Assert.Equal(before.Journals + 1, after.Journals);                         // قيد واحد عند ترحيل المرتجع المالي
        Assert.Equal(7, await StockAsync(api, product));                           // والمخزون مرة واحدة

        line = await InvoiceLineAsync(api, invoice, stockLine);
        Assert.Equal(3, line["financialReturnedQty"].D());
        Assert.Equal(0, line["awaitingFinancialReturnQty"].D());
        Assert.Equal(0, line["remainingQty"].D());                                 // المالي يقابل المرفوض: لا احتساب مرتين
        Is(409, await api.PostReturn(new { originalInvoiceId = invoice, returnReason = "مكرر", lines = new[] { new { itemId = product, originalInvoiceItemId = stockLine, quantity = 1 } } }),
            "the same quantity is not linked to a second financial return");

        // المرتجع التشغيلي يعيد الكمية للمتبقي (بلا مخزون ولا قيد)، ثم إعادة التوريد
        var footprint = await FootprintAsync(api);
        var opReturn = await api.Post("/deliverynotes/returns", ReceiptReturn(receiptId, product, 2));
        Is(200, opReturn, "operational receipt return");
        Assert.Equal(footprint, await FootprintAsync(api));
        line = await InvoiceLineAsync(api, invoice, stockLine);
        Assert.Equal(2, line["operationalReturnedQty"].D());
        Assert.Equal(2, line["remainingQty"].D());
        Is(200, await api.Post("/deliverynotes", InvoiceReceipt(invoice, (stockLine, 2, 0, null, null))), "re-supply");
        Assert.Equal(0, (await InvoiceLineAsync(api, invoice, stockLine))["remainingQty"].D());
        Is(409, await api.Delete($"/deliverynotes/returns/{opReturn.Data!["id"].S()}"), "deleting the return would exceed the invoice");
        Assert.Equal(7, await StockAsync(api, product));

        // تقرير المرفوض: قوبل بمرتجع مالي
        var report = await api.Get("/purchase-receipt-reports");
        Is(200, report, "report");
        var rejected = report.Data!["rejected"]!.AsArray().First(r => r!["deliveryNoteId"].S() == receiptId)!;
        Assert.Equal("financially_returned", rejected["state"].S());
        Assert.Equal("تالف عند الوصول", rejected["rejectionReason"].S());
        Assert.Contains(report.Data["returns"]!.AsArray(), r => r!["returnNumber"].S() == opReturn.Data["returnNumber"].S());
    }

    [Fact]
    public async Task Invoice_without_receipts_keeps_its_old_financial_return_rules()
    {
        var api = await NewTenantAsync();
        var (invoice, stockLine, _, product, _) = await PostedPurchaseAsync(api, qty: 5);
        var fin = await api.PostReturn(new { originalInvoiceId = invoice, returnReason = "x", lines = new[] { new { itemId = product, originalInvoiceItemId = stockLine, quantity = 5 } } });
        Assert.True(fin.Success, fin.Body?.ToJsonString());
        Assert.Equal(0, (await InvoiceLineAsync(api, invoice, stockLine))["remainingQty"].D());
        Is(400, await api.Post("/deliverynotes", InvoiceReceipt(invoice, (stockLine, 1, 0, null, null))), "fully returned financially");
    }

    [Fact]
    public async Task Receipts_cannot_start_from_returns_drafts_or_invoices_billed_from_receipts()
    {
        var api = await NewTenantAsync();
        var (invoice, stockLine, _, product, supplier) = await PostedPurchaseAsync(api, qty: 5);
        var ret = await api.PostReturn(new { originalInvoiceId = invoice, returnReason = "x", lines = new[] { new { itemId = product, quantity = 1 } } });
        Assert.True(ret.Success, ret.Body?.ToJsonString());
        var fromReturn = await api.Get($"/purchase-receipt-tracking/sources/purchase-invoice/{ret.Data!["id"].S()}");
        Assert.True(fromReturn.Status is 404 or 409, $"return is not a purchase invoice source: {fromReturn.Status}");

        var draft = await api.PostInvoice(new
        {
            kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier, status = "draft",
            items = new[] { new { itemId = product, quantity = 2, unitPrice = 40, vatRate = 15 } },
        });
        Is(201, draft, "draft");
        Is(409, await api.Get($"/purchase-receipt-tracking/sources/purchase-invoice/{draft.Data!["id"].S()}"), "draft");
        Is(409, await api.Post("/deliverynotes", InvoiceReceipt(draft.Data["id"].S(), (draft.Data["items"]![0]!["id"].S(), 1, 0, null, null))), "draft create");

        // فاتورة ناتجة عن دورة اتفاقية سبق استلامها وفوترتها
        var contract = await PurchaseAgreementAsync(api, supplier, product, 3);
        var note = (await api.Post("/deliverynotes", AgreementReceipt(contract, supplier, product, 3))).Data!["id"].S();
        var billed = await api.Post($"/deliverynotes/{note}/Invoice", new { });
        Is(200, billed, "bill agreement receipt");
        var billedId = billed.Data!["id"].S();
        Is(409, await api.Get($"/purchase-receipt-tracking/sources/purchase-invoice/{billedId}"), "source of billed agreement cycle");
        Is(409, await api.Post("/deliverynotes", InvoiceReceipt(billedId, (billed.Data["items"]![0]!["id"].S(), 1, 0, null, null))), "create from billed cycle");
        Is(400, await api.Post("/deliverynotes", new
        {
            type = "sales_delivery", sourceType = "purchase_invoice", sourceId = invoice, partyName = "x", date = DateTime.UtcNow,
            items = new[] { new { sourceLineId = stockLine, itemId = product, itemName = "x", deliveredQty = 1 } },
        }), "purchase source only for goods receipts");
    }

    // ---------------- التزامن ----------------

    [Fact]
    public async Task Concurrent_receipts_and_returns_never_exceed_the_invoice_or_agreement()
    {
        var api = await NewTenantAsync();
        var (invoice, stockLine, _, product, supplier) = await PostedPurchaseAsync(api, qty: 10);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => api.Post("/deliverynotes", InvoiceReceipt(invoice, (stockLine, 4, 0, null, null)))));
        Assert.Equal(2, results.Count(r => r.Status == 200));
        Assert.All(results.Where(r => r.Status != 200), r => Assert.Equal(400, r.Status));
        Assert.Equal(2, (await InvoiceLineAsync(api, invoice, stockLine))["remainingQty"].D());

        // سند مرفوضه ومرتجعان ماليان متزامنان: يمر أحدهما فقط
        var (invoice2, line2, _, product2, _) = await PostedPurchaseAsync(api, qty: 4, sku: "P2");
        Is(200, await api.Post("/deliverynotes", InvoiceReceipt(invoice2, (line2, 2, 2, "مخالف للمواصفات", null))), "receipt with rejection");
        var race = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            api.PostReturn(new { originalInvoiceId = invoice2, returnReason = "مرفوض", lines = new[] { new { itemId = product2, originalInvoiceItemId = line2, quantity = 2 } } })));
        Assert.Equal(1, race.Count(r => r.Success));

        var contract = await PurchaseAgreementAsync(api, supplier, product, 9);
        var onContract = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => api.Post("/deliverynotes", AgreementReceipt(contract, supplier, product, 3))));
        Assert.Equal(3, onContract.Count(r => r.Status == 200));
    }

    // ---------------- المتابعة: الموعد والتأخر والرفض والإلغاء والسجل والمرفقات ----------------

    [Fact]
    public async Task Tracking_computes_delay_requires_reasons_blocks_closed_sources_and_logs_events()
    {
        var api = await NewTenantAsync();
        var (invoice, stockLine, _, _, _) = await PostedPurchaseAsync(api, qty: 6);
        var opened = await api.Post("/purchase-receipt-tracking/open", new { sourceType = "purchase_invoice", sourceId = invoice, expectedDate = DateTime.UtcNow.Date.AddDays(-3), notes = "شحنة بحرية" });
        Is(200, opened, "open");
        var id = opened.Data!["id"].S();
        Assert.Equal("delayed", opened.Data["status"].S());                         // محسوبة من الموعد
        Assert.Equal("pending", opened.Data["storedStatus"].S());                   // ولا تُخزَّن
        Assert.True(opened.Data["delayDays"].GetValue<int>() >= 3);
        Is(200, await api.Post("/purchase-receipt-tracking/open", new { sourceType = "purchase_invoice", sourceId = invoice }), "open again returns the active one");

        var delayed = await api.Get("/purchase-receipt-tracking?status=delayed");
        Assert.Contains(delayed.Data!["items"]!.AsArray(), t => t!["id"].S() == id);

        Is(200, await api.Put($"/purchase-receipt-tracking/{id}", new { expectedDate = DateTime.UtcNow.Date.AddDays(5), notes = "تأجيل" }), "reschedule");
        Is(200, await api.Post("/deliverynotes", InvoiceReceipt(invoice, (stockLine, 2, 0, null, null))), "partial receipt");
        var details = (await api.Get($"/purchase-receipt-tracking/{id}")).Data!;
        Assert.Equal("partially_received", details["status"].S());
        Assert.False(details["isDelayed"]!.GetValue<bool>());

        // المرفقات على المتابعة وعلى السند
        Assert.Equal(200, await UploadAsync(api, "purchase_supply_tracking", id));
        Assert.Equal(200, await UploadAsync(api, "purchase_receipt", details["receipts"]![0]!["id"].S()));
        Assert.Single((await api.Get($"/attachments/purchase_supply_tracking/{id}")).Data!.AsArray());

        Is(400, await api.Post($"/purchase-receipt-tracking/{id}/reject", new { reason = " " }), "reason required");
        var rejected = await api.Post($"/purchase-receipt-tracking/{id}/reject", new { reason = "المورد أخلّ بالمواصفات" });
        Is(200, rejected, "reject");
        Assert.Equal("rejected", rejected.Data!["status"].S());
        Assert.False(string.IsNullOrEmpty(rejected.Data["rejectedByName"].S()));
        Assert.NotNull(rejected.Data["rejectedAt"]);
        Is(409, await api.Post($"/purchase-receipt-tracking/{id}/cancel", new { reason = "x" }), "already closed");
        Is(409, await api.Post("/deliverynotes", InvoiceReceipt(invoice, (stockLine, 1, 0, null, null))), "closed source blocks new receipts");

        var reopened = await api.Post("/purchase-receipt-tracking/open", new { sourceType = "purchase_invoice", sourceId = invoice });
        Is(200, reopened, "reopen");
        var newId = reopened.Data!["id"].S();
        Assert.NotEqual(id, newId);
        Is(200, await api.Post("/deliverynotes", InvoiceReceipt(invoice, (stockLine, 1, 0, null, null))), "receipt after reopening");
        var cancelled = await api.Post($"/purchase-receipt-tracking/{newId}/cancel", new { reason = "أُلغي الطلب" });
        Is(200, cancelled, "cancel");
        Assert.Equal("أُلغي الطلب", cancelled.Data!["cancellationReason"].S());

        var events = (await api.Get($"/purchase-receipt-tracking/{id}/events")).Data!.AsArray().Select(e => e!["eventType"].S()).ToList();
        Assert.Equal(new[] { "opened", "updated", "receipt_created", "rejected" }, events);
        var newEvents = cancelled.Data["events"]!.AsArray().Select(e => e!["eventType"].S()).ToList();
        Assert.Equal(new[] { "opened", "reopened", "receipt_created", "cancelled" }, newEvents);
    }

    // ---------------- الصلاحيات ----------------

    [Fact]
    public async Task Tracking_and_reports_are_granted_manually_and_enforced_in_the_backend()
    {
        var owner = await NewTenantAsync();
        var (invoice, stockLine, _, _, _) = await PostedPurchaseAsync(owner, qty: 6);

        // دور المشتريات وحده (حتى بالإضافة): لا متابعة ولا تقارير ولا سند من فاتورة
        var buyer = await UserWithAsync(owner, "مشتريات", ("purchases", new[] { "view", "create", "edit", "delete" }));
        Is(403, await buyer.Get("/purchase-receipt-tracking"), "purchases alone cannot open tracking");
        Is(403, await buyer.Get($"/purchase-receipt-tracking/sources/purchase-invoice/{invoice}"), "nor sources");
        Is(403, await buyer.Post("/deliverynotes", InvoiceReceipt(invoice, (stockLine, 1, 0, null, null))), "nor create a receipt from an invoice");
        Is(403, await buyer.Get("/purchase-receipt-reports"), "nor reports");

        // لا منح تلقائي لأدوار النظام الجاهزة
        var email = $"acc{Guid.NewGuid():N}@test.com";
        Is(200, await owner.Post("/users", new { name = "محاسب", email, phone = "1", password = "Passw0rd!", role = "chief_accountant" }), "chief accountant");
        var accountant = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");
        Is(403, await accountant.Get("/purchase-receipt-tracking"), "not granted by default");
        Is(403, await accountant.Get("/purchase-receipt-reports"), "reports not granted by default");

        var viewer = await UserWithAsync(owner, "متابعة", ("purchase-receipt-tracking", new[] { "view" }), ("purchases", new[] { "view", "create" }));
        Is(200, await viewer.Get("/purchase-receipt-tracking"), "view");
        Is(403, await viewer.Post("/purchase-receipt-tracking/open", new { sourceType = "purchase_invoice", sourceId = invoice }), "open needs create");
        Is(403, await viewer.Post("/deliverynotes", InvoiceReceipt(invoice, (stockLine, 1, 0, null, null))), "receipt needs tracking create");
        Is(403, await viewer.Get("/purchase-receipt-reports"), "reports are separate");

        var receiver = await UserWithAsync(owner, "استلام", ("purchase-receipt-tracking", new[] { "view", "create" }), ("purchases", new[] { "view", "create" }),
            ("purchase-receipt-reports", new[] { "view" }));
        var created = await receiver.Post("/deliverynotes", InvoiceReceipt(invoice, (stockLine, 1, 0, null, null)));
        Is(200, created, "receipt");
        var trackingId = (await receiver.Get("/purchase-receipt-tracking")).Data!["items"]![0]!["id"].S();
        Is(403, await receiver.Post($"/purchase-receipt-tracking/{trackingId}/reject", new { reason = "x" }), "reject needs reject");
        Is(403, await receiver.Post($"/purchase-receipt-tracking/{trackingId}/cancel", new { reason = "x" }), "cancel needs cancel");
        Is(403, await receiver.Put($"/purchase-receipt-tracking/{trackingId}", new { notes = "x" }), "edit needs edit");
        Is(200, await receiver.Get("/purchase-receipt-reports"), "reports");
        Assert.Equal(403, await UploadAsync(receiver, "purchase_supply_tracking", trackingId));   // مرفقات المتابعة بصلاحية التعديل
    }

    // ---------------- توافق البيانات القديمة وعدم التأثير على المبيعات ----------------

    [Fact]
    public async Task Legacy_receipts_without_received_quantity_count_as_their_note_quantity_and_sales_ignore_rejections()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var product = await SeedProductAsync(api, stock: 5);
        var contract = await PurchaseAgreementAsync(api, supplier, product, 10);
        var note = (await api.Post("/deliverynotes", AgreementReceipt(contract, supplier, product, 4))).Data!["id"].S();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>();
            // سجل قبل الهجرة: المستلم فارغ والمرفوض صفر
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE DeliveryNoteItem SET ReceivedQty = NULL, RejectedQty = 0 WHERE DeliveryNoteId = {Guid.Parse(note)}");
        }
        var line = (await api.Get($"/purchase-receipt-tracking/sources/contract/{contract}")).Data!["lines"]![0]!;
        Assert.Equal(4, line["receivedQty"].D());                                  // لا تحويل إلى صفر
        Assert.Equal(4, line["acceptedQty"].D());
        Assert.Equal(6, line["remainingQty"].D());
        Assert.Null((await api.Get($"/deliverynotes/{note}")).Data!["items"]![0]!["receivedQty"]);
        Is(200, await api.Post($"/deliverynotes/{note}/Invoice", new { }), "legacy receipt still bills");

        // بيان المبيعات لا يقبل رفضاً ولا مستلماً (سلوكه كما كان)
        var (customer, _) = await SeedCustomerAsync(api);
        var sale = await api.Post("/deliverynotes", new
        {
            type = "sales_delivery", partyName = "عميل", partyId = customer, date = DateTime.UtcNow,
            items = new[] { new { itemId = product, itemName = "صنف", unit = "PCS", deliveredQty = 2, rejectedQty = 1, rejectionReason = "x", unitPrice = 100, vatRate = 15 } },
        });
        Is(200, sale, "sales note");
        Assert.Equal(0, sale.Data!["items"]![0]!["rejectedQty"].D());
        Assert.Null(sale.Data["items"]![0]!["receivedQty"]);
        Assert.Equal("untracked", sale.Data["deliveryStatus"].S());
    }

    [Fact]
    public async Task Manual_goods_receipt_bills_only_the_accepted_quantity_and_stays_out_of_tracking()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var product = await SeedProductAsync(api, stock: 0);
        var manual = await api.Post("/deliverynotes", new
        {
            type = "purchase_delivery", partyName = "مورد", partyId = supplier, partyType = "supplier", date = DateTime.UtcNow,
            items = new[] { new { itemId = product, itemName = "صنف", unit = "PCS", deliveredQty = 3, rejectedQty = 1, rejectionReason = "كسر", unitPrice = 40, vatRate = 15 } },
        });
        Is(200, manual, "manual goods receipt");
        Assert.Equal("untracked", manual.Data!["deliveryStatus"].S());
        Assert.Equal("manual", manual.Data["sourceType"].S());
        Assert.Equal(4, manual.Data["items"]![0]!["receivedQty"].D());
        Assert.Equal(120, manual.Data["subtotal"].D());                            // قيمة السند بالمقبول
        var bill = await api.Post($"/deliverynotes/{manual.Data["id"].S()}/Invoice", new { });
        Is(200, bill, "bill");
        Assert.Equal(3, bill.Data!["items"]![0]!["quantity"].D());
        Assert.Equal(3, await StockAsync(api, product));
        Assert.Empty((await api.Get("/purchase-receipt-tracking")).Data!["items"]!.AsArray());
    }
}
