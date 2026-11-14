using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// دورة مواد المشروع من الشراء حتى تكلفة الإيرادات:
/// طلب شراء للمشروع ← اعتماد ← فاتورة شراء (مخزون المستودع بالوارد أولاً) ← إذن تسليم على بند جدول الكميات
/// (مدين مواد مشاريع تحت التنفيذ / دائن المخزون) ← مستخلص يختار كميات الإذن ← فاتورته (العميل/الإيراد/الضريبة)
/// مع قيد التكلفة الفعلية (مدين تكلفة إيرادات العقود / دائن تحت التنفيذ). المخزون يُخفَّض مرة واحدة عند التسليم.
/// المثال: شراء 8 × 60 ثم 10 × 70، تسليم 11 = 480 + 3 × 70 = 690؛ مستخلص 10 × 100 = 1,000 + ضريبة 150.
/// </summary>
[Collection("api")]
public class ProjectMaterialCostFlowTests : TestBase
{
    public ProjectMaterialCostFlowTests(ErpFactory f) : base(f) { }

    private const string Revenue = "411";
    private const string OutputVat = "213";

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");
    private static void Rejected(Res r, string step, params int[] codes)
        => Assert.True((codes.Length == 0 ? new[] { 400, 409 } : codes).Contains(r.Status), $"{step}: expected rejection, got {r.Status} {r.Body?.ToJsonString()}");

    /// <summary>
    /// رصيد الحساب من ميزان المراجعة: الافتتاحي + حركة الفترة (عمود الختامي في التقرير يُقرَّب لخانة عشرية واحدة، فلا يصلح لهللات).
    /// </summary>
    private static async Task<decimal> BalanceAsync(Client api, string code)
        => (await api.Get("/reports/TrialBalance")).Data!.AsArray()
            .Where(r => r!["accountCode"].S() == code)
            .Sum(r => r!["openingDebit"].D() - r!["openingCredit"].D() + r!["periodDebit"].D() - r!["periodCredit"].D());

    /// <summary>صافي كل حساب في القيد: موجب مدين، سالب دائن.</summary>
    private static async Task<Dictionary<string, decimal>> EntryAsync(Client api, string entryId)
        => (await api.Get($"/journalentries/{entryId}")).Data!["lines"]!.AsArray()
            .GroupBy(l => l!["accountCode"].S()).ToDictionary(g => g.Key, g => g.Sum(l => l!["debit"].D() - l!["credit"].D()));

    private static async Task AssertInventoryReconciledAsync(Client api, decimal expected)
    {
        var items = (await api.Get("/reports/InventoryReconciliation")).Data!["rows"]!.AsArray().Single(r => r!["key"].S() == "items")!;
        Assert.True(items["difference"].D() == 0, items.ToJsonString());
        Assert.Equal(expected, items["ledgerValue"].D());
        Assert.Equal(expected, await BalanceAsync(api, MainSiteInventory));
    }

    private sealed record Project(string Id, Guid Item, Guid Group, Guid SubItem, Guid Product, Guid Supplier, string CustomerAccount);

