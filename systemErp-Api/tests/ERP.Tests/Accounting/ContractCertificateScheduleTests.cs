using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// جدولة المستخلصات تنظّم مواعيد التقديم ولا تقيّد الإنجاز: مشروع اكتمل قبل انقضاء جدوله (شهرياً أو بعدد دفعات)
/// يُقدَّم مستخلصه المكتمل ويُعتمد ويُستلم، والجاري الجزئي ما زال ينتظر نهاية فترته، ولا تُتجاوز كميات العقد.
/// </summary>
[Collection("api")]
public class ContractCertificateScheduleTests : TestBase
{
    public ContractCertificateScheduleTests(ErpFactory f) : base(f) { }

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");

    private static Task<Res> Move(Client api, string id, string to) => api.Post($"/commercialcontracts/{id}/move", new { toStatus = to });

    /// <summary>عقد سنة (بندان 100 × 100) بجدولة <paramref name="schedule"/> مفعّل على المسار المبسط.</summary>
    private async Task<(Client Api, string Id, Guid A, Guid B)> ActiveAsync(string schedule, int? count = null, int? months = null)
    {
        var api = await NewConstructionTenantAsync();
        var (customer, _) = await SeedCustomerAsync(api, creditLimit: 1_000_000);
        var created = await api.Post("/commercialcontracts", new
        {
            title = "عقد", contractType = "construction_boq", workflow = "simple", requiresGuarantee = false,
            partyType = "customer", partyId = customer, partyName = "عميل",
            startDate = DateTime.UtcNow.Date, endDate = DateTime.UtcNow.Date.AddMonths(12),
            vatRate = 15, advancePercent = 0, retentionPercent = 5,
            certificateSchedule = schedule, certificateCount = count, certificatePeriodMonths = months,
            items = new[]
            {
                new { description = "بند أ", unit = "م", quantity = 100, unitPrice = 100, vatRate = 15, lineKind = "measured" },
                new { description = "بند ب", unit = "م", quantity = 100, unitPrice = 100, vatRate = 15, lineKind = "measured" },
            },
            milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
        });
        Ok(created, "create");
        var id = created.Data!["id"].S();
        Ok(await Move(api, id, "active"), "activate");
        return (api, id, created.Data["items"]![0]!["id"].G(), created.Data["items"]![1]!["id"].G());
    }

    private static async Task<string> DraftAsync(Client api, string id, string kind, Guid a, decimal qa, Guid b, decimal qb, int expected = 200)
    {
        var r = await api.Post($"/commercialcontracts/{id}/certificates", new
        {
            kind, certificateDate = DateTime.UtcNow.Date,
            lines = new[] { new { contractItemId = a, currentQuantity = qa }, new { contractItemId = b, currentQuantity = qb } },
            deductions = Array.Empty<object>(),
        });
        if (expected != 200) { Assert.Equal(expected, r.Status); return string.Empty; }
        Ok(r, "draft");
        return r.Data!["id"].S();
    }

    private async Task ApproveAsync(Client api, string id, string certId)
    {
        await AttachCertificateEvidenceAsync(api, id, certId);
        Ok(await api.Post($"/commercialcontracts/{id}/certificates/{certId}/submit"), "submit");
        Ok(await api.Post($"/commercialcontracts/{id}/certificates/{certId}/approve"), "approve");
    }

    private static async Task<ScheduleProgress> ProgressAsync(Client api, string id)
    {
        var p = (await api.Get($"/commercialcontracts/{id}/boq-progress")).Data!;
        return new(p["completionPercent"].D(), p["schedule"]!["complete"]!.GetValue<bool>());
    }

    private sealed record ScheduleProgress(decimal Completion, bool ScheduleComplete);

    [Fact]
    public async Task Monthly_interim_completing_the_project_is_issued_before_period_end_and_allows_acceptance()
    {
        var (api, id, a, b) = await ActiveAsync("periodic", months: 1);
        var certId = await DraftAsync(api, id, "interim", a, 100, b, 100);

        // المشروع اكتمل في الشهر الأول من 12: الإصدار لا ينتظر نهاية الفترة ولا بقية الأشهر
        await AttachCertificateEvidenceAsync(api, id, certId);
        Ok(await api.Post($"/commercialcontracts/{id}/certificates/{certId}/issue"), "issue at 100%");
        Assert.Equal(100m, (await ProgressAsync(api, id)).Completion);
        Ok(await Move(api, id, "initial_acceptance"), "acceptance at 100%");
    }

    [Fact]
    public async Task Monthly_partial_interim_still_waits_for_its_period_end()
    {
        // الفترة الأولى تنتهي بنهاية الشهر الجاري؛ في آخر يوم منه تكون قد انتهت فلا يُختبر الانتظار
        var saudiToday = DateTime.UtcNow.AddHours(3).Date;
        if (saudiToday.AddDays(1).Month != saudiToday.Month) return;

        var (api, id, a, b) = await ActiveAsync("periodic", months: 1);
        var certId = await DraftAsync(api, id, "interim", a, 50, b, 0);
        Assert.Equal(409, (await api.Post($"/commercialcontracts/{id}/certificates/{certId}/submit")).Status);
    }

    [Fact]
    public async Task Monthly_final_at_100_percent_in_the_first_month_closes_the_schedule()
    {
        var (api, id, a, b) = await ActiveAsync("periodic", months: 1);
        await ApproveAsync(api, id, await DraftAsync(api, id, "final", a, 100, b, 100));

        var p = await ProgressAsync(api, id);
        Assert.Equal(100m, p.Completion);
        Assert.True(p.ScheduleComplete);
        await DraftAsync(api, id, "interim", a, 0, b, 0, expected: 409); // لا مستخلص بعد الختامي
        Ok(await Move(api, id, "initial_acceptance"), "acceptance");
    }

    [Fact]
    public async Task Twelve_installments_do_not_have_to_be_used_when_the_project_completes_in_two()
    {
        var (api, id, a, b) = await ActiveAsync("installments", count: 12);
        await ApproveAsync(api, id, await DraftAsync(api, id, "interim", a, 40, b, 40));
        await ApproveAsync(api, id, await DraftAsync(api, id, "final", a, 60, b, 60));

        var p = await ProgressAsync(api, id);
        Assert.Equal(100m, p.Completion);
        Assert.True(p.ScheduleComplete);
        Ok(await Move(api, id, "initial_acceptance"), "acceptance after 2 of 12");
    }

    [Fact]
    public async Task Certificates_cannot_exceed_contract_quantities()
    {
        var (api, id, a, b) = await ActiveAsync("periodic", months: 1);
        await DraftAsync(api, id, "final", a, 101, b, 100, expected: 400);

        await ApproveAsync(api, id, await DraftAsync(api, id, "interim", a, 100, b, 100));
        await DraftAsync(api, id, "interim", a, 1, b, 0, expected: 400); // التراكمي يتجاوز كمية العقد
    }
}
