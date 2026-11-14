using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// عزل مشتريات المشروع: طلب شراء المشروع ← سند استلام (عهدة للمشروع بسعر شرائها، بلا مستودع عام ولا متوسط تكلفة) ← فاتورة المورد
/// من السند تسوّي استحقاق الاستلام. منع الاستلام المكرر والتحويل المباشر للمستودع، والإرجاع للمورد، والحذف والعكس، واستخدام العهدة
/// في المستخلص والهدر.
/// </summary>
[Collection("api")]
public class ProjectPurchaseIsolationTests : TestBase
{
    public ProjectPurchaseIsolationTests(ErpFactory f) : base(f) { }

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");
    private static void Status(int expected, Res r, string step) => Assert.True(r.Status == expected, $"{step}: expected {expected}, got {r.Status} {r.Body?.ToJsonString()}");

    private sealed record Project(string Id, Guid Item, Guid Group, Guid Product, Guid Supplier, string SupplierAccount);

    // ---------- الإعداد ----------

    /// <summary>
    /// صنف أسمنت في المستودع العام 50 قطعة بتكلفة 10 (متوسطه 10)، مربوط بمادة مكتبة، وعقد مقاولات ساري ببند «صب» ومجموعة بفرعي،
    /// واحتياج معتمد للأسمنت على «صب» 100 قطعة.
    /// </summary>
    private async Task<(Client Api, Project P)> SetupAsync()
    {
        var api = await NewConstructionTenantAsync();
        var product = await SeedProductAsync(api, "CEM", stock: 50, cost: 10);
        var (supplier, supplierAccount) = await SeedSupplierAsync(api);
        var cement = await api.Post("/construction-resources", new { nameAr = "أسمنت", resourceType = "material", unit = "PCS", costSource = "product", productId = product });
        Ok(cement, "resource");
        var (customer, _) = await SeedCustomerAsync(api);
        var created = await api.Post("/commercialcontracts", new
        {
            title = "برج", contractType = "construction_boq", workflow = "simple", requiresGuarantee = false, partyType = "customer", partyId = customer, partyName = "عميل",
            startDate = DateTime.UtcNow.Date, endDate = DateTime.UtcNow.Date.AddMonths(6), vatRate = 15,
            items = new object[]
            {
                new { description = "صب", unit = "م3", quantity = 10, unitPrice = 100, vatRate = 15, lineKind = "measured", sortOrder = 1 },
                new { description = "مجموعة", unit = "م2", quantity = 1, unitPrice = 50, vatRate = 15, lineKind = "measured", sortOrder = 2 },
                new { description = "فرعي", unit = "م2", quantity = 1, unitPrice = 50, vatRate = 15, lineKind = "measured", isSubItem = true, sortOrder = 3 },
            },
            milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
        });
        Ok(created, "contract");
        var id = created.Data!["id"].S();
        Ok(await api.Post($"/commercialcontracts/{id}/move", new { toStatus = "active" }), "activate");
        var items = created.Data["items"]!.AsArray();
        Guid Line(string d) => items.First(i => i!["description"].S() == d)!["id"].G();
        Ok(await api.Put($"/commercialcontracts/{id}/material-requirements", new
        {
            lines = new object[] { new { boqItemId = Line("صب"), resourceId = cement.Data!["id"].G(), source = "manual", baselineQuantity = 100 } },
        }), "plan");
        Ok(await api.Post($"/commercialcontracts/{id}/material-requirements/submit"), "plan submit");
        Ok(await api.Post($"/commercialcontracts/{id}/material-requirements/approve"), "plan approve");
        return (api, new Project(id, Line("صب"), Line("مجموعة"), product, supplier, supplierAccount));
    }

    private static async Task<string> RequisitionAsync(Client api, Project p, decimal qty, decimal price, bool approve = true, string? contractId = null)
    {
        var r = await api.Post("/materialrequisitions", new
        {
            requestDate = DateTime.UtcNow, requiredDate = DateTime.UtcNow.AddDays(5), department = "المشاريع", requestedBy = "م. خالد", priority = "high",
            supplierId = p.Supplier, supplierName = "مورد", status = "draft", contractId = contractId ?? p.Id,
            items = new[] { new { itemId = p.Product, itemName = "أسمنت", sku = "CEM", unit = "PCS", requestedQuantity = qty, estimatedCost = price, boqItemId = p.Item } },
        });
        Ok(r, "requisition");
        var id = r.Data!["id"].S();
        if (approve) Ok(await api.Post($"/materialrequisitions/{id}/approve"), "approve requisition");
        return id;
    }

