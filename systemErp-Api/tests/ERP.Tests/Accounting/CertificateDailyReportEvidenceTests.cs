using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// المستخلص مبني على جدول الكميات: المستخدم يدخل الكمية الحالية لكل بند، والتقارير اليومية المعتمدة أدلة تتحقق منها وتُربط بها.
/// الخادم يخصص لكل كمية حالية أجزاء أسطر الإنجاز المعتمدة حتى نهاية فترة المستخلص (الأقدم أولاً) غير المخصصة لمستخلص آخر غير
/// باطل، ويمنع التقديم والاعتماد بلا دليل كافٍ، ويحرر الأدلة بالإلغاء والرفض مع بقاء سجلها، ويمنع إعادة فتح تقرير مستخدم دليلاً،
/// ويصمد أمام الطلبات المتزامنة.
/// المثال: صب خرسانة 500 م3 بسعر 300.
/// </summary>
[Collection("api")]
public class CertificateDailyReportEvidenceTests : TestBase
{
    public CertificateDailyReportEvidenceTests(ErpFactory f) : base(f) { }

    private static readonly DateTime Day = new(2026, 11, 10);

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");

    private sealed record Project(string Id, Guid Concrete, Guid Steel);

    private static async Task<Project> ProjectAsync(Client api)
    {
        var (customer, _) = await SeedCustomerAsync(api);
        var created = await api.Post("/commercialcontracts", new
        {
            title = "أعمال خرسانية", contractType = "construction_boq", workflow = "simple", requiresGuarantee = false, partyName = "عميل", partyId = customer,
            startDate = Day.AddDays(-20), endDate = Day.AddMonths(6), vatRate = 15,
            items = new object[]
            {
                new { description = "صب خرسانة", unit = "م3", quantity = 500, unitPrice = 300, vatRate = 15, lineKind = "measured" },
                new { description = "حديد تسليح", unit = "طن", quantity = 50, unitPrice = 3000, vatRate = 15, lineKind = "measured" },
            },
            milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
        });
        Ok(created, "create contract");
        var id = created.Data!["id"].S();
        Ok(await api.Post($"/commercialcontracts/{id}/move", new { toStatus = "active" }), "activate");
        var items = created.Data["items"]!.AsArray();
        Ok(await api.Put($"/commercialcontracts/{id}/project/profile", new { workdayStart = "07:00", workdayEnd = "11:00" }), "hours");
        return new Project(id, items.First(i => i!["description"].S() == "صب خرسانة")!["id"].G(), items.First(i => i!["description"].S() == "حديد تسليح")!["id"].G());
    }

    /// <summary>تقرير يومي بسطر إنجاز واحد للخرسانة؛ <paramref name="approve"/> = يُقدَّم ويُعتمد، وإلا يُقدَّم فقط أو يبقى مسودة.</summary>
    private static async Task<string> ReportAsync(Client api, Project p, DateTime date, decimal concrete, string state = "approved")
    {
        var r = await api.Post($"/commercialcontracts/{p.Id}/site-reports", new
        {
            reportDate = date,
            hours = new[] { new { start = "07:00", end = "08:00", contractItemId = p.Concrete, targetQuantity = 10, actualQuantity = concrete } },
        });
        Ok(r, "report " + date.ToString("MM-dd"));
        var rid = r.Data!["id"].S();
        if (state is "submitted" or "approved") Ok(await api.Post($"/commercialcontracts/{p.Id}/site-reports/{rid}/submit"), "submit report");
        if (state == "approved") await ApproveReportAsync(api, p, rid);
        return rid;
    }

    private static async Task ApproveReportAsync(Client api, Project p, string rid)
        => Ok(await api.Post($"/commercialcontracts/{p.Id}/site-reports/{rid}/approve"), "approve report");

    private static object CertBody(Project p, DateTime? from, DateTime to, decimal concrete) => new
    {
        kind = "interim", periodFrom = from, periodTo = to,
        lines = new[] { new { contractItemId = p.Concrete, currentQuantity = concrete } },
        deductions = Array.Empty<object>(),
    };

    private static Task<Res> CreateCertAsync(Client api, Project p, DateTime? from, DateTime to, decimal concrete)
        => api.Post($"/commercialcontracts/{p.Id}/certificates", CertBody(p, from, to, concrete));

    private static async Task<JsonNode> GetCertAsync(Client api, Project p, string certId)
    {
        var r = await api.Get($"/commercialcontracts/{p.Id}/certificates/{certId}");
        Ok(r, "get certificate");
        return r.Data!;
    }

