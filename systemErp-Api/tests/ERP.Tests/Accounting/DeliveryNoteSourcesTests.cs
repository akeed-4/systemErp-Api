using ERP.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Tests;

/// <summary>
/// مسارا بيان التسليم: الاتفاقية ← بيان ← فاتورة مرحلية بالمؤكد تسليمه، وفاتورة البيع المرحّلة ← بيان ← توصيل وإثبات استلام
/// (بلا فاتورة ولا حركة ولا قيد). الكميات والحجز بالتزامن، والإلغاء، والمرتجعات، والصلاحيات، وتوافق البيانات غير المتتبَّعة.
/// </summary>
[Collection("api")]
public class DeliveryNoteSourcesTests : TestBase
{
    public DeliveryNoteSourcesTests(ErpFactory f) : base(f) { }

    // ---------------- أدوات ----------------

    private static void Is(int expected, Res r, string what) => Assert.True(r.Status == expected, $"{what}: {r.Status} {r.Body?.ToJsonString()}");

    private async Task<string> AgreementAsync(Client api, Guid party, Guid product, decimal qty)
    {
        var created = await api.Post("/commercialcontracts", new
        {
            title = "اتفاقية توريد", contractType = "supply_materials", workflow = "simple", requiresGuarantee = false, partyName = "عميل", partyId = party,
            contractValue = qty * 100, vatRate = 15, startDate = DateTime.UtcNow.Date, endDate = DateTime.UtcNow.Date.AddMonths(3),
            items = new object[] { new { itemId = product, description = "صنف", unit = "PCS", quantity = qty, unitPrice = 100, vatRate = 15 } },
            milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
        });
        Is(201, created, "agreement");
        var id = created.Data!["id"].S();
        Is(200, await api.Post($"/commercialcontracts/{id}/move", new { toStatus = "active" }), "activate");
        return id;
    }

    private static object ContractNote(string contractId, Guid party, Guid item, decimal qty) => new
    {
        type = "sales_delivery", partyName = "عميل", partyId = party, contractId, date = DateTime.UtcNow,
        items = new[] { new { itemId = item, itemName = "صنف", unit = "PCS", contractQty = 0, deliveredQty = qty, unitPrice = 100, vatRate = 15 } },
    };

    private static object InvoiceNote(string invoiceId, params (string Line, decimal Qty, Guid? Warehouse)[] lines) => new
    {
        type = "sales_delivery", sourceType = "sales_invoice", sourceId = invoiceId, partyName = "يتجاهله الخادم", date = DateTime.UtcNow,
        items = lines.Select(l => new { sourceLineId = l.Line, itemId = Guid.NewGuid(), itemName = "x", deliveredQty = l.Qty, unitPrice = 1, vatRate = 0, warehouseId = l.Warehouse }).ToArray(),
    };

    private static async Task<Res> Confirm(Client api, string noteId, string receiver = "مستلم", decimal? qty = null)
    {
        var note = (await api.Get($"/deliverynotes/{noteId}")).Data!;
        var lines = note["items"]!.AsArray().Select(i => new { lineId = i!["id"].S(), confirmedQty = qty ?? i["deliveredQty"].D() }).ToArray();
        return await api.Post($"/delivery-tracking/{noteId}/confirm", new { receiverName = receiver, lines });
    }

