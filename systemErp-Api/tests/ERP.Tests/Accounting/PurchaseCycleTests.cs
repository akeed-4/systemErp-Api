using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// دورة الشراء: طلب بلا أسعار ← اعتماد ← طلب عروض لعدة موردين ← عروضهم ← المقارنة (السعر والخصم والضريبة والتكاليف الإضافية)
/// ← ترسية لكل سطر ← أمر شراء لكل مورد ← استلام جزئي لا يتجاوز المتبقي (ومتزامن) ← فاتورة لكل سند، والمستندات مترابطة بالمعرّفات.
/// </summary>
[Collection("api")]
public class PurchaseCycleTests : TestBase
{
    public PurchaseCycleTests(ErpFactory f) : base(f) { }

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");
    private static void Rejected(Res r, string step)
        => Assert.True(r.Status is 400 or 409, $"{step}: expected rejection, got {r.Status} {r.Body?.ToJsonString()}");

    private static JsonNode Line(JsonNode comparison, Guid product)
        => comparison["lines"]!.AsArray().First(l => l!["itemId"].G() == product)!;

    [Fact]
    public async Task Requisition_to_rfq_comparison_award_partial_receipts_and_invoices()
    {
        var api = await NewTenantAsync();
        var cement = await SeedProductAsync(api, "CEM", stock: 0, cost: 80);
        var steel = await SeedProductAsync(api, "STL", stock: 0, cost: 150);
        var (s1, _) = await SeedSupplierAsync(api, "مورد أول");
        var (s2, _) = await SeedSupplierAsync(api, "مورد ثان");

        // طلب شراء بلا أسعار ثم اعتماده
        var req = await api.Post("/materialrequisitions", new
        {
            requestDate = DateTime.UtcNow, requiredDate = DateTime.UtcNow.AddDays(5), department = "المشتريات", requestedBy = "أحمد", priority = "high",
            supplierId = s1, supplierName = "مورد أول", status = "draft",
            items = new object[]
            {
                new { itemId = cement, itemName = "أسمنت", sku = "CEM", unit = "PCS", requestedQuantity = 10 },
                new { itemId = steel, itemName = "حديد", sku = "STL", unit = "PCS", requestedQuantity = 4 },
            },
        });
        Ok(req, "requisition");
        var reqId = req.Data!["id"].S();
        Rejected(await api.Post("/purchase-cycle/rfqs", new { requisitionId = reqId, supplierIds = new[] { s1, s2 } }), "rfq before approval");
        Ok(await api.Post($"/materialrequisitions/{reqId}/approve"), "approve");

        var rfq = await api.Post("/purchase-cycle/rfqs", new { requisitionId = reqId, supplierIds = new[] { s1, s2 }, dueDate = DateTime.UtcNow.AddDays(3) });
        Ok(rfq, "rfq");
        var rfqId = rfq.Data!["id"].S();
        Assert.Equal(2, rfq.Data["items"]!.AsArray().Count);
        Assert.Equal(2, rfq.Data["suppliers"]!.AsArray().Count);
        Rejected(await api.Post("/purchase-cycle/rfqs", new { requisitionId = reqId, supplierIds = new[] { s1 } }), "second active rfq");
        // الطلب المرتبط بطلب عروض قائم لا يُحوَّل لفاتورة مباشرة
        Assert.Equal(409, (await api.Post($"/materialrequisitions/{reqId}/ConvertToInvoice")).Status);

        Guid RfqItem(Guid product) => rfq.Data!["items"]!.AsArray().First(i => i!["itemId"].G() == product)!["id"].G();
        // المورد الأول: خصم على الأسمنت وتكاليف شحن 100؛ الثاني بلا خصم ولا شحن
        Ok(await api.Post($"/purchase-cycle/rfqs/{rfqId}/quotations", new
        {
            supplierId = s1, extraCosts = 100, deliveryDays = 5,
            lines = new object[]
            {
                new { rfqItemId = RfqItem(cement), unitPrice = 100, discount = 50, vatRate = 15 },
                new { rfqItemId = RfqItem(steel), unitPrice = 200, discount = 0, vatRate = 15 },
            },
        }), "quote s1");
        var other = await SeedSupplierAsync(api, "غير مدعو");
        Rejected(await api.Post($"/purchase-cycle/rfqs/{rfqId}/quotations", new
        {
            supplierId = other.Id, lines = new object[] { new { rfqItemId = RfqItem(cement), unitPrice = 1, vatRate = 15 } },
        }), "uninvited supplier");
        Ok(await api.Post($"/purchase-cycle/rfqs/{rfqId}/quotations", new
        {
            supplierId = s2,
            lines = new object[]
            {
                new { rfqItemId = RfqItem(cement), unitPrice = 90, discount = 0, vatRate = 15 },
                new { rfqItemId = RfqItem(steel), unitPrice = 260, discount = 0, vatRate = 15 },
            },
        }), "quote s2");

        // المقارنة: الأسمنت أرخص عند الثاني (90 مقابل 100.43 بعد الخصم ونصيب الشحن)، والحديد عند الأول، والإجمالي عند الأول
        var cmp = (await api.Get($"/purchase-cycle/rfqs/{rfqId}/comparison")).Data!;
        var quotes = cmp["quotes"]!.AsArray();
        var q1 = quotes.First(q => q!["supplierId"].G() == s1)!;
        var q2 = quotes.First(q => q!["supplierId"].G() == s2)!;
        Assert.Equal(1750m, q1["net"].D());
        Assert.Equal(100m, q1["extraCosts"].D());
        Assert.Equal(2127.5m, q1["grandTotal"].D());
        Assert.Equal(2231m, q2["grandTotal"].D());
        Assert.Equal(q1["quotationId"].S(), cmp["bestTotalQuotationId"].S());
        Assert.Equal(q2["quotationId"].S(), Line(cmp, cement)["bestQuotationId"].S());
        Assert.Equal(q1["quotationId"].S(), Line(cmp, steel)["bestQuotationId"].S());
        var s1Cement = Line(cmp, cement)["offers"]!.AsArray().First(o => o!["quotationId"].S() == q1["quotationId"].S())!;
        Assert.Equal(950m, s1Cement["net"].D());
        Assert.Equal(54.29m, s1Cement["extraShare"].D());

        // الترسية لكل سطر: أمر شراء لكل مورد، ولا ترسية ثانية
        var award = await api.Post($"/purchase-cycle/rfqs/{rfqId}/award", new
        {
            lines = new object[]
            {
                new { rfqItemId = RfqItem(cement), quotationId = q2["quotationId"].S() },
                new { rfqItemId = RfqItem(steel), quotationId = q1["quotationId"].S() },
            },
        });
        Ok(award, "award");
        Assert.Equal("awarded", award.Data!["status"].S());
        var orders = award.Data["orders"]!.AsArray();
        Assert.Equal(2, orders.Count);
        Rejected(await api.Post($"/purchase-cycle/rfqs/{rfqId}/award", new { quotationId = q1["quotationId"].S() }), "award twice");
        var cementOrder = orders.First(o => o!["supplierId"].G() == s2)!["id"].S();
        var steelOrder = orders.First(o => o!["supplierId"].G() == s1)!["id"].S();

        // أمر الحديد يحمل نصيب الحديد وحده من شحن المورد الأول (100 − نصيب الأسمنت غير المرسّى 54.29 = 45.71):
        // سعر الاستلام (800 + 45.71) ÷ 4 = 211.4275
        Assert.Equal(45.71m, orders.First(o => o!["supplierId"].G() == s1)!["extraCosts"].D());
        Assert.Equal(0m, orders.First(o => o!["supplierId"].G() == s2)!["extraCosts"].D());
        var steelTrack = (await api.Get($"/purchase-cycle/orders/{steelOrder}")).Data!;
        Assert.Equal(211.4275m, steelTrack["lines"]![0]!["unitPrice"].D());
        Assert.Equal(reqId, steelTrack["requisitionId"].S());
        Assert.Equal(rfqId, steelTrack["rfqId"].S());
        Assert.Equal(q1["quotationId"].S(), steelTrack["quotationId"].S());

        // أمر الدورة لا يُعدَّل ولا يُحذف ولا يُحوَّل كاملاً من شاشة الأوامر
        Rejected(await api.Post($"/commercialorders/{cementOrder}/ConvertToInvoice"), "full conversion");
        Assert.NotEqual(200, (await api.Delete($"/commercialorders/{cementOrder}")).Status);

        // استلام جزئي 6 من 10، ثم 5 مرفوض (المتبقي 4)
        var first = await api.Post($"/purchase-cycle/orders/{cementOrder}/receipts", new { lines = new[] { new { itemId = cement, quantity = 6 } } });
        Ok(first, "receipt 1");
        Assert.Equal(cementOrder, first.Data!["purchaseOrderId"].S());
        Rejected(await api.Post($"/purchase-cycle/orders/{cementOrder}/receipts", new { lines = new[] { new { itemId = cement, quantity = 5 } } }), "over receipt");
        Rejected(await api.Post($"/purchase-cycle/orders/{cementOrder}/receipts", new { lines = new[] { new { itemId = steel, quantity = 1 } } }), "item not in order");
        var track = (await api.Get($"/purchase-cycle/orders/{cementOrder}")).Data!;
        Assert.Equal("partially_fulfilled", track["status"].S());
        Assert.Equal(4m, track["lines"]![0]!["remainingToReceive"].D());
        Rejected(await api.Put($"/deliverynotes/{first.Data["id"].S()}", new { type = "purchase_delivery", partyName = "x", items = new[] { new { itemId = cement, itemName = "أسمنت", deliveredQty = 10, unitPrice = 1, vatRate = 15 } } }), "edit po receipt");

        // فاتورة جزئية للسند الأول: 6 × 90 ويدخل المخزون
        var inv1 = await api.Post($"/purchase-cycle/orders/{cementOrder}/receipts/{first.Data["id"].S()}/invoice");
        Ok(inv1, "invoice 1");
        Assert.Equal(540m, inv1.Data!["subtotal"].D());
        Assert.Equal(6m, (await api.Get($"/products/{cement}")).Data!["currentStock"].D());

        var second = await api.Post($"/purchase-cycle/orders/{cementOrder}/receipts", new { lines = new[] { new { itemId = cement, quantity = 4 } } });
        Ok(second, "receipt 2");
        Ok(await api.Post($"/purchase-cycle/orders/{cementOrder}/receipts/{second.Data!["id"].S()}/invoice"), "invoice 2");
        track = (await api.Get($"/purchase-cycle/orders/{cementOrder}")).Data!;
        Assert.Equal("completed", track["status"].S());
        Assert.Equal(10m, track["lines"]![0]!["invoiced"].D());
        Assert.Equal(2, track["receipts"]!.AsArray().Count);
        Assert.Equal(10m, (await api.Get($"/products/{cement}")).Data!["currentStock"].D());

        // استلامان متزامنان 3 + 3 على أمر حديد كميته 4: ينجح واحد فقط
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            api.Post($"/purchase-cycle/orders/{steelOrder}/receipts", new { lines = new[] { new { itemId = steel, quantity = 3 } } })));
        Assert.Equal(1, results.Count(r => r.Status is 200 or 201));
        var steelReceipt = results.First(r => r.Status is 200 or 201).Data!;
        Assert.Equal(634.28m, steelReceipt["subtotal"].D()); // 3 × 211.4275

        // طلب عروض عليه استلام لا يُلغى
        Rejected(await api.Post($"/purchase-cycle/rfqs/{rfqId}/cancel"), "cancel with receipts");
    }

    [Fact]
    public async Task Cancelled_rfq_releases_the_requisition()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, "P9", stock: 0);
        var (s1, _) = await SeedSupplierAsync(api, "مورد");
        var req = await api.Post("/materialrequisitions", new
        {
            requestDate = DateTime.UtcNow, requiredDate = DateTime.UtcNow.AddDays(5), department = "المشتريات", requestedBy = "أحمد", priority = "low",
            supplierId = s1, supplierName = "مورد", status = "draft",
            items = new[] { new { itemId = product, itemName = "صنف", sku = "P9", unit = "PCS", requestedQuantity = 3, estimatedCost = 10 } },
        });
        Ok(req, "requisition");
        var reqId = req.Data!["id"].S();
        Ok(await api.Post($"/materialrequisitions/{reqId}/approve"), "approve");
        var rfq = await api.Post("/purchase-cycle/rfqs", new { requisitionId = reqId, supplierIds = new[] { s1 } });
        Ok(rfq, "rfq");
        var rfqId = rfq.Data!["id"].S();
        var quote = await api.Post($"/purchase-cycle/rfqs/{rfqId}/quotations", new
        {
            supplierId = s1, lines = new[] { new { rfqItemId = rfq.Data["items"]![0]!["id"].G(), unitPrice = 10, vatRate = 15 } },
        });
        Ok(quote, "quote");
        // عرض الدورة لا يُحرَّر ولا يُحوَّل من شاشة العروض العامة
        Rejected(await api.Post($"/quotations/{quote.Data!["id"].S()}/ConvertToOrder"), "generic convert");
        Ok(await api.Post($"/purchase-cycle/rfqs/{rfqId}/award", new { quotationId = quote.Data["id"].S() }), "award");
        Ok(await api.Post($"/purchase-cycle/rfqs/{rfqId}/cancel"), "cancel");
        var orders = (await api.Get($"/purchase-cycle/rfqs/{rfqId}")).Data!["orders"]!.AsArray();
        Assert.All(orders, o => Assert.Equal("cancelled", o!["status"].S()));
        // الطلب عاد قابلاً للتحويل المباشر
        Ok(await api.Post($"/materialrequisitions/{reqId}/ConvertToInvoice"), "convert after cancel");
    }

    private static async Task<(string ReqId, JsonNode Rfq)> RfqAsync(Client api, Guid[] suppliers, params (Guid Item, string Name, decimal Qty)[] lines)
    {
        var req = await api.Post("/materialrequisitions", new
        {
            requestDate = DateTime.UtcNow, requiredDate = DateTime.UtcNow.AddDays(5), department = "المشتريات", requestedBy = "أحمد", priority = "high",
            status = "draft",
            items = lines.Select(l => new { itemId = l.Item, itemName = l.Name, sku = l.Name, unit = "PCS", requestedQuantity = l.Qty }).ToArray(),
        });
        Ok(req, "requisition");
        var reqId = req.Data!["id"].S();
        Ok(await api.Post($"/materialrequisitions/{reqId}/approve"), "approve requisition");
        var rfq = await api.Post("/purchase-cycle/rfqs", new { requisitionId = reqId, supplierIds = suppliers });
        Ok(rfq, "rfq");
        return (reqId, rfq.Data!);
    }

    private static Guid RfqItemOf(JsonNode rfq, Guid product) => rfq["items"]!.AsArray().First(i => i!["itemId"].G() == product)!["id"].G();

    [Fact]
    public async Task Partial_award_carries_only_awarded_extra_costs_and_receipts_combine_into_one_invoice()
    {
        var api = await NewTenantAsync();
        var alu = await SeedProductAsync(api, "ALU", stock: 0, cost: 10);
        var tile = await SeedProductAsync(api, "TIL", stock: 0, cost: 20);
        var (s1, _) = await SeedSupplierAsync(api, "مورد الألمنيوم");
        var (s2, _) = await SeedSupplierAsync(api, "مورد البلاط");
        var (_, rfq) = await RfqAsync(api, new[] { s1, s2 }, (alu, "ألمنيوم", 10), (tile, "بلاط", 5));
        var rfqId = rfq["id"].S();

        // الأول يسعّر البلاط بكمية جزئية (3 من 5) وعليه شحن 30؛ الثاني يغطي كل الأسطر
        var q1 = await api.Post($"/purchase-cycle/rfqs/{rfqId}/quotations", new
        {
            supplierId = s1, extraCosts = 30,
            lines = new object[]
            {
                new { rfqItemId = RfqItemOf(rfq, alu), unitPrice = 10, vatRate = 15 },
                new { rfqItemId = RfqItemOf(rfq, tile), quantity = 3, unitPrice = 20, vatRate = 15 },
            },
        });
        Ok(q1, "quote s1");
        var q2 = await api.Post($"/purchase-cycle/rfqs/{rfqId}/quotations", new
        {
            supplierId = s2,
            lines = new object[]
            {
                new { rfqItemId = RfqItemOf(rfq, alu), unitPrice = 12, vatRate = 15 },
                new { rfqItemId = RfqItemOf(rfq, tile), unitPrice = 20, vatRate = 15 },
            },
        });
        Ok(q2, "quote s2");
        string q1Id = q1.Data!["id"].S(), q2Id = q2.Data!["id"].S();

        // عرض لا يغطي كل الكميات المطلوبة لا يُرسّى كاملاً ولا على السطر الجزئي
        Rejected(await api.Post($"/purchase-cycle/rfqs/{rfqId}/award", new { quotationId = q1Id }), "whole award of partial quote");
        Rejected(await api.Post($"/purchase-cycle/rfqs/{rfqId}/award", new
        {
            lines = new object[] { new { rfqItemId = RfqItemOf(rfq, alu), quotationId = q1Id }, new { rfqItemId = RfqItemOf(rfq, tile), quotationId = q1Id } },
        }), "partial line award");
        Assert.Equal("open", (await api.Get($"/purchase-cycle/rfqs/{rfqId}")).Data!["status"].S());

        var award = await api.Post($"/purchase-cycle/rfqs/{rfqId}/award", new
        {
            lines = new object[] { new { rfqItemId = RfqItemOf(rfq, alu), quotationId = q1Id }, new { rfqItemId = RfqItemOf(rfq, tile), quotationId = q2Id } },
        });
        Ok(award, "award");
        var orders = award.Data!["orders"]!.AsArray();
        var aluOrder = orders.Single(o => o!["supplierId"].G() == s1)!;
        var tileOrder = orders.Single(o => o!["supplierId"].G() == s2)!;
        // نصيب الألمنيوم من الشحن 30 × 100 ÷ 160 = 18.75 فقط (نصيب البلاط غير المرسّى 11.25 لا يُحمَّل عليه)
        Assert.Equal(18.75m, aluOrder["extraCosts"].D());
        Assert.Equal(0m, tileOrder["extraCosts"].D());
        var aluTrack = (await api.Get($"/purchase-cycle/orders/{aluOrder["id"].S()}")).Data!;
        Assert.Equal(11.875m, aluTrack["lines"]![0]!["unitPrice"].D()); // (100 + 18.75) ÷ 10
        Assert.Equal(10m, aluTrack["lines"]![0]!["ordered"].D());       // لا تتجاوز الترسية كمية الطلب
        Assert.Equal(5m, (await api.Get($"/purchase-cycle/orders/{tileOrder["id"].S()}")).Data!["lines"]![0]!["ordered"].D());

        // استلامان جزئيان ثم فاتورة مورد واحدة لهما
        var aluId = aluOrder["id"].S();
        var r1 = await api.Post($"/purchase-cycle/orders/{aluId}/receipts", new { lines = new[] { new { itemId = alu, quantity = 4 } } });
        var r2 = await api.Post($"/purchase-cycle/orders/{aluId}/receipts", new { lines = new[] { new { itemId = alu, quantity = 6 } } });
        Ok(r1, "receipt 1"); Ok(r2, "receipt 2");
        string r1Id = r1.Data!["id"].S(), r2Id = r2.Data!["id"].S();
        Rejected(await api.Post($"/purchase-cycle/orders/{aluId}/invoice", new { deliveryNoteIds = Array.Empty<Guid>() }), "no receipts");
        Rejected(await api.Post($"/purchase-cycle/orders/{tileOrder["id"].S()}/invoice", new { deliveryNoteIds = new[] { r1Id } }), "receipt of another order");
        var combined = await api.Post($"/purchase-cycle/orders/{aluId}/invoice", new { deliveryNoteIds = new[] { r1Id, r2Id } });
        Ok(combined, "combined invoice");
        Assert.Equal(118.75m, combined.Data!["subtotal"].D());
        Assert.Equal(10m, (await api.Get($"/products/{alu}")).Data!["currentStock"].D());
        // لا تُفوتر الكمية مرتين: لا منفرداً ولا مجمّعاً
        Rejected(await api.Post($"/purchase-cycle/orders/{aluId}/receipts/{r1Id}/invoice"), "single after combined");
        Rejected(await api.Post($"/purchase-cycle/orders/{aluId}/invoice", new { deliveryNoteIds = new[] { r2Id } }), "combined twice");
        aluTrack = (await api.Get($"/purchase-cycle/orders/{aluId}")).Data!;
        Assert.Equal("completed", aluTrack["status"].S());
        Assert.Equal(10m, aluTrack["lines"]![0]!["invoiced"].D());
        Assert.All(aluTrack["receipts"]!.AsArray(), r => Assert.Equal(combined.Data["invoiceNumber"].S(), r!["invoiceNumber"].S()));

        // فاتورة مجمّعة وفاتورة منفردة متزامنتان على السند نفسه: تنجح واحدة فقط، ولا يتكرر المخزون
        var tileId = tileOrder["id"].S();
        var t1 = await api.Post($"/purchase-cycle/orders/{tileId}/receipts", new { lines = new[] { new { itemId = tile, quantity = 2 } } });
        var t2 = await api.Post($"/purchase-cycle/orders/{tileId}/receipts", new { lines = new[] { new { itemId = tile, quantity = 3 } } });
        Ok(t1, "tile receipt 1"); Ok(t2, "tile receipt 2");
        string t1Id = t1.Data!["id"].S(), t2Id = t2.Data!["id"].S();
        var race = await Task.WhenAll(
            api.Post($"/purchase-cycle/orders/{tileId}/invoice", new { deliveryNoteIds = new[] { t1Id, t2Id } }),
            api.Post($"/purchase-cycle/orders/{tileId}/receipts/{t1Id}/invoice"));
        Assert.Equal(1, race.Count(r => r.Status is 200 or 201));
        if (race[1].Status is 200 or 201) Ok(await api.Post($"/purchase-cycle/orders/{tileId}/receipts/{t2Id}/invoice"), "remaining receipt");
        var tileTrack = (await api.Get($"/purchase-cycle/orders/{tileId}")).Data!;
        Assert.Equal(5m, tileTrack["lines"]![0]!["invoiced"].D());
        Assert.Equal("completed", tileTrack["status"].S());
        Assert.Equal(5m, (await api.Get($"/products/{tile}")).Data!["currentStock"].D());
    }

    [Fact]
    public async Task Award_is_held_by_the_approval_policy_and_executed_on_final_approval()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, "APR", stock: 0, cost: 10);
        var (s1, _) = await SeedSupplierAsync(api, "مورد معتمد");
        var (s2, _) = await SeedSupplierAsync(api, "مورد آخر");
        var (_, rfq) = await RfqAsync(api, new[] { s1, s2 }, (product, "صنف", 4));
        var rfqId = rfq["id"].S();
        var quote = await api.Post($"/purchase-cycle/rfqs/{rfqId}/quotations", new
        {
            supplierId = s1, lines = new[] { new { rfqItemId = RfqItemOf(rfq, product), unitPrice = 100, vatRate = 15 } },
        });
        Ok(quote, "quote");

        Assert.Equal(201, (await api.Post("/ApprovalPolicies", new
        {
            nameAr = "اعتماد ترسية العروض", documentTypes = new[] { "purchase_quotation_award" }, actionType = "approve", minAmountTrigger = 1, isActive = true,
            steps = new[] { new { level = 1, approverRole = "general_manager", titleAr = "المدير العام" } },
        })).Status);
        var email = $"u{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await api.Post("/users", new { name = "مدير عام", email, phone = "1", password = "Passw0rd!", role = "general_manager" })).Status);
        var manager = await api.LoginAsAsync(NewHttp(), email, "Passw0rd!");

        // الترسية تُحجز بطلب اعتماد بمبلغ أوامرها (4 × 100 + 15%) ولا تُنفَّذ
        var held = await api.Post($"/purchase-cycle/rfqs/{rfqId}/award", new { quotationId = quote.Data!["id"].S() });
        Assert.Equal(202, held.Status);
        var requestId = held.Data!["id"].S();
        Assert.Equal(460m, held.Data["documentAmount"].D());
        var pending = (await api.Get($"/purchase-cycle/rfqs/{rfqId}")).Data!;
        Assert.Equal("open", pending["status"].S());
        Assert.True(pending["awardPendingApproval"]!.GetValue<bool>());
        Assert.Empty(pending["orders"]!.AsArray());
        // ما رآه المعتمِد ثابت حتى القرار: لا عرض جديد ولا مورد ولا إلغاء ولا طلب اعتماد ثانٍ
        Rejected(await api.Post($"/purchase-cycle/rfqs/{rfqId}/quotations", new
        {
            supplierId = s2, lines = new[] { new { rfqItemId = RfqItemOf(rfq, product), unitPrice = 50, vatRate = 15 } },
        }), "quote while pending");
        Rejected(await api.Post($"/purchase-cycle/rfqs/{rfqId}/cancel"), "cancel while pending");
        Rejected(await api.Post($"/purchase-cycle/rfqs/{rfqId}/award", new { quotationId = quote.Data["id"].S() }), "second request");

        Assert.Equal(200, (await manager.Post($"/approvals/{requestId}/approve", new { comment = "موافق" })).Status);
        var awarded = (await api.Get($"/purchase-cycle/rfqs/{rfqId}")).Data!;
        Assert.Equal("awarded", awarded["status"].S());
        Assert.False(awarded["awardPendingApproval"]!.GetValue<bool>());
        var order = awarded["orders"]!.AsArray().Single()!;
        Assert.Equal(s1, order["supplierId"].G());
        Assert.Equal(460m, order["grandTotal"].D());
    }
}