    private static JsonNode Line(JsonNode cert, Guid item) => cert["lines"]!.AsArray().First(l => l!["contractItemId"].G() == item)!;

    private async Task ApproveCertAsync(Client api, Project p, string certId)
    {
        await AttachCertificateEvidenceAsync(api, p.Id, certId);
        Ok(await api.Post($"/commercialcontracts/{p.Id}/certificates/{certId}/submit"), "submit certificate");
        Ok(await api.Post($"/commercialcontracts/{p.Id}/certificates/{certId}/approve"), "approve certificate");
    }

    // ---------- التحقق من الكميات ----------

    [Fact]
    public async Task Manual_quantities_are_validated_against_approved_reports_within_the_certificate_period()
    {
        var api = await NewConstructionTenantAsync();
        var p = await ProjectAsync(api);
        await ReportAsync(api, p, Day.AddDays(-2), 12);
        await ReportAsync(api, p, Day.AddDays(-1), 18);
        await ReportAsync(api, p, Day, 10, state: "submitted");   // غير معتمد: ليس دليلاً
        await ReportAsync(api, p, Day.AddDays(1), 20);            // بعد نهاية الفترة: ليس دليلاً لهذا المستخلص

        // تجاوز كمية العقد يُرفض عند الحفظ نفسه (500 بلا نسبة تجاوز)
        Assert.Equal(400, (await CreateCertAsync(api, p, Day.AddDays(-5), Day, 501)).Status);

        // المستخدم يدخل 35 يدوياً: تُحفظ المسودة، والعرض يبيّن المتاح 30 وغير المدعوم 5
        var created = await CreateCertAsync(api, p, Day.AddDays(-5), Day, 35);
        Ok(created, "draft");
        var certId = created.Data!["id"].S();
        var draft = await GetCertAsync(api, p, certId);
        Assert.True(draft["evidenceRequired"]!.GetValue<bool>());
        var line = Line(draft, p.Concrete);
        Assert.Equal(35m, line["currentQuantity"].D());
        Assert.Equal(30m, line["availableEvidenceQuantity"].D());
        Assert.Equal(5m, line["unsupportedQuantity"].D());
        Assert.Equal(30m, line["evidenceQuantity"].D());

        // التقديم يرفض الكمية غير المدعومة بخطأ لكل بند
        await AttachCertificateEvidenceAsync(api, p.Id, certId);
        var refused = await api.Post($"/commercialcontracts/{p.Id}/certificates/{certId}/submit");
        Assert.Equal(400, refused.Status);
        Assert.Single(refused.Body!["errors"]!.AsArray());

        // 30 مدعومة بالكامل: تُقدَّم وتُعتمد، وأدلتها التقريران المعتمدان داخل الفترة بالأقدم أولاً
        Ok(await api.Put($"/commercialcontracts/{p.Id}/certificates/{certId}", CertBody(p, Day.AddDays(-5), Day, 30)), "fix");
        await ApproveCertAsync(api, p, certId);
        var approved = await GetCertAsync(api, p, certId);
        Assert.Equal("approved", approved["status"].S());
        var evidence = approved["evidence"]!.AsArray();
        Assert.Equal(new[] { Day.AddDays(-2), Day.AddDays(-1) }, evidence.Select(e => DateTime.Parse(e!["reportDate"].S()).Date).ToArray());
        Assert.Equal(new[] { 12m, 18m }, evidence.Select(e => e!["quantity"].D()).ToArray());
        Assert.All(evidence, e => Assert.Equal(p.Concrete, e!["contractItemId"].G()));

        line = Line(approved, p.Concrete);
        Assert.Equal(0m, line["previousQuantity"].D());
        Assert.Equal(30m, line["cumulativeQuantity"].D());
        Assert.Equal(9000m, line["currentAmount"].D());
        Assert.Equal(30m, line["evidenceQuantity"].D());
        Assert.Equal(0m, Line(approved, p.Steel)["currentQuantity"].D());

        var progress = (await api.Get($"/commercialcontracts/{p.Id}/boq-progress")).Data!;
        var concrete = progress["lines"]!.AsArray().First(l => l!["contractItemId"].G() == p.Concrete)!;
        Assert.Equal(30m, concrete["certifiedQuantity"].D());
        Assert.Equal(470m, concrete["remainingQuantity"].D());
    }

    // ---------- منع التخصيص المزدوج ----------

