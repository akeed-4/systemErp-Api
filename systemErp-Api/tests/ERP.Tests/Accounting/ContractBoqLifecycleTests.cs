using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;
using Xunit.Abstractions;

namespace ERP.Tests;

/// <summary>
/// دورة عقد مقاولات بجدول كميات على المسار المبسط من الإنشاء حتى الإغلاق:
/// قيمة 100,000 (بندان)، دفعة مقدمة 10%، ضمان حسن تنفيذ 5%، ضريبة 15%:
/// تفعيل مباشر ← فاتورة الدفعة ← مستخلص جارٍ 40% ← مستخلص ختامي 60% ← استلام أولي ← إفراج عن الضمان ← إغلاق.
/// كل خطوة تفحص أرقامها وقيودها، والنهاية تفحص أن حسابات العقد الوسيطة صُفّرت وأن الذمة = القيمة + الضريبة.
/// </summary>
[Collection("api")]
public class ContractBoqLifecycleTests : TestBase
{
    private readonly ITestOutputHelper _out;
    public ContractBoqLifecycleTests(ErpFactory f, ITestOutputHelper output) : base(f) => _out = output;

    private const string AdvancesReceived = "218";   // التزام
    private const string RetentionReceivable = "1132"; // أصل (تحت «ضريبة وأرصدة مدينة»)
    private const string Revenue = "411";
    private const string OutputVat = "213";

    private static Task<Res> Move(Client api, string id, string to, string? note = null)
        => api.Post($"/commercialcontracts/{id}/move", new { toStatus = to, note });

    /// <summary>رصيد الحساب الختامي (مدين − دائن) من ميزان المراجعة.</summary>
    private static async Task<decimal> BalanceAsync(Client api, string code)
    {
        var rows = (await api.Get("/reports/TrialBalance")).Data!.AsArray();
        return rows.Where(r => r!["accountCode"].S() == code).Sum(r => r!["closingDebit"].D() - r!["closingCredit"].D());
    }

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");

    private static object Certificate(string kind, Guid a, decimal qa, Guid b, decimal qb) => new
    {
        kind, certificateDate = DateTime.UtcNow.Date,
        lines = new[] { new { contractItemId = a, currentQuantity = qa }, new { contractItemId = b, currentQuantity = qb } },
        deductions = Array.Empty<object>(),
    };

    // ---------- تحصيل وسداد المستخلصات (المسار المبسط والكامل) ----------

    /// <summary>عقد مقاولات بجدول كميات (بند واحد 100 × 100 = 10,000) مفعّل على المسار المطلوب.</summary>
    private async Task<(string Id, Guid Item)> ActiveBoqAsync(Client api, string workflow, string partyType, Guid party)
    {
        var created = await api.Post("/commercialcontracts", new
        {
            title = "عقد", contractType = "construction_boq", workflow, requiresGuarantee = false,
            // ضمان بنكي اختياري: بيانات الضمان تُمسح مع requiresGuarantee = false
            guaranteeNumber = workflow == "full" ? "LG-1" : null, guaranteeAmount = workflow == "full" ? 500m : (decimal?)null,
            guaranteeExpiry = workflow == "full" ? DateTime.UtcNow.Date.AddYears(1) : (DateTime?)null,
            partyType, partyId = party, partyName = "طرف", startDate = DateTime.UtcNow.Date, endDate = DateTime.UtcNow.Date.AddMonths(3),
            vatRate = 15, advancePercent = 10, retentionPercent = 5,
            items = new[] { new { description = "بند", unit = "م", quantity = 100, unitPrice = 100, vatRate = 15, lineKind = "measured" } },
            milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
        });
        Ok(created, "create");
        var id = created.Data!["id"].S();
        if (workflow == "full")
        {
            Ok(await Move(api, id, "review"), "review");
            Ok(await api.Post($"/commercialcontracts/{id}/review", new { kind = "legal", decision = "approve" }), "legal");
            Ok(await api.Post($"/commercialcontracts/{id}/review", new { kind = "financial", decision = "approve" }), "financial");
            Assert.Equal(200, await UploadFileAsync(api, $"/commercialcontracts/{id}/attachments?kind=signed_copy", "signed.pdf", SmallPdf));
        }
        Ok(await Move(api, id, "active"), "activate");
        return (id, created.Data["items"]![0]!["id"].G());
    }