    private static Task<Res> ReceiveAsync(Client api, Project p, string requisitionId, decimal qty, decimal price = 0, Guid? product = null)
        => api.Post("/deliverynotes", new
        {
            type = "purchase_delivery", requisitionId, date = DateTime.UtcNow.Date,
            items = new[] { new { itemId = product ?? p.Product, itemName = "أسمنت", deliveredQty = qty, unitPrice = price, vatRate = 15 } },
        });

    private static async Task<Dictionary<string, decimal>> EntryAsync(Client api, string entryId)
        => (await api.Get($"/journalentries/{entryId}")).Data!["lines"]!.AsArray()
            .GroupBy(l => l!["accountCode"].S()).ToDictionary(g => g.Key, g => g.Sum(l => l!["debit"].D() - l!["credit"].D()));

    private static async Task<decimal> BalanceAsync(Client api, string code)
        => (await api.Get("/reports/TrialBalance")).Data!.AsArray()
            .Where(r => r!["accountCode"].S() == code)
            .Sum(r => r!["openingDebit"].D() - r!["openingCredit"].D() + r!["periodDebit"].D() - r!["periodCredit"].D());

    private static async Task<(string Wip, string Grni)> AccountsAsync(Client api)
    {
        var s = (await api.Get("/accounting-settings")).Data!;
        return (s["projectMaterialsWipAccountCode"].S(), s["projectGoodsReceivedNotInvoicedAccountCode"].S());
    }

    private static async Task<JsonNode> CustodyAsync(Client api, Project p, string custodyId)
        => (await api.Get($"/commercialcontracts/{p.Id}/material-issues/{custodyId}")).Data!;

    private static async Task<JsonNode> TrackingAsync(Client api, Project p)
        => (await api.Get($"/commercialcontracts/{p.Id}/material-tracking")).Data!["lines"]!.AsArray().Single(l => l!["productId"].G() == p.Product)!;

    private static async Task AssertGeneralWarehouseUntouchedAsync(Client api, Project p)
    {
        var product = (await api.Get($"/products/{p.Product}")).Data!;
        Assert.Equal(50m, product["currentStock"].D());
        Assert.Equal(10m, product["averageCost"].D());
    }

    // ---------- الاستلام عهدة للمشروع ----------

    [Fact]
    public async Task Project_goods_receipt_is_custody_at_purchase_price_and_never_enters_the_general_warehouse()
    {
        var (api, p) = await SetupAsync();
        var req = await RequisitionAsync(api, p, qty: 100, price: 12);

        // لا تحويل مباشر لفاتورة شراء للمستودع العام
        Status(409, await api.Post($"/materialrequisitions/{req}/ConvertToInvoice"), "direct conversion");

        var grn = await ReceiveAsync(api, p, req, 40);
        Ok(grn, "receive 40");
        var note = grn.Data!;
        Assert.Equal(p.Id, note["contractId"].S());
        Assert.Equal(p.Supplier, note["partyId"].G()); // المورد من الطلب
        Assert.Equal(12m, note["items"]![0]!["unitPrice"].D()); // السعر من تقدير الطلب
        Assert.Equal(p.Item, note["items"]![0]!["boqItemId"].G()); // البند من سطر الطلب

        // العهدة: بسعر الشراء على البند، وقيدها مدين المواد تحت التنفيذ / دائن بضاعة مستلمة لم تُفوتر
        var custody = await CustodyAsync(api, p, note["custodyIssueId"].S());
        Assert.Equal("purchase_receipt", custody["kind"].S()); Assert.Equal("posted", custody["status"].S());
        Assert.Equal(480m, custody["totalCost"].D());
        var (wip, grni) = await AccountsAsync(api);
        var entry = await EntryAsync(api, custody["journalEntryId"].S());
        Assert.Equal(480m, entry[wip]); Assert.Equal(-480m, entry[grni]);
        await AssertGeneralWarehouseUntouchedAsync(api, p);

        var line = await TrackingAsync(api, p);
        Assert.Equal(40m, line["purchasedQuantity"].D()); Assert.Equal(40m, line["deliveredQuantity"].D());

        // منع الاستلام المكرر: 60 متبقية من الطلب
        Status(400, await ReceiveAsync(api, p, req, 61), "exceeds the request");
        Ok(await ReceiveAsync(api, p, req, 60), "receive the rest");
        Status(400, await ReceiveAsync(api, p, req, 1), "request fully received");

        // العهدة تُختار في المستخلص كأي تسليم، ولا تُرجع للمستودع العام
        var options = (await api.Get($"/commercialcontracts/{p.Id}/certificates/material-options")).Data!.AsArray();
        var receiptLine = custody["lines"]![0]!["id"].G();
        Assert.Contains(options, o => o!["issueLineId"].G() == receiptLine);
        Status(400, await api.Post($"/commercialcontracts/{p.Id}/material-issues", new
        {
            kind = "return", receiverType = "site_engineer", receiverName = "م. خالد", post = true,
            lines = new[] { new { sourceLineId = receiptLine, quantity = 1m } },
        }), "return project custody to the general warehouse");
        await AssertGeneralWarehouseUntouchedAsync(api, p);
    }