    [Fact]
    public async Task Report_quantities_are_never_allocated_to_two_certificates()
    {
        var api = await NewConstructionTenantAsync();
        var p = await ProjectAsync(api);
        await ReportAsync(api, p, Day.AddDays(-2), 30);
        var first = await CreateCertAsync(api, p, Day.AddDays(-5), Day.AddDays(-1), 30);
        Ok(first, "certificate 1");
        await ApproveCertAsync(api, p, first.Data!["id"].S());

        await ReportAsync(api, p, Day, 20);
        // المستخلص الثاني يرى المنفّذ التراكمي 50 لكن المتاح دليلاً 20 فقط (الـ30 مخصصة للأول)
        var second = await CreateCertAsync(api, p, Day, Day, 25);
        Ok(second, "certificate 2");
        var secondId = second.Data!["id"].S();
        var draft = await GetCertAsync(api, p, secondId);
        Assert.Equal(50m, Line(draft, p.Concrete)["siteExecutedQuantity"].D());
        Assert.Equal(20m, Line(draft, p.Concrete)["availableEvidenceQuantity"].D());
        await AttachCertificateEvidenceAsync(api, p.Id, secondId);
        Assert.Equal(400, (await api.Post($"/commercialcontracts/{p.Id}/certificates/{secondId}/submit")).Status);

        Ok(await api.Put($"/commercialcontracts/{p.Id}/certificates/{secondId}", CertBody(p, Day, Day, 20)), "fix 2");
        await ApproveCertAsync(api, p, secondId);
        var approved = await GetCertAsync(api, p, secondId);
        var evidence = approved["evidence"]!.AsArray();
        Assert.Equal(Day, DateTime.Parse(evidence.Single()!["reportDate"].S()).Date);
        Assert.Equal(20m, evidence.Single()!["quantity"].D());

        // السابق والحالي والتراكمي كمية وقيمة من الخادم
        var line = Line(approved, p.Concrete);
        Assert.Equal(30m, line["previousQuantity"].D());
        Assert.Equal(20m, line["currentQuantity"].D());
        Assert.Equal(50m, line["cumulativeQuantity"].D());
        Assert.Equal(9000m, line["previousAmount"].D());
        Assert.Equal(6000m, line["currentAmount"].D());
        Assert.Equal(15000m, line["cumulativeAmount"].D());

        // لم يبقَ دليل: مستخلص ثالث بأي كمية لا يُقدَّم
        var third = await CreateCertAsync(api, p, Day, Day, 1);
        Ok(third, "certificate 3");
        Assert.Equal(0m, Line(await GetCertAsync(api, p, third.Data!["id"].S()), p.Concrete)["availableEvidenceQuantity"].D());
        await AttachCertificateEvidenceAsync(api, p.Id, third.Data!["id"].S());
        Assert.Equal(400, (await api.Post($"/commercialcontracts/{p.Id}/certificates/{third.Data["id"].S()}/submit")).Status);
    }

    // ---------- الإلغاء والرفض ----------

    [Fact]
    public async Task Rejected_and_cancelled_certificates_release_evidence_keep_history_and_guard_report_reopen()
    {
        var api = await NewConstructionTenantAsync();
        var p = await ProjectAsync(api);
        var rid = await ReportAsync(api, p, Day, 40);

        var a = await CreateCertAsync(api, p, Day, Day, 40);
        Ok(a, "certificate A");
        var aId = a.Data!["id"].S();
        await AttachCertificateEvidenceAsync(api, p.Id, aId);
        Ok(await api.Post($"/commercialcontracts/{p.Id}/certificates/{aId}/submit"), "submit A");

        // التقرير دليل لمستخلص مقدَّم: لا يُعاد فتحه
        Assert.Equal(409, (await api.Post($"/commercialcontracts/{p.Id}/site-reports/{rid}/reopen", new { note = "تصحيح" })).Status);

        // الرفض قرار نهائي بسبب إلزامي، ومن المقدَّم وحده
        Assert.Equal(400, (await api.Post($"/commercialcontracts/{p.Id}/certificates/{aId}/reject", new { note = " " })).Status);
        var rejected = await api.Post($"/commercialcontracts/{p.Id}/certificates/{aId}/reject", new { note = "الكميات غير مطابقة للمحضر" });
        Ok(rejected, "reject");
        Assert.Equal("rejected", rejected.Data!["status"].S());
        Assert.Equal("الكميات غير مطابقة للمحضر", rejected.Data["returnNote"].S());
        Assert.False(string.IsNullOrEmpty(rejected.Data["rejectedBy"].S()));
        Assert.Equal(409, (await api.Post($"/commercialcontracts/{p.Id}/certificates/{aId}/approve")).Status);
        Assert.Equal(409, (await api.Post($"/commercialcontracts/{p.Id}/certificates/{aId}/reject", new { note = "x" })).Status);
        // سجله باقٍ للتدقيق
        var history = await GetCertAsync(api, p, aId);
        Assert.Equal(40m, history["evidence"]!.AsArray().Sum(e => e!["quantity"].D()));
        Assert.Equal(40m, Line(history, p.Concrete)["currentQuantity"].D());

        // الكمية تحررت: مستخلص جديد يجد الدليل كاملاً ولا يرث سابقاً من المرفوض
        var b = await CreateCertAsync(api, p, Day, Day, 40);
        Ok(b, "certificate B");
        var bId = b.Data!["id"].S();
        var bDraft = await GetCertAsync(api, p, bId);
        Assert.Equal(40m, Line(bDraft, p.Concrete)["availableEvidenceQuantity"].D());
        Assert.Equal(0m, Line(bDraft, p.Concrete)["previousQuantity"].D());
        Assert.NotEqual(history["sequenceNo"]!.GetValue<int>(), bDraft["sequenceNo"]!.GetValue<int>());
        // حتى المسودة تحجز دليلها
        Assert.Equal(409, (await api.Post($"/commercialcontracts/{p.Id}/site-reports/{rid}/reopen", new { note = "تصحيح" })).Status);

        // الإلغاء بسبب يحرر الدليل ويُختم
        Assert.Equal(400, (await api.Post($"/commercialcontracts/{p.Id}/certificates/{bId}/cancel", new { note = "" })).Status);
        var cancelled = await api.Post($"/commercialcontracts/{p.Id}/certificates/{bId}/cancel", new { note = "أُعدّ بالخطأ" });
        Ok(cancelled, "cancel");
        Assert.Equal("cancelled", cancelled.Data!["status"].S());
        Assert.False(string.IsNullOrEmpty(cancelled.Data["cancelledBy"].S()));

        // لا مستخلص غير باطل يستخدمه: يُعاد فتح التقرير
        Ok(await api.Post($"/commercialcontracts/{p.Id}/site-reports/{rid}/reopen", new { note = "تصحيح" }), "reopen");
    }