    private async Task<(int Journals, int Movements, int Invoices)> FootprintAsync(Client api)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>();
        return (await db.Set<ERP.Core.Models.Accounting.JournalEntry>().IgnoreQueryFilters().CountAsync(j => j.TenantId == api.TenantId),
            await db.Set<ERP.Core.Models.Shared.StockMovement>().IgnoreQueryFilters().CountAsync(m => m.TenantId == api.TenantId),
            await db.Set<ERP.Core.Models.Accounting.InvoiceBase>().IgnoreQueryFilters().CountAsync(i => i.TenantId == api.TenantId));
    }

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

    /// <summary>فاتورة بيع مرحّلة: سطر صنف مخزني بكمية، وسطر خدمة.</summary>
    private async Task<(string Invoice, string StockLine, string ServiceLine, Guid Product)> PostedSaleAsync(Client api, decimal qty = 10)
    {
        var (customer, _) = await SeedCustomerAsync(api);
        var product = await SeedProductAsync(api, stock: 50);
        var service = (await api.Post("/products", new { sku = "SRV", nameAr = "تركيب", category = "C1", unit = "PCS", sellingPrice = 50, vatRate = 15, itemType = "service" })).Data!["id"].G();
        var sale = await api.Post("/sales-invoices", new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "credit", partyId = customer, status = "posted",
            items = new object[]
            {
                new { itemId = product, quantity = qty, unitPrice = 100, vatRate = 15 },
                new { itemId = service, quantity = 1, unitPrice = 50, vatRate = 15 },
            },
        });
        Is(201, sale, "sale");
        var items = sale.Data!["items"]!.AsArray();
        return (sale.Data["id"].S(), items.First(i => i!["itemId"].G() == product)!["id"].S(), items.First(i => i!["itemId"].G() == service)!["id"].S(), product);
    }

    // ---------------- المسار الأول: الاتفاقية ----------------

    [Fact]
    public async Task Agreement_path_bills_only_confirmed_quantities_once_across_several_partial_notes()
    {
        var api = await NewTenantAsync();
        var (party, _) = await SeedCustomerAsync(api);
        var product = await SeedProductAsync(api, stock: 50);
        var contract = await AgreementAsync(api, party, product, 10);

        var first = await api.Post("/deliverynotes", ContractNote(contract, party, product, 4));
        Is(200, first, "first note");
        Assert.Equal("contract", first.Data!["sourceType"].S());
        Assert.Equal("pending", first.Data["deliveryStatus"].S());
        Assert.False(string.IsNullOrEmpty(first.Data["items"]![0]!["sourceLineId"].S()));
        var second = await api.Post("/deliverynotes", ContractNote(contract, party, product, 6));
        Is(200, second, "second note (multiple notes, same agreement)");
        Is(400, await api.Post("/deliverynotes", ContractNote(contract, party, product, 1)), "over agreement");

        var firstId = first.Data["id"].S(); var secondId = second.Data!["id"].S();
        Is(409, await api.Post($"/deliverynotes/{firstId}/Invoice", new { }), "bill before confirmation");

        // تسليم جزئي: 3 من 4 — النقص (1) يتحرر من الاتفاقية
        Is(200, await api.Post($"/delivery-tracking/{firstId}/dispatch", new { driverName = "سائق" }), "dispatch");
        var confirmed = await Confirm(api, firstId, qty: 3);
        Is(200, confirmed, "confirm partial");
        Assert.Equal("partially_delivered", confirmed.Data!["deliveryStatus"].S());
        Assert.Equal(1, (await api.Get($"/deliverynotes/sources/contract/{contract}")).Data!["lines"]![0]!["remainingQty"].D());

        var before = await FootprintAsync(api);
        var inv1 = await api.Post($"/deliverynotes/{firstId}/Invoice", new { });
        Is(200, inv1, "interim invoice 1");
        Assert.Equal(3, inv1.Data!["items"]![0]!["quantity"].D());
        Is(409, await api.Post($"/deliverynotes/{firstId}/Invoice", new { }), "same quantity billed twice");
        var after = await FootprintAsync(api);
        Assert.Equal(before.Invoices + 1, after.Invoices);

        Is(200, await Confirm(api, secondId), "confirm second");
        var inv2 = await api.Post($"/deliverynotes/{secondId}/Invoice", new { });
        Is(200, inv2, "interim invoice 2");
        Assert.Equal(6, inv2.Data!["items"]![0]!["quantity"].D());
        Assert.Equal("invoiced", (await api.Get($"/deliverynotes/{secondId}")).Data!["status"].S());

        // الفاتورة المرحلية تتبع مستندها (القاعدة القائمة): لا تُحذف مباشرة، ويبقى ما فُوتر مسجلاً
        Is(409, await api.Delete($"/sales-invoices/{inv2.Data["id"].S()}"), "interim invoice follows its source");
        Assert.Equal(6, (await api.Get($"/deliverynotes/{secondId}")).Data!["items"]![0]!["invoicedQty"].D());
        Is(409, await api.Post($"/deliverynotes/{secondId}/Invoice", new { }), "no second time");

        var events = (await api.Get($"/deliverynotes/{firstId}/events")).Data!.AsArray().Select(e => e!["eventType"].S()).ToList();
        Assert.Equal(new[] { "created", "dispatched", "partially_delivered", "invoiced" }, events);
    }

    [Fact]
    public async Task Cancel_requires_reason_releases_quantities_and_is_blocked_after_delivery_or_billing()
    {
        var api = await NewTenantAsync();
        var (party, _) = await SeedCustomerAsync(api);
        var product = await SeedProductAsync(api);
        var contract = await AgreementAsync(api, party, product, 5);
        var note = (await api.Post("/deliverynotes", ContractNote(contract, party, product, 5))).Data!["id"].S();
        Is(400, await api.Post("/deliverynotes", ContractNote(contract, party, product, 1)), "fully reserved");

        Is(409, await api.Delete($"/deliverynotes/{note}"), "tracked note is never hard-deleted");
        Is(400, await api.Post($"/delivery-tracking/{note}/cancel", new { reason = " " }), "reason required");
        var cancelled = await api.Post($"/delivery-tracking/{note}/cancel", new { reason = "طلب العميل" });
        Is(200, cancelled, "cancel");
        Assert.Equal("cancelled", cancelled.Data!["status"].S());
        Assert.Equal("طلب العميل", cancelled.Data["cancellationReason"].S());
        Assert.False(string.IsNullOrEmpty(cancelled.Data["cancelledByUserId"].S()));
        Is(409, await api.Post($"/deliverynotes/{note}/Invoice", new { }), "cancelled is not billable");
        Is(409, await api.Post($"/delivery-tracking/{note}/cancel", new { reason = "مرة أخرى" }), "cancel twice");

        // الكمية تحررت
        var next = (await api.Post("/deliverynotes", ContractNote(contract, party, product, 5))).Data!["id"].S();
        Is(200, await Confirm(api, next), "confirm");
        Is(409, await api.Post($"/delivery-tracking/{next}/cancel", new { reason = "متأخر" }), "cannot cancel after actual delivery");
        var third = await AgreementAsync(api, party, product, 2);
        var billed = (await api.Post("/deliverynotes", ContractNote(third, party, product, 2))).Data!["id"].S();
        Is(200, await Confirm(api, billed), "confirm");
        Is(200, await api.Post($"/deliverynotes/{billed}/Invoice", new { }), "bill");
        Is(409, await api.Post($"/delivery-tracking/{billed}/cancel", new { reason = "بعد الفوترة" }), "cannot cancel after billing");
    }

    // ---------------- المسار الثاني: فاتورة البيع المرحّلة ----------------

    [Fact]
    public async Task Sales_invoice_path_is_operational_only_partial_notes_respect_remaining_and_keep_the_line_warehouse()
    {
        var api = await NewTenantAsync();
        var (invoice, stockLine, serviceLine, product) = await PostedSaleAsync(api, qty: 10);

        var source = (await api.Get($"/deliverynotes/sources/sales-invoice/{invoice}")).Data!;
        var lines = source["lines"]!.AsArray();
        Assert.False(lines.First(l => l!["sourceLineId"].S() == serviceLine)!["deliverable"]!.GetValue<bool>());
        Assert.Equal(10, lines.First(l => l!["sourceLineId"].S() == stockLine)!["remainingQty"].D());

        var stockBefore = (await api.Get($"/products/{product}")).Data!["currentStock"].D();
        var before = await FootprintAsync(api);

        Is(400, await api.Post("/deliverynotes", InvoiceNote(invoice, (serviceLine, 1, null))), "service line excluded");
        var otherSite = (await api.Post("/warehouses", new { code = "W2", nameAr = "موقع آخر" })).Data?["id"].S();
        var first = await api.Post("/deliverynotes", InvoiceNote(invoice, (stockLine, 4, otherSite == "" ? null : Guid.Parse(otherSite!))));
        Is(200, first, "first partial note");
        var line = first.Data!["items"]![0]!;
        Assert.Equal(product, line["itemId"].G());                                    // من الفاتورة لا من العميل
        Assert.Equal(lines.First(l => l!["sourceLineId"].S() == stockLine)!["warehouseId"].S(), line["warehouseId"].S()); // موقع سطر الفاتورة
        Assert.Equal(100, line["unitPrice"].D());
        Assert.Equal("sales_invoice", first.Data["sourceType"].S());

        Is(400, await api.Post("/deliverynotes", InvoiceNote(invoice, (stockLine, 7, null))), "over remaining 6");
        var second = await api.Post("/deliverynotes", InvoiceNote(invoice, (stockLine, 6, null)));
        Is(200, second, "second note");
        Is(400, await api.Post("/deliverynotes", InvoiceNote(invoice, (stockLine, 1, null))), "nothing left");

        // لا فاتورة ولا حركة ولا قيد ولا تغير رصيد
        Assert.Equal(before, await FootprintAsync(api));
        Assert.Equal(stockBefore, (await api.Get($"/products/{product}")).Data!["currentStock"].D());

        var firstId = first.Data["id"].S();
        Is(200, await Confirm(api, firstId), "confirm");
        Is(409, await api.Post($"/deliverynotes/{firstId}/Invoice", new { }), "invoice-sourced note is not billable");
        Is(409, await api.Post($"/deliverynotes/{firstId}/MilestoneInvoice", new { milestoneId = Guid.NewGuid() }), "nor by milestone");
        Assert.Equal(before, await FootprintAsync(api));

        // الفاتورة ذات البيانات القائمة لا تُعدَّل ولا تُحذف
        Is(409, await api.Delete($"/sales-invoices/{invoice}"), "invoice with active notes");
    }

    [Fact]
    public async Task Delivery_returns_reopen_quantity_while_credit_notes_remove_it()
    {
        var api = await NewTenantAsync();
        var (invoice, stockLine, _, product) = await PostedSaleAsync(api, qty: 10);
        async Task<decimal> Remaining() => (await api.Get($"/deliverynotes/sources/sales-invoice/{invoice}")).Data!["lines"]!.AsArray()
            .First(l => l!["sourceLineId"].S() == stockLine)!["remainingQty"].D();

        var note = (await api.Post("/deliverynotes", InvoiceNote(invoice, (stockLine, 5, null)))).Data!["id"].S();
        Assert.Equal(5, await Remaining());
        Is(409, await api.Post("/deliverynotes/returns", new { type = "sales_delivery_return", deliveryNoteId = note, returnReason = "رفض", items = new[] { new { itemId = product, quantity = 1 } } }),
            "delivery return before confirmation");
        Is(200, await Confirm(api, note), "confirm");
        Is(200, await api.Post("/deliverynotes/returns", new { type = "sales_delivery_return", deliveryNoteId = note, returnReason = "رفض", items = new[] { new { itemId = product, quantity = 2 } } }),
            "delivery return");
        Assert.Equal(7, await Remaining());   // مرتجع التسليم يعيد الكمية للتسليم

        var credit = await api.Post("/sales-returns/from-invoice", new { originalInvoiceId = invoice, returnReason = "إلغاء جزئي", lines = new[] { new { itemId = product, quantity = 3 } } });
        Is(200, credit, "credit note");
        Assert.Equal(4, await Remaining());   // الإشعار الدائن يخرجها من التسليم
        var src = (await api.Get($"/deliverynotes/sources/sales-invoice/{invoice}")).Data!["lines"]!.AsArray().First(l => l!["sourceLineId"].S() == stockLine)!;
        Assert.Equal(3, src["financialReturnedQty"].D());
        Assert.Equal(3, src["deliveredQty"].D());
    }

    [Fact]
    public async Task Invoice_issued_from_a_delivery_note_cannot_start_another_delivery_cycle()
    {
        var api = await NewTenantAsync();
        var (party, _) = await SeedCustomerAsync(api);
        var product = await SeedProductAsync(api);
        var contract = await AgreementAsync(api, party, product, 3);
        var note = (await api.Post("/deliverynotes", ContractNote(contract, party, product, 3))).Data!["id"].S();
        Is(200, await Confirm(api, note), "confirm");
        var invoice = (await api.Post($"/deliverynotes/{note}/Invoice", new { })).Data!["id"].S();
        Is(409, await api.Get($"/deliverynotes/sources/sales-invoice/{invoice}"), "source");
        Is(409, await api.Post("/deliverynotes", InvoiceNote(invoice, (Guid.NewGuid().ToString(), 1, null))), "create");
    }

    // ---------------- التزامن ----------------

    [Fact]
    public async Task Concurrent_notes_never_exceed_the_invoice_or_agreement_quantity()
    {
        var api = await NewTenantAsync();
        var (invoice, stockLine, _, product) = await PostedSaleAsync(api, qty: 10);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => api.Post("/deliverynotes", InvoiceNote(invoice, (stockLine, 4, null)))));
        Assert.Equal(2, results.Count(r => r.Status == 200));
        Assert.All(results.Where(r => r.Status != 200), r => Assert.Equal(400, r.Status));

        var (party, _) = await SeedCustomerAsync(api, name: "عميل 2");
        var contract = await AgreementAsync(api, party, product, 9);
        var onContract = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => api.Post("/deliverynotes", ContractNote(contract, party, product, 3))));
        Assert.Equal(3, onContract.Count(r => r.Status == 200));

        // تأكيد وإلغاء متزامنان على البيان نفسه: ينجح أحدهما فقط
        var note = onContract.First(r => r.Status == 200).Data!["id"].S();
        var race = await Task.WhenAll(Confirm(api, note), api.Post($"/delivery-tracking/{note}/cancel", new { reason = "تزامن" }));
        Assert.Equal(1, race.Count(r => r.Status == 200));
    }

    // ---------------- الصلاحيات والتوافق ----------------

    [Fact]
    public async Task Tracking_actions_require_their_own_permissions_in_the_backend()
    {
        var owner = await NewTenantAsync();
        var (invoice, stockLine, _, _) = await PostedSaleAsync(owner);
        var note = (await owner.Post("/deliverynotes", InvoiceNote(invoice, (stockLine, 2, null)))).Data!["id"].S();

        var seller = await UserWithAsync(owner, "مبيعات", ("sales", new[] { "view", "create" }));
        Is(403, await seller.Get("/delivery-tracking"), "sales alone cannot open tracking");
        Is(403, await seller.Post($"/delivery-tracking/{note}/dispatch", new { }), "sales alone cannot dispatch");
        Is(403, await seller.Get("/delivery-reports/summary"), "nor reports");

        var driver = await UserWithAsync(owner, "توصيل", ("delivery-tracking", new[] { "view", "send" }));
        Is(200, await driver.Get("/delivery-tracking"), "tracking list");
        Is(200, await driver.Post($"/delivery-tracking/{note}/dispatch", new { }), "dispatch");
        var lineId = (await owner.Get($"/deliverynotes/{note}")).Data!["items"]![0]!["id"].S();
        Is(403, await driver.Get($"/deliverynotes/{note}"), "tracking alone does not open the sales document");
        Is(403, await driver.Post($"/delivery-tracking/{note}/confirm", new { receiverName = "x", lines = new[] { new { lineId, confirmedQty = 2 } } }), "confirm needs receive");
        Is(403, await driver.Post($"/delivery-tracking/{note}/cancel", new { reason = "x" }), "cancel needs cancel");
        Is(403, await driver.Get("/delivery-reports/summary"), "reports are separate");

        var supervisor = await UserWithAsync(owner, "مشرف", ("delivery-tracking", new[] { "view", "receive", "cancel" }), ("delivery-reports", new[] { "view" }));
        Is(200, await supervisor.Post($"/delivery-tracking/{note}/confirm", new { receiverName = "عميل", lines = new[] { new { lineId = (await owner.Get($"/deliverynotes/{note}")).Data!["items"]![0]!["id"].S(), confirmedQty = 2 } } }), "confirm");
        var summary = await supervisor.Get("/delivery-reports/summary");
        Is(200, summary, "summary");
        Assert.Contains(summary.Data!.AsArray(), r => r!["deliveryStatus"].S() == "delivered" && r["sourceType"].S() == "sales_invoice");
    }

    [Fact]
    public async Task Manual_notes_and_goods_receipts_stay_untracked_with_their_old_rules()
    {
        var api = await NewTenantAsync();
        var (party, _) = await SeedCustomerAsync(api);
        var product = await SeedProductAsync(api);
        var manual = await api.Post("/deliverynotes", new
        {
            type = "sales_delivery", partyName = "عميل", partyId = party, date = DateTime.UtcNow,
            items = new[] { new { itemId = product, itemName = "صنف", unit = "PCS", deliveredQty = 2, unitPrice = 100, vatRate = 15, confirmedQty = 99, invoicedQty = 99 } },
        });
        Is(200, manual, "manual");
        Assert.Equal("manual", manual.Data!["sourceType"].S());
        Assert.Equal("untracked", manual.Data["deliveryStatus"].S());
        Assert.Null(manual.Data["items"]![0]!["confirmedQty"]);                  // لا يُقبل من العميل
        var id = manual.Data["id"].S();
        Is(409, await api.Post($"/delivery-tracking/{id}/dispatch", new { }), "untracked not in tracking");
        Is(200, await api.Post($"/deliverynotes/{id}/Invoice", new { }), "old billing unchanged");

        var other = (await api.Post("/deliverynotes", new
        {
            type = "sales_delivery", partyName = "عميل", partyId = party, date = DateTime.UtcNow,
            items = new[] { new { itemId = product, itemName = "صنف", unit = "PCS", deliveredQty = 1, unitPrice = 100, vatRate = 15 } },
        })).Data!["id"].S();
        Is(200, await api.Delete($"/deliverynotes/{other}"), "old delete unchanged");

        var (supplier, _) = await SeedSupplierAsync(api);
        var grn = await api.Post("/deliverynotes", new
        {
            type = "purchase_delivery", partyName = "مورد", partyId = supplier, partyType = "supplier", date = DateTime.UtcNow,
            items = new[] { new { itemId = product, itemName = "صنف", unit = "PCS", deliveredQty = 3, unitPrice = 40, vatRate = 15 } },
        });
        Is(200, grn, "goods receipt");
        Assert.Equal("untracked", grn.Data!["deliveryStatus"].S());
        Assert.Equal("manual", grn.Data["sourceType"].S());
    }
}