    /// <summary>مستخلص جارٍ معتمد ومفوتر بكمية <paramref name="qty"/>؛ يعيد معرّف فاتورته.</summary>
    private async Task<(string CertificateId, string InvoiceId, decimal Net)> BilledCertificateAsync(Client api, string id, Guid item, decimal qty)
    {
        var cert = await api.Post($"/commercialcontracts/{id}/certificates", new
        {
            kind = "interim", certificateDate = DateTime.UtcNow.Date, lines = new[] { new { contractItemId = item, currentQuantity = qty } },
            deductions = Array.Empty<object>(),
        });
        Ok(cert, "certificate");
        var certId = cert.Data!["id"].S();
        await AttachCertificateEvidenceAsync(api, id, certId);
        Ok(await api.Post($"/commercialcontracts/{id}/certificates/{certId}/submit"), "submit");
        Ok(await api.Post($"/commercialcontracts/{id}/certificates/{certId}/approve"), "approve");
        var bill = await api.Post($"/commercialcontracts/{id}/certificates/{certId}/bill");
        Ok(bill, "bill");
        return (certId, bill.Data!["id"].S(), cert.Data["netPayable"].D());
    }

    // ---------- أرشيف البنود الإلزامي ----------

    [Fact]
    public async Task Certificate_items_with_progress_require_an_archived_document_before_save_submit_and_approve()
    {
        var api = await NewConstructionTenantAsync();
        var (party, _) = await SeedCustomerAsync(api);
        var created = await api.Post("/commercialcontracts", new
        {
            title = "عقد", contractType = "construction_boq", workflow = "simple", requiresGuarantee = false, partyType = "customer", partyId = party, partyName = "طرف",
            startDate = DateTime.UtcNow.Date, endDate = DateTime.UtcNow.Date.AddMonths(3), vatRate = 15,
            items = new[]
            {
                new { description = "حفر", unit = "م3", quantity = 100, unitPrice = 100, vatRate = 15, lineKind = "measured" },
                new { description = "ردم", unit = "م3", quantity = 100, unitPrice = 50, vatRate = 15, lineKind = "measured" },
            },
            milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
        });
        Ok(created, "create");
        var id = created.Data!["id"].S();
        var (dig, fill) = (created.Data["items"]![0]!["id"].G(), created.Data["items"]![1]!["id"].G());
        Ok(await Move(api, id, "active"), "activate");

        // الإنشاء الأول بلا معرّف بعد: الشاشة ترفع المستندات بعده، والخادم يمنع التقديم
        var cert = await api.Post($"/commercialcontracts/{id}/certificates", Certificate("interim", dig, 10, fill, 0));
        Ok(cert, "create draft");
        var certId = cert.Data!["id"].S();
        var submit = await api.Post($"/commercialcontracts/{id}/certificates/{certId}/submit");
        Assert.Equal(400, submit.Status);
        Assert.Contains("حفر", submit.Body!["message"].S());
        Assert.DoesNotContain("ردم", submit.Body!["message"].S()); // بلا كمية حالية: لا يلزمه أرشيف

        // تعديل المسودة القائمة بكمية على بند بلا أرشيف: يُرفض بالبنود الناقصة كلها
        var update = await api.Put($"/commercialcontracts/{id}/certificates/{certId}", Certificate("interim", dig, 10, fill, 5));
        Assert.Equal(400, update.Status);
        Assert.Equal(2, update.Body!["errors"]!.AsArray().Count);

        // بعد الإرفاق: الحفظ والتقديم والاعتماد
        Assert.Equal(200, await UploadFileAsync(api, $"/commercialcontracts/{id}/certificates/{certId}/items/{dig}/attachments", "dig.pdf", SmallPdf));
        Assert.Equal(200, await UploadFileAsync(api, $"/commercialcontracts/{id}/certificates/{certId}/items/{fill}/attachments", "fill.pdf", SmallPdf));
        Ok(await api.Put($"/commercialcontracts/{id}/certificates/{certId}", Certificate("interim", dig, 10, fill, 5)), "update with archive");
        Ok(await api.Post($"/commercialcontracts/{id}/certificates/{certId}/submit"), "submit");

        // حذف مستند بعد التقديم (بصلاحية الحذف) يمنع الاعتماد حتى يُرفق غيره
        var docs = (await api.Get($"/commercialcontracts/{id}/certificates/{certId}/attachments?itemId={fill}")).Data!.AsArray();
        Ok(await api.Delete($"/commercialcontracts/{id}/certificates/{certId}/attachments/{docs[0]!["id"].S()}"), "delete archive");
        var approve = await api.Post($"/commercialcontracts/{id}/certificates/{certId}/approve");
        Assert.Equal(400, approve.Status);
        Assert.Contains("ردم", approve.Body!["message"].S());
        Assert.Equal(200, await UploadFileAsync(api, $"/commercialcontracts/{id}/certificates/{certId}/items/{fill}/attachments", "fill2.pdf", SmallPdf));
        Ok(await api.Post($"/commercialcontracts/{id}/certificates/{certId}/approve"), "approve");
    }