    [Fact]
    public async Task Supplier_return_and_invoice_settle_the_receipt_accrual_without_billing_the_customer()
    {
        var (api, p) = await SetupAsync();
        var req = await RequisitionAsync(api, p, qty: 100, price: 12);
        var note = (await ReceiveAsync(api, p, req, 40)).Data!;
        var noteId = note["id"].S();
        var (wip, grni) = await AccountsAsync(api);

        // إرجاع للمورد من العهدة: مدين بضاعة مستلمة لم تُفوتر / دائن المواد تحت التنفيذ
        var ret = await api.Post("/deliverynotes/returns", new
        {
            type = "purchase_delivery_return", deliveryNoteId = noteId, date = DateTime.UtcNow.Date, returnReason = "تالف",
            items = new[] { new { itemId = p.Product, quantity = 10m } },
        });
        Ok(ret, "supplier return");
        var returnCustody = await CustodyAsync(api, p, ret.Data!["custodyIssueId"].S());
        Assert.Equal("receipt_return", returnCustody["kind"].S());
        var entry = await EntryAsync(api, returnCustody["journalEntryId"].S());
        Assert.Equal(120m, entry[grni]); Assert.Equal(-120m, entry[wip]);
        var line = await TrackingAsync(api, p);
        Assert.Equal(30m, line["deliveredQuantity"].D()); Assert.Equal(10m, line["returnedQuantity"].D());

        // فاتورة المورد من السند: الصافي 30 × 12 = 360 + ضريبة، تسوّي الاستحقاق ولا تمسّ المستودع ولا فوترة العميل
        var invoice = await api.Post($"/deliverynotes/{noteId}/Invoice");
        Ok(invoice, "supplier invoice");
        Assert.Equal(414m, invoice.Data!["grandTotal"].D());
        Assert.Equal(0m, await BalanceAsync(api, grni)); // −480 + 120 + 360
        Assert.Equal(360m, await BalanceAsync(api, wip));
        Assert.Equal(-414m, await BalanceAsync(api, p.SupplierAccount));
        await AssertGeneralWarehouseUntouchedAsync(api, p);
        Assert.Equal(0m, (await api.Get($"/commercialcontracts/{p.Id}")).Data!["totalInvoiced"].D());

        // المفوتر لا يُعدّل ولا يُحذف ولا يُرجع منه بالسند
        Status(409, await api.Delete($"/deliverynotes/{noteId}"), "delete invoiced receipt");
    }