    /// <summary>
    /// منشأة بالوارد أولاً، صنف أسمنت مربوط بمادة مكتبة، عقد مقاولات مبسّط لعميل (بلا دفعة ولا ضمان، ضريبة 15%):
    /// بند «صب» 10 × 100، ومجموعة بفرعي. احتياج معتمد للأسمنت على البند <paramref name="required"/> قطعة.
    /// </summary>
    private static async Task<Project> SetupAsync(Client api, decimal required = 20)
    {
        Ok(await api.Put("/costing/policy", new { method = "fifo", recalculateOnNewPurchase = true, includeFreightAndCustoms = true, negativeInventoryPolicy = "prohibit", standardCostVarianceAccountCode = "513" }), "fifo");
        var product = await SeedProductAsync(api, "CEM", stock: 0);
        var (supplier, _) = await SeedSupplierAsync(api);
        var cement = await api.Post("/construction-resources", new { nameAr = "أسمنت", resourceType = "material", unit = "PCS", costSource = "product", productId = product });
        Ok(cement, "resource");
        var (customer, customerAccount) = await SeedCustomerAsync(api);
        var created = await api.Post("/commercialcontracts", new
        {
            title = "مشروع صب", contractType = "construction_boq", workflow = "simple", requiresGuarantee = false, partyType = "customer", partyId = customer, partyName = "عميل",
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
        var item = Line("صب");

        Ok(await api.Put($"/commercialcontracts/{id}/material-requirements", new
        {
            lines = new object[] { new { boqItemId = item, resourceId = cement.Data!["id"].G(), source = "manual", baselineQuantity = required } },
        }), "plan");
        Ok(await api.Post($"/commercialcontracts/{id}/material-requirements/submit"), "plan submit");
        Ok(await api.Post($"/commercialcontracts/{id}/material-requirements/approve"), "plan approve");
        return new Project(id, item, Line("مجموعة"), Line("فرعي"), product, supplier, customerAccount);
    }

    /// <summary>
    /// شراء للمستودع العام (طلب عام ← اعتماد ← تحويل لفاتورة شراء ← ترحيلها)، ثم يُسلَّم للمشروع بإذن صرف صريح. طلب المشروع نفسه
    /// لا يدخل المستودع العام: يُستلم بسند استلام للمشروع (ProjectPurchaseIsolationTests).
    /// </summary>
    private static async Task BuyViaRequisitionAsync(Client api, Project p, decimal qty, decimal cost)
    {
        var req = await api.Post("/materialrequisitions", new
        {
            requestDate = DateTime.UtcNow, requiredDate = DateTime.UtcNow.AddDays(5), department = "المشتريات", requestedBy = "م. خالد", priority = "high",
            supplierId = p.Supplier, supplierName = "مورد", status = "draft", deliveryLocation = "project_site", deliveryAddress = "الموقع",
            items = new[] { new { itemId = p.Product, itemName = "أسمنت", sku = "CEM", unit = "PCS", requestedQuantity = qty, estimatedCost = cost } },
        });
        Ok(req, "requisition");
        // «موقع المشروع» وصفي بلا أثر فأُلغي: الطلب للمستودع دائماً، والتسليم للمشروع بإذن
        Assert.Equal("warehouse", req.Data!["deliveryLocation"].S());
        var reqId = req.Data["id"].S();
        Ok(await api.Post($"/materialrequisitions/{reqId}/approve"), "requisition approve");
        var draft = await api.Post($"/materialrequisitions/{reqId}/ConvertToInvoice");
        Ok(draft, "convert");
        Ok(await api.Post($"/purchase-invoices/{draft.Data!["id"].S()}/post"), "purchase post");
    }

    private static async Task<JsonNode> DeliverAsync(Client api, Project p, decimal qty, Guid? boq = null, string step = "deliver")
    {
        var r = await api.Post($"/commercialcontracts/{p.Id}/material-issues", new
        {
            receiverType = "site_engineer", receiverName = "م. خالد", post = true,
            lines = new[] { new { boqItemId = boq ?? p.Item, productId = p.Product, quantity = qty } },
        });
        Ok(r, step);
        return r.Data!;
    }

    private static object Certificate(Guid item, decimal qty, params (Guid Line, decimal Qty)[] materials) => new
    {
        kind = "interim", certificateDate = DateTime.UtcNow.Date,
        lines = new[] { new { contractItemId = item, currentQuantity = qty } },
        deductions = Array.Empty<object>(),
        materials = materials.Select(m => new { issueLineId = m.Line, quantity = m.Qty }).ToArray(),
    };

    /// <summary>مستخلص مبسّط يُفوتر مباشرة؛ يعيد المستخلص بعد الفوترة.</summary>
    private async Task<JsonNode> IssueCertificateAsync(Client api, Project p, object body, string step)
    {
        var cert = await api.Post($"/commercialcontracts/{p.Id}/certificates", body);
        Ok(cert, step);
        var certId = cert.Data!["id"].S();
        await AttachCertificateEvidenceAsync(api, p.Id, certId);
        Ok(await api.Post($"/commercialcontracts/{p.Id}/certificates/{certId}/issue"), step + " issue");
        return (await api.Get($"/commercialcontracts/{p.Id}/certificates/{certId}")).Data!;
    }

    private static async Task<(string Wip, string Cost)> AccountsAsync(Client api)
    {
        var s = (await api.Get("/accounting-settings")).Data!;
        return (s["projectMaterialsWipAccountCode"].S(), s["contractCostOfRevenueAccountCode"].S());
    }

    private static async Task<JsonArray> OptionsAsync(Client api, Project p) => (await api.Get($"/commercialcontracts/{p.Id}/certificates/material-options")).Data!.AsArray();

    private static async Task<JsonNode> BoqRowAsync(Client api, Project p)
        => (await api.Get($"/commercialcontracts/{p.Id}/material-tracking/boq")).Data!.AsArray().Single(r => r!["contractItemId"].G() == p.Item)!;

    // ---------- الدورة الكاملة ----------

    [Fact]
    public async Task Delivery_moves_fifo_cost_to_wip_and_the_certificate_invoice_recognizes_it_once()
    {
        var api = await NewConstructionTenantAsync();
        var p = await SetupAsync(api);
        await BuyViaRequisitionAsync(api, p, 8, 60);
        await BuyViaRequisitionAsync(api, p, 10, 70);
        await AssertInventoryReconciledAsync(api, 1180m);

        // التسليم: 8 × 60 + 3 × 70 = 690 بالوارد أولاً، إلى مواد تحت التنفيذ لا إلى مصروف
        var issue = await DeliverAsync(api, p, 11);
        Assert.Equal(690m, issue["totalCost"].D());
        var (wip, cost) = await AccountsAsync(api);
        Assert.Equal(wip, issue["debitAccountCode"].S());
        Assert.Equal(p.Item, issue["lines"]![0]!["boqItemId"].G());
        var delivery = await EntryAsync(api, issue["journalEntryId"].S());
        Assert.Equal(690m, delivery[wip]);
        Assert.Equal(-690m, delivery[MainSiteInventory]);
        Assert.Equal(2, delivery.Count);
        await AssertInventoryReconciledAsync(api, 490m);
        Assert.Equal(690m, await BalanceAsync(api, wip));
        Assert.Equal(0m, await BalanceAsync(api, "517")); // لا مصروف عند التسليم
        Assert.Equal(0m, await BalanceAsync(api, cost));

        var row = await BoqRowAsync(api, p);
        Assert.Equal(20m, row["plannedQuantity"].D());
        Assert.Equal(11m, row["deliveredQuantity"].D());
        Assert.Equal(9m, row["remainingQuantity"].D());
        Assert.Equal(690m, row["outstandingCost"].D());

        // المستخلص: 10 × 100 = 1,000 + ضريبة 150، ويختار كمية الإذن كاملة بتكلفتها الفعلية
        var line = issue["lines"]![0]!["id"].G();
        var option = (await OptionsAsync(api, p)).Single()!;
        Assert.Equal(issue["issueNumber"].S(), option["issueNumber"].S());
        Assert.Equal(11m, option["remainingQuantity"].D());
        Assert.Equal(690m, option["remainingCost"].D());
        var cert = await IssueCertificateAsync(api, p, Certificate(p.Item, 10, (line, 11)), "certificate");
        Assert.Equal("invoiced", cert["status"].S());
        Assert.Equal(690m, cert["materialCost"].D());
        Assert.Equal(1000m, cert["taxableAmount"].D());
        Assert.Equal(150m, cert["vatAmount"].D());
        Assert.Equal(issue["issueNumber"].S(), cert["materials"]![0]!["issueNumber"].S());

        var recognition = await EntryAsync(api, cert["costJournalEntryId"].S());
        Assert.Equal(690m, recognition[cost]);
        Assert.Equal(-690m, recognition[wip]);
        Assert.Equal(2, recognition.Count); // لا مخزون في قيد المستخلص

        Assert.Equal(1150m, await BalanceAsync(api, p.CustomerAccount));
        Assert.Equal(-1000m, await BalanceAsync(api, Revenue));
        Assert.Equal(-150m, await BalanceAsync(api, OutputVat));
        Assert.Equal(690m, await BalanceAsync(api, cost));
        Assert.Equal(0m, await BalanceAsync(api, wip));
        await AssertInventoryReconciledAsync(api, 490m); // المخزون دائن مرة واحدة فقط

        row = await BoqRowAsync(api, p);
        Assert.Equal(690m, row["invoicedCost"].D());
        Assert.Equal(0m, row["outstandingCost"].D());
        Assert.Empty(await OptionsAsync(api, p));
    }

    // ---------- الفوترة الجزئية ومنع التكرار والإلغاء ----------

    [Fact]
    public async Task Partial_billing_never_bills_a_delivery_twice_and_cancelling_the_invoice_restores_it()
    {
        var api = await NewConstructionTenantAsync();
        var p = await SetupAsync(api);
        await BuyViaRequisitionAsync(api, p, 8, 60);
        await BuyViaRequisitionAsync(api, p, 10, 70);
        var issue = await DeliverAsync(api, p, 11);
        var line = issue["lines"]![0]!["id"].G();
        var (wip, cost) = await AccountsAsync(api);

        // مستخلص 1: 4 × 100 بخمس قطع من الإذن (5 × 62.7273 = 313.64)
        var first = await IssueCertificateAsync(api, p, Certificate(p.Item, 4, (line, 5)), "certificate 1");
        Assert.Equal(313.64m, first["materialCost"].D());
        var option = (await OptionsAsync(api, p)).Single()!;
        Assert.Equal(5m, option["billedQuantity"].D());
        Assert.Equal(6m, option["remainingQuantity"].D());
        Assert.Equal(376.36m, option["remainingCost"].D());

        // مستخلص 2: أكثر من المتبقي مرفوض؛ المتبقي كاملاً يأخذ باقي التكلفة بلا كسر
        Rejected(await api.Post($"/commercialcontracts/{p.Id}/certificates", Certificate(p.Item, 5, (line, 7))), "over-select", 400);
        var second = await IssueCertificateAsync(api, p, Certificate(p.Item, 5, (line, 6)), "certificate 2");
        Assert.Equal(376.36m, second["materialCost"].D());
        Assert.Empty(await OptionsAsync(api, p));
        Rejected(await api.Post($"/commercialcontracts/{p.Id}/certificates", Certificate(p.Item, 1, (line, 1))), "duplicate billing", 400);
        Assert.Equal(690m, await BalanceAsync(api, cost));
        Assert.Equal(0m, await BalanceAsync(api, wip));
        Assert.Equal(-900m, await BalanceAsync(api, Revenue));

        // الإذن لا يُلغى ما دامت كمياته في مستخلص، وفاتورة المستخلص لا تُلغى بمرتجع حر
        Rejected(await api.Post($"/commercialcontracts/{p.Id}/material-issues/{issue["id"].S()}/cancel", new { reason = "خطأ" }), "cancel certified delivery", 409);
        Rejected(await api.PostReturn(new { originalInvoiceId = second["invoiceId"].S(), returnReason = "إلغاء" }), "free return", 409);
        // الأقدم لا يُلغى قبل اللاحق
        Rejected(await api.Post($"/commercialcontracts/{p.Id}/certificates/{first["id"].S()}/cancel-invoice", new { note = "خطأ" }), "cancel older", 409);

        // إلغاء فاتورة المستخلص 2: إشعار دائن يعكس الإيراد والضريبة، وقيد التكلفة يُعكس فتعود الكمية والتكلفة لتحت التنفيذ
        var cancelled = await api.Post($"/commercialcontracts/{p.Id}/certificates/{second["id"].S()}/cancel-invoice", new { note = "خطأ في القياس" });
        Ok(cancelled, "cancel invoice 2");
        Assert.Equal("cancelled", cancelled.Data!["status"].S());
        Assert.False(string.IsNullOrEmpty(cancelled.Data["creditNoteNumber"].S()));
        Assert.Equal(376.36m, await BalanceAsync(api, wip));
        Assert.Equal(313.64m, await BalanceAsync(api, cost));
        Assert.Equal(-400m, await BalanceAsync(api, Revenue));
        Assert.Equal(-60m, await BalanceAsync(api, OutputVat));
        Assert.Equal(460m, await BalanceAsync(api, p.CustomerAccount));
        Assert.Equal(6m, (await OptionsAsync(api, p)).Single()!["remainingQuantity"].D());
        Rejected(await api.Post($"/commercialcontracts/{p.Id}/certificates/{second["id"].S()}/cancel-invoice", new { note = "مرة ثانية" }), "double cancel", 409);
        var progress = (await api.Get($"/commercialcontracts/{p.Id}/boq-progress")).Data!;
        Assert.Equal(460m, progress["invoicedTotal"].D());

        // إلغاء المستخلص 1 ثم الإذن: كل شيء يعود للمخزون، ولا أثر في الإيراد أو التكلفة أو تحت التنفيذ
        Ok(await api.Post($"/commercialcontracts/{p.Id}/certificates/{first["id"].S()}/cancel-invoice", new { note = "خطأ" }), "cancel invoice 1");
        Assert.Equal(690m, await BalanceAsync(api, wip));
        Assert.Equal(0m, await BalanceAsync(api, cost));
        Assert.Equal(0m, await BalanceAsync(api, Revenue));
        Assert.Equal(0m, await BalanceAsync(api, OutputVat));
        Assert.Equal(0m, await BalanceAsync(api, p.CustomerAccount));
        Ok(await api.Post($"/commercialcontracts/{p.Id}/material-issues/{issue["id"].S()}/cancel", new { reason = "إرجاع للمستودع" }), "cancel delivery");
        Assert.Equal(0m, await BalanceAsync(api, wip));
        await AssertInventoryReconciledAsync(api, 1180m);
    }

    // ---------- التحقق ----------

    [Fact]
    public async Task Deliveries_and_selections_are_validated_against_the_boq_and_the_approved_plan()
    {
        var api = await NewConstructionTenantAsync();
        var p = await SetupAsync(api, required: 20);
        await BuyViaRequisitionAsync(api, p, 20, 50);
        var url = $"/commercialcontracts/{p.Id}/material-issues";
        object Body(Guid? boq, decimal qty) => new
        {
            receiverType = "site_engineer", receiverName = "م. خالد", post = true,
            lines = new[] { new { boqItemId = boq, productId = p.Product, quantity = qty } },
        };

        Rejected(await api.Post(url, Body(null, 5)), "no boq", 400);
        Rejected(await api.Post(url, Body(p.Group, 5)), "group item", 400);
        Rejected(await api.Post(url, Body(Guid.NewGuid(), 5)), "foreign item", 400);
        var other = await SetupAsync(await NewConstructionTenantAsync()); // بند من منشأة/عقد آخر
        Rejected(await api.Post(url, Body(other.Item, 5)), "other contract item", 400);
        Rejected(await api.Post(url, Body(p.Item, 21)), "over the approved requirement", 400);

        // تسليم على الفرعي (خارج تخطيط البند) مقبول ضمن حد الصنف المعتمد، ويظهر تنبيهاً في متابعة البنود
        var onSub = await DeliverAsync(api, p, 2, p.SubItem, "deliver on sub item");
        var subRow = (await api.Get($"/commercialcontracts/{p.Id}/material-tracking/boq")).Data!.AsArray().Single(r => r!["contractItemId"].G() == p.SubItem)!;
        Assert.True(subRow["overPlanned"]!.GetValue<bool>());
        var issue = await DeliverAsync(api, p, 10);
        var line = issue["lines"]![0]!["id"].G();

        // لا تُختار مواد بند بلا أعمال مقيسة، ولا كمية سالبة
        Rejected(await api.Post($"/commercialcontracts/{p.Id}/certificates", new
        {
            kind = "interim", certificateDate = DateTime.UtcNow.Date,
            lines = new[] { new { contractItemId = p.SubItem, currentQuantity = 1m } }, deductions = Array.Empty<object>(),
            materials = new[] { new { issueLineId = line, quantity = 1m } },
        }), "material on unmeasured item", 400);
        Rejected(await api.Post($"/commercialcontracts/{p.Id}/certificates", Certificate(p.Item, 1, (line, -1))), "negative", 400);

        // التعديل بلا مواد مرسلة يبقيها، وإلغاء المسودة يعيد الكمية متاحة
        var draft = await api.Post($"/commercialcontracts/{p.Id}/certificates", Certificate(p.Item, 1, (line, 4)));
        Ok(draft, "draft");
        var id = draft.Data!["id"].S();
        await AttachCertificateEvidenceAsync(api, p.Id, id);
        var kept = await api.Put($"/commercialcontracts/{p.Id}/certificates/{id}", new
        {
            kind = "interim", certificateDate = DateTime.UtcNow.Date, lines = new[] { new { contractItemId = p.Item, currentQuantity = 2m } }, deductions = Array.Empty<object>(),
        });
        Ok(kept, "update without materials");
        Assert.Equal(4m, kept.Data!["materials"]![0]!["quantity"].D());
        Assert.Equal(6m, (await OptionsAsync(api, p)).Single(o => o!["issueLineId"].G() == line)!["remainingQuantity"].D());
        Ok(await api.Post($"/commercialcontracts/{p.Id}/certificates/{id}/cancel", new { note = "إلغاء" }), "cancel draft");
        Assert.Equal(10m, (await OptionsAsync(api, p)).Single(o => o!["issueLineId"].G() == line)!["remainingQuantity"].D());
        Assert.Equal(onSub["lines"]![0]!["boqItemId"].G(), p.SubItem);
    }

    // ---------- شاشة المستندات المستقلة ----------

    [Fact]
    public async Task Delivery_notes_list_across_projects_is_paged_filtered_and_isolated()
    {
        var api = await NewConstructionTenantAsync();
        var p = await SetupAsync(api);
        await BuyViaRequisitionAsync(api, p, 20, 50);
        var posted = await DeliverAsync(api, p, 4);
        var draft = await api.Post($"/commercialcontracts/{p.Id}/material-issues", new
        {
            receiverType = "supervisor", receiverName = "مشرف", post = false,
            lines = new[] { new { boqItemId = p.Item, productId = p.Product, quantity = 2m } },
        });
        Ok(draft, "draft");

        // المستند للطباعة يحمل المشروع والمستودع والعميل
        var doc = (await api.Get($"/commercialcontracts/{p.Id}/material-issues/{posted["id"].S()}")).Data!;
        Assert.False(string.IsNullOrEmpty(doc["contractNumber"].S()));
        Assert.Equal("مشروع صب", doc["projectName"].S());
        Assert.False(string.IsNullOrEmpty(doc["warehouseName"].S()));
        Assert.False(string.IsNullOrEmpty(doc["lines"]![0]!["boqDescription"].S()));

        var all = (await api.Get("/project-material-issues/load?requireTotalCount=true&take=1&skip=0")).Body!;
        Assert.Equal(2, all["totalCount"]!.GetValue<int>());
        Assert.Single(all["data"]!.AsArray()); // صفحة واحدة من السرفر
        var filter = Uri.EscapeDataString("[\"status\",\"=\",\"posted\"]");
        var onlyPosted = (await api.Get($"/project-material-issues/load?requireTotalCount=true&filter={filter}")).Body!;
        var row = onlyPosted["data"]!.AsArray().Single()!;
        Assert.Equal(posted["issueNumber"].S(), row["issueNumber"].S());
        Assert.Equal(doc["contractNumber"].S(), row["contractNumber"].S());
        Assert.Equal(1, row["lineCount"]!.GetValue<int>());
        Assert.Equal(200m, row["totalCost"].D());

        // منشأة أخرى لا ترى أذونات هذه
        var other = await NewConstructionTenantAsync();
        Assert.Equal(0, (await other.Get("/project-material-issues/load?requireTotalCount=true")).Body!["totalCount"]!.GetValue<int>());
    }

    // ---------- التزامن ----------

    [Fact]
    public async Task Concurrent_submissions_never_select_the_same_delivery_twice()
    {
        var api = await NewConstructionTenantAsync();
        var p = await SetupAsync(api);
        await BuyViaRequisitionAsync(api, p, 20, 50);
        var issue = await DeliverAsync(api, p, 10);
        var line = issue["lines"]![0]!["id"].G();

        // مستخلصان بالتوازي على الكمية نفسها: واحد فقط يُحفظ
        var both = await Task.WhenAll(
            api.Post($"/commercialcontracts/{p.Id}/certificates", Certificate(p.Item, 1, (line, 10))),
            api.Post($"/commercialcontracts/{p.Id}/certificates", Certificate(p.Item, 1, (line, 10))));
        Assert.Equal(1, both.Count(r => r.Status is 200 or 201));
        var saved = both.Single(r => r.Status is 200 or 201).Data!["id"].S();
        Ok(await api.Post($"/commercialcontracts/{p.Id}/certificates/{saved}/cancel", new { note = "اختبار" }), "cancel draft");

        // اختيار الكمية وإلغاء الإذن بالتوازي: لا يمرّان معاً
        var race = await Task.WhenAll(
            api.Post($"/commercialcontracts/{p.Id}/certificates", Certificate(p.Item, 1, (line, 10))),
            api.Post($"/commercialcontracts/{p.Id}/material-issues/{issue["id"].S()}/cancel", new { reason = "تزامن" }));
        Assert.False(race[0].Status is 200 or 201 && race[1].Status == 200, $"{race[0].Body?.ToJsonString()} | {race[1].Body?.ToJsonString()}");
        var status = (await api.Get($"/commercialcontracts/{p.Id}/material-issues/{issue["id"].S()}")).Data!["status"].S();
        var selected = (await OptionsAsync(api, p)).Sum(o => o!["billedQuantity"].D());
        Assert.True(status == "posted" || selected == 0, $"issue {status} but {selected} selected");
    }
}