    private static object Payment(string invoiceId, decimal amount) => new { invoiceId, amount, treasuryAccountCode = "1111", referenceNumber = "TRX-1" };

    private static async Task<decimal> DueAsync(Client api, string id, string certId)
        => (await api.Get($"/commercialcontracts/{id}/certificates/{certId}")).Data!["amountDue"].D();

    [Theory]
    [InlineData("simple")]
    [InlineData("full")]
    public async Task Customer_certificates_are_collected_partially_then_fully_on_either_path(string workflow)
    {
        var api = await NewConstructionTenantAsync();
        var (customer, customerAccount) = await SeedCustomerAsync(api, creditLimit: 1_000_000);
        var (id, item) = await ActiveBoqAsync(api, workflow, "customer", customer);

        // 40 × 100 = 4,000 (بلا دفعة مقدمة مفوترة فلا استرداد): ضريبة 600، ضمان 200 ← الصافي المستحق 4,400
        var (certId, invoiceId, net) = await BilledCertificateAsync(api, id, item, 40);
        Assert.Equal(4_400m, net);
        Assert.Equal(4_400m, await DueAsync(api, id, certId));

        var partial = await api.Post($"/commercialcontracts/{id}/payments", Payment(invoiceId, 1_000));
        Ok(partial, "partial collection");
        Assert.Equal("Receipt", partial.Data!["type"].S(), ignoreCase: true);
        Assert.Equal(3_400m, await DueAsync(api, id, certId));

        Assert.Equal(400, (await api.Post($"/commercialcontracts/{id}/payments", Payment(invoiceId, 3_500))).Status); // يتجاوز المتبقي
        Ok(await api.Post($"/commercialcontracts/{id}/payments", Payment(invoiceId, 3_400)), "full collection");
        Assert.Equal(0m, await DueAsync(api, id, certId));
        Assert.Equal(4_400m, (await api.Get($"/commercialcontracts/{id}/boq-progress")).Data!["collectedTotal"].D());

        // ذمة العميل بعد التحصيل: الضمان المحتجز لم يُحمَّل عليه بعد، فالرصيد صفر
        Assert.Equal(0m, await BalanceAsync(api, customerAccount));
        var (d, c) = Totals(await api.Get("/reports/TrialBalance"));
        Assert.Equal(d, c);
    }

    [Theory]
    [InlineData("simple", 200)]
    [InlineData("full", 409)]
    public async Task Simplified_contract_bills_a_certificate_directly_while_full_path_keeps_approval(string workflow, int expected)
    {
        var api = await NewConstructionTenantAsync();
        var (customer, _) = await SeedCustomerAsync(api, creditLimit: 1_000_000);
        var (id, item) = await ActiveBoqAsync(api, workflow, "customer", customer);
        var draft = await api.Post($"/commercialcontracts/{id}/certificates", new
        {
            kind = "interim", certificateDate = DateTime.UtcNow.Date, lines = new[] { new { contractItemId = item, currentQuantity = 50m } },
            deductions = Array.Empty<object>(),
        });
        Ok(draft, "draft");
        var certId = draft.Data!["id"].S();

        await AttachCertificateEvidenceAsync(api, id, certId);
        var issued = await api.Post($"/commercialcontracts/{id}/certificates/{certId}/issue");
        Assert.Equal(expected, issued.Status);
        var cert = (await api.Get($"/commercialcontracts/{id}/certificates/{certId}")).Data!;
        if (expected == 200)
        {
            Assert.Equal("invoiced", cert["status"].S());
            Assert.Equal(issued.Data!["id"].S(), cert["invoiceId"].S());
            Assert.Equal(5_500m, cert["amountDue"].D()); // 5,000 + 750 ضريبة − 250 ضمان
            Assert.Equal(409, (await api.Post($"/commercialcontracts/{id}/certificates/{certId}/issue")).Status); // لا تتكرر
        }
        else Assert.Equal("draft", cert["status"].S()); // لا شيء تغيّر
    }