    // ---------- التزامن ----------

    [Fact]
    public async Task Concurrent_certificate_creation_allows_a_single_open_certificate_without_over_allocation()
    {
        var api = await NewConstructionTenantAsync();
        var p = await ProjectAsync(api);
        await ReportAsync(api, p, Day, 30);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => CreateCertAsync(api, p, Day, Day, 30)));
        Assert.Single(results, r => r.Status is 200 or 201);
        Assert.All(results.Where(r => r.Status is not (200 or 201)), r => Assert.Equal(409, r.Status));

        var list = (await api.Get($"/commercialcontracts/{p.Id}/certificates")).Data!.AsArray();
        var allocated = list.Where(c => c!["status"].S() is not ("cancelled" or "rejected"))
            .SelectMany(c => c!["evidence"]!.AsArray()).Sum(e => e!["quantity"].D());
        Assert.Equal(30m, allocated);
    }

    [Fact]
    public async Task Concurrent_report_reopen_and_certificate_allocation_never_both_succeed()
    {
        var api = await NewConstructionTenantAsync();
        var p = await ProjectAsync(api);
        var rid = await ReportAsync(api, p, Day, 30);

        for (var round = 0; round < 3; round++)
        {
            var create = CreateCertAsync(api, p, Day, Day, 30);
            var reopen = api.Post($"/commercialcontracts/{p.Id}/site-reports/{rid}/reopen", new { note = "تصحيح " + round });
            await Task.WhenAll(create, reopen);
            var created = create.Result;
            var reopened = reopen.Result.Status == 200;
            Assert.True(reopened || reopen.Result.Status == 409, $"reopen: {reopen.Result.Status} {reopen.Result.Body?.ToJsonString()}");
            Assert.True(created.Status is 200 or 201 or 409, $"create: {created.Status} {created.Body?.ToJsonString()}");

            if (created.Status is 200 or 201)
            {
                var certId = created.Data!["id"].S();
                var evidence = (await GetCertAsync(api, p, certId))["evidence"]!.AsArray();
                // تقرير أُعيد فتحه لا يبقى دليلاً، وتقرير هو دليل لم يُعَد فتحه
                if (reopened) Assert.Empty(evidence);
                else Assert.Equal(30m, evidence.Sum(e => e!["quantity"].D()));
                Ok(await api.Post($"/commercialcontracts/{p.Id}/certificates/{certId}/cancel", new { note = "جولة" }), "cancel round");
            }
            if (reopened)
            {
                Ok(await api.Post($"/commercialcontracts/{p.Id}/site-reports/{rid}/submit"), "resubmit report");
                await ApproveReportAsync(api, p, rid);
            }
        }
    }
}