    [Fact]
    public async Task Deleting_a_receipt_reverses_its_custody_unless_the_custody_is_already_used()
    {
        var (api, p) = await SetupAsync();
        var req = await RequisitionAsync(api, p, qty: 100, price: 12);
        var (wip, grni) = await AccountsAsync(api);

        var first = (await ReceiveAsync(api, p, req, 20)).Data!;
        Status(409, await api.Put($"/deliverynotes/{first["id"].S()}", new
        {
            type = "purchase_delivery", partyName = "مورد", date = DateTime.UtcNow.Date,
            items = new[] { new { itemId = p.Product, itemName = "أسمنت", deliveredQty = 25m, unitPrice = 12m, vatRate = 15 } },
        }), "edit posted receipt");
        Status(409, await api.Post($"/commercialcontracts/{p.Id}/material-issues/{first["custodyIssueId"].S()}/cancel", new { reason = "x" }), "cancel custody directly");
        Ok(await api.Delete($"/deliverynotes/{first["id"].S()}"), "delete receipt");
        Assert.Equal("cancelled", (await CustodyAsync(api, p, first["custodyIssueId"].S()))["status"].S());
        Assert.Equal(0m, await BalanceAsync(api, wip)); Assert.Equal(0m, await BalanceAsync(api, grni));
        Assert.Equal(0m, (await TrackingAsync(api, p))["deliveredQuantity"].D());

        // الكمية تعود للاستلام، والعهدة المستخدمة (هدر منها) تمنع حذف سندها
        var second = (await ReceiveAsync(api, p, req, 100)).Data!;
        var custody = await CustodyAsync(api, p, second["custodyIssueId"].S());
        Ok(await api.Post($"/commercialcontracts/{p.Id}/material-issues", new
        {
            kind = "waste", receiverType = "site_engineer", receiverName = "م. خالد", post = true,
            lines = new[] { new { sourceLineId = custody["lines"]![0]!["id"].G(), quantity = 5m } },
        }), "waste from custody");
        Status(409, await api.Delete($"/deliverynotes/{second["id"].S()}"), "delete used receipt");
        // الإرجاع للمورد لا يتجاوز المتاح في العهدة (100 − 5 مهدرة)
        Status(409, await api.Post("/deliverynotes/returns", new
        {
            type = "purchase_delivery_return", deliveryNoteId = second["id"].S(), date = DateTime.UtcNow.Date,
            items = new[] { new { itemId = p.Product, quantity = 96m } },
        }), "return more than custody");
    }

    [Fact]
    public async Task Receipts_require_an_approved_request_of_the_same_project_with_its_items_and_boq_lines()
    {
        var (api, p) = await SetupAsync();
        var draft = await RequisitionAsync(api, p, qty: 10, price: 12, approve: false);
        Status(409, await ReceiveAsync(api, p, draft, 5), "unapproved request");

        var req = await RequisitionAsync(api, p, qty: 10, price: 12);
        var other = await SeedProductAsync(api, "STL", stock: 0);
        Status(400, await ReceiveAsync(api, p, req, 5, product: other), "item outside the request");
        Status(400, await api.Post("/deliverynotes", new
        {
            type = "sales_delivery", requisitionId = req, partyName = "عميل", date = DateTime.UtcNow.Date,
            items = new[] { new { itemId = p.Product, itemName = "أسمنت", deliveredQty = 1m, unitPrice = 12m, vatRate = 15 } },
        }), "sales delivery on a purchase request");

        // بند المجموعة لا يُستلم له، وطلب المشروع لا يُحذف وله سندات
        Status(400, await api.Post("/materialrequisitions", new
        {
            requestDate = DateTime.UtcNow, requiredDate = DateTime.UtcNow.AddDays(5), department = "المشاريع", requestedBy = "م", priority = "high",
            supplierId = p.Supplier, supplierName = "مورد", status = "draft", contractId = p.Id,
            items = new[] { new { itemId = p.Product, itemName = "أسمنت", sku = "CEM", unit = "PCS", requestedQuantity = 1m, estimatedCost = 12m, boqItemId = p.Group } },
        }), "request line on a group item");
        Ok(await ReceiveAsync(api, p, req, 5), "receive");
        Status(409, await api.Delete($"/materialrequisitions/{req}"), "delete request with receipts");

        // سندات الاستلام العادية (بلا طلب مشروع) تبقى كما كانت: عقد المقاولات يُفوتر بالمستخلصات
        Status(409, await api.Post("/deliverynotes", new
        {
            type = "purchase_delivery", contractId = p.Id, partyName = "مورد", date = DateTime.UtcNow.Date,
            items = new[] { new { itemId = p.Product, itemName = "أسمنت", deliveredQty = 1m, unitPrice = 12m, vatRate = 15 } },
        }), "plain receipt on a BOQ contract");
        await AssertGeneralWarehouseUntouchedAsync(api, p);
    }
}