    [Fact]
    public async Task Initial_acceptance_opens_once_every_line_is_fully_certified_even_without_a_final_certificate()
    {
        var api = await NewConstructionTenantAsync();
        var (customer, _) = await SeedCustomerAsync(api, creditLimit: 1_000_000);
        var (id, item) = await ActiveBoqAsync(api, "simple", "customer", customer);

        await BilledCertificateAsync(api, id, item, 50); // 50%: لا استلام بعد
        var early = await Move(api, id, "initial_acceptance");
        Assert.Equal(409, early.Status);
        Assert.Contains("100%", early.Body!["message"].S());

        await BilledCertificateAsync(api, id, item, 50); // مستخلص جارٍ يكمل 100%
        Ok(await Move(api, id, "initial_acceptance"), "acceptance at 100% without a final certificate");
    }

    [Fact]
    public async Task Advance_invoice_is_collected_and_foreign_invoices_are_rejected()
    {
        var api = await NewConstructionTenantAsync();
        var (customer, _) = await SeedCustomerAsync(api, creditLimit: 1_000_000);
        var (id, item) = await ActiveBoqAsync(api, "simple", "customer", customer);
        var advance = await api.Post($"/commercialcontracts/{id}/advance-invoice");
        Ok(advance, "advance");
        var before = (await api.Get($"/commercialcontracts/{id}/boq-progress")).Data!;
        Assert.Equal(advance.Data!["id"].S(), before["advanceInvoiceId"].S());
        Assert.Equal(1_150m, before["advanceAmountDue"].D()); // 1,000 + 15%
        Ok(await api.Post($"/commercialcontracts/{id}/payments", Payment(advance.Data["id"].S(), 1_150)), "advance collection");
        Assert.Equal(0m, (await api.Get($"/commercialcontracts/{id}/boq-progress")).Data!["advanceAmountDue"].D());

        // فاتورة عقد آخر لا تُحصَّل من هذا العقد
        var (other, otherItem) = await ActiveBoqAsync(api, "simple", "customer", customer);
        var (_, otherInvoice, _) = await BilledCertificateAsync(api, other, otherItem, 10);
        Assert.Equal(400, (await api.Post($"/commercialcontracts/{id}/payments", Payment(otherInvoice, 10))).Status);
        Assert.Equal(400, (await api.Post($"/commercialcontracts/{id}/payments", Payment(otherInvoice, 0))).Status);
    }

    [Fact]
    public async Task Subcontractor_certificate_is_paid_with_a_payment_voucher()
    {
        var api = await NewConstructionTenantAsync();
        var (supplier, supplierAccount) = await SeedSupplierAsync(api);
        Ok(await api.Post("/journalentries", new { date = DateTime.UtcNow, description = "رأس المال",
            lines = new[] { new { accountCode = "1111", debit = 50_000m, credit = 0m }, new { accountCode = "31", debit = 0m, credit = 50_000m } } }), "capital");
        var (id, item) = await ActiveBoqAsync(api, "full", "supplier", supplier);
        var (certId, invoiceId, net) = await BilledCertificateAsync(api, id, item, 40);

        var paid = await api.Post($"/commercialcontracts/{id}/payments", Payment(invoiceId, net));
        Ok(paid, "subcontractor payment");
        Assert.Equal("Payment", paid.Data!["type"].S(), ignoreCase: true);
        Assert.Equal(0m, await DueAsync(api, id, certId));
        Assert.Equal(0m, await BalanceAsync(api, supplierAccount));
    }

    [Fact]
    public async Task Simplified_boq_contract_100k_with_10_percent_advance_runs_from_creation_to_close()
    {
        var api = await NewConstructionTenantAsync();
        var (party, customerAccount) = await SeedCustomerAsync(api, creditLimit: 1_000_000);

        // حسابات العقود مُهيَّأة مع المنشأة ومربوطة بالإعدادات (بلا إعداد يدوي)
        var settings = (await api.Get("/accounting-settings")).Data!;
        Assert.Equal(AdvancesReceived, settings["contractAdvancesReceivedAccountCode"].S());
        Assert.Equal(RetentionReceivable, settings["contractRetentionReceivableAccountCode"].S());

        // 1) الإنشاء: مقاولات بجدول كميات، مسار مبسط، بلا ضمان بنكي
        var created = await api.Post("/commercialcontracts", new
        {
            title = "مشروع مبنى إداري", contractType = "construction_boq", workflow = "simple", requiresGuarantee = false,
            partyType = "customer", partyId = party, partyName = "عميل آجل",
            startDate = DateTime.UtcNow.Date, endDate = DateTime.UtcNow.Date.AddMonths(6),
            contractValue = 0, vatRate = 15, advancePercent = 10, retentionPercent = 5, maxPenaltyPercent = 10,
            items = new object[]
            {
                new { description = "حفر وردم", unit = "م³", quantity = 1000, unitPrice = 50, vatRate = 15, lineCode = "1.1", sectionName = "أعمال ترابية", lineKind = "measured", sortOrder = 1 },
                new { description = "خرسانة مسلحة", unit = "م³", quantity = 200, unitPrice = 250, vatRate = 15, lineCode = "2.1", sectionName = "أعمال خرسانية", lineKind = "measured", sortOrder = 2 },
            },
            milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
        });
        Ok(created, "create");
        var id = created.Data!["id"].S();
        Assert.Equal(100_000m, created.Data["contractValue"].D());
        Assert.True(created.Data["isSimplified"]!.GetValue<bool>());
        var items = created.Data["items"]!.AsArray().OrderBy(i => i!["sortOrder"]!.GetValue<int>()).ToList();
        var excavation = items[0]!["id"].G();
        var concrete = items[1]!["id"].G();

        // 2) التفعيل المباشر (مسودة ← ساري)
        Ok(await Move(api, id, "active"), "activate");

        // 3) فاتورة الدفعة المقدمة: 10,000 + 1,500 ضريبة على «دفعات مقدمة مستلمة»
        var advance = await api.Post($"/commercialcontracts/{id}/advance-invoice");
        Ok(advance, "advance invoice");
        Assert.Equal(11_500m, advance.Data!["grandTotal"].D());
        Assert.Equal(-10_000m, await BalanceAsync(api, AdvancesReceived));
        Assert.Equal(409, (await api.Post($"/commercialcontracts/{id}/advance-invoice")).Status); // لا تتكرر

        // 4) مستخلص جارٍ: 40% من كل بند = 40,000
        var c1 = await api.Post($"/commercialcontracts/{id}/certificates", Certificate("interim", excavation, 400, concrete, 80));
        Ok(c1, "certificate 1 create");
        var c1Id = c1.Data!["id"].S();
        Assert.Equal(40_000m, c1.Data["currentWorkValue"].D());
        Assert.Equal(4_000m, c1.Data["advanceRecovery"].D());   // 10% من الأعمال
        Assert.Equal(36_000m, c1.Data["taxableAmount"].D());
        Assert.Equal(5_400m, c1.Data["vatAmount"].D());
        Assert.Equal(2_000m, c1.Data["retentionAmount"].D());   // 5% من الأعمال
        Assert.Equal(39_400m, c1.Data["netPayable"].D());
        await AttachCertificateEvidenceAsync(api, id, c1Id);
        Ok(await api.Post($"/commercialcontracts/{id}/certificates/{c1Id}/submit"), "certificate 1 submit");
        Ok(await api.Post($"/commercialcontracts/{id}/certificates/{c1Id}/approve"), "certificate 1 approve");
        var bill1 = await api.Post($"/commercialcontracts/{id}/certificates/{c1Id}/bill");
        Ok(bill1, "certificate 1 bill");
        Assert.Equal(41_400m, bill1.Data!["grandTotal"].D());
        Assert.Equal(-6_000m, await BalanceAsync(api, AdvancesReceived));  // استُرد 4,000
        Assert.Equal(2_000m, await BalanceAsync(api, RetentionReceivable));

        // 5) مستخلص ختامي: الباقي 60% = 60,000؛ يسترد ما بقي من الدفعة كله
        var c2 = await api.Post($"/commercialcontracts/{id}/certificates", Certificate("final", excavation, 600, concrete, 120));
        Ok(c2, "certificate 2 create");
        var c2Id = c2.Data!["id"].S();
        Assert.Equal(60_000m, c2.Data["currentWorkValue"].D());
        Assert.Equal(6_000m, c2.Data["advanceRecovery"].D());
        Assert.Equal(54_000m, c2.Data["taxableAmount"].D());
        Assert.Equal(8_100m, c2.Data["vatAmount"].D());
        Assert.Equal(3_000m, c2.Data["retentionAmount"].D());
        Assert.Equal(59_100m, c2.Data["netPayable"].D());
        await AttachCertificateEvidenceAsync(api, id, c2Id);
        Ok(await api.Post($"/commercialcontracts/{id}/certificates/{c2Id}/submit"), "certificate 2 submit");
        Ok(await api.Post($"/commercialcontracts/{id}/certificates/{c2Id}/approve"), "certificate 2 approve");
        Ok(await api.Post($"/commercialcontracts/{id}/certificates/{c2Id}/bill"), "certificate 2 bill");
        Assert.Equal(0m, await BalanceAsync(api, AdvancesReceived));       // الدفعة استُردت كاملة
        Assert.Equal(5_000m, await BalanceAsync(api, RetentionReceivable));

        // لا مستخلص بعد الختامي
        Assert.Equal(409, (await api.Post($"/commercialcontracts/{id}/certificates", Certificate("interim", excavation, 1, concrete, 0))).Status);

        // 6) الاستلام الأولي ثم الإفراج عن الضمان ثم الإغلاق
        Ok(await Move(api, id, "initial_acceptance"), "initial acceptance");
        var blockedClose = await Move(api, id, "closed");
        Assert.Equal(409, blockedClose.Status); // الضمان لم يُفرج عنه بعد
        var release = await api.Post($"/commercialcontracts/{id}/retention-release");
        Ok(release, "retention release");
        Assert.Equal(5_000m, release.Data!["amount"].D());
        Assert.Equal(0m, await BalanceAsync(api, RetentionReceivable));
        Ok(await Move(api, id, "closed"), "close");

        // 7) الحصيلة: الإيراد = القيمة، الضريبة = 15%، ذمة العميل = القيمة + الضريبة، والميزان متوازن
        Assert.Equal(-100_000m, await BalanceAsync(api, Revenue));
        Assert.Equal(-15_000m, await BalanceAsync(api, OutputVat));
        Assert.Equal(115_000m, await BalanceAsync(api, customerAccount));
        var (d, c) = Totals(await api.Get("/reports/TrialBalance"));
        Assert.Equal(d, c);

        var progress = (await api.Get($"/commercialcontracts/{id}/boq-progress")).Data!;
        Assert.Equal(100_000m, progress["certifiedValue"].D());
        Assert.Equal(100m, progress["completionPercent"].D());
        Assert.Equal(0m, progress["advanceOutstanding"].D());
        Assert.Equal(0m, progress["retentionOutstanding"].D());
        Assert.Equal(115_000m, progress["invoicedTotal"].D());
        Assert.Equal("closed", (await api.Get($"/commercialcontracts/{id}")).Data!["stage"].S());

        // تقرير التشغيل (يظهر بمخرجات الاختبار): القيود والأرصدة كما رُحّلت فعلاً
        var ledger = await api.Get("/reports/JournalLedger?take=500");
        var rows = (ledger.Data is JsonObject o && o["data"] is JsonArray inner ? inner : ledger.Data!.AsArray()).ToList();
        foreach (var r in rows)
            _out.WriteLine($"JE|{r!["entryNumber"].S()}|{r["description"].S()}|{r["accountCode"].S()}|{r["accountName"].S()}|{r["debit"].D():0.00}|{r["credit"].D():0.00}");
        foreach (var r in (await api.Get("/reports/TrialBalance")).Data!.AsArray())
            if (r!["closingDebit"].D() != 0 || r["closingCredit"].D() != 0)
                _out.WriteLine($"TB|{r["accountCode"].S()}|{r["accountNameAr"].S()}|{r["closingDebit"].D():0.00}|{r["closingCredit"].D():0.00}");
    }
}
