using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// خطة الإنجاز اليومية: الحفظ بالتعديل (المعرّفات باقية، والحذف صريح، والتزامن برمز السطر)، والتحقق من المستهدف والتاريخ، والتغطية
/// (المستهدف في اليوم والأيام اللازمة والتأخر عن المرحلة)، والمقترح من المراحل، والمستهدف المخطط في التقرير اليومي بجانب مستهدف
/// المشرف، وتنبيه المشروع الساري بلا خطة.
/// المثال: صب خرسانة 500 م3، ويوم عمل 07:00–11:00 (4 ساعات).
/// </summary>
[Collection("api")]
public class DailyProgressPlanTests : TestBase
{
    public DailyProgressPlanTests(ErpFactory f) : base(f) { }

    private static readonly DateTime Today = DateTime.UtcNow.AddHours(3).Date;

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");

    private sealed record Project(string Id, Guid Concrete, Guid Steel);

    private static async Task<Project> ProjectAsync(Client api)
    {
        var (customer, _) = await SeedCustomerAsync(api);
        var created = await api.Post("/commercialcontracts", new
        {
            title = "أعمال خرسانية", contractType = "construction_boq", workflow = "simple", requiresGuarantee = false, partyName = "عميل", partyId = customer,
            startDate = Today.AddDays(-30), endDate = Today.AddMonths(6), vatRate = 15,
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
        Ok(await api.Put($"/commercialcontracts/{id}/project/profile", new { workdayStart = "07:00", workdayEnd = "11:00" }), "hours");
        var items = created.Data["items"]!.AsArray();
        return new Project(id, items.First(i => i!["description"].S() == "صب خرسانة")!["id"].G(), items.First(i => i!["description"].S() == "حديد تسليح")!["id"].G());
    }

    private static string Plans(Project p) => $"/commercialcontracts/{p.Id}/project/daily-plans";

    private static JsonNode Row(JsonNode list, Guid item) => list.AsArray().First(x => x!["contractItemId"].G() == item)!;

    // ---------- الحفظ والتزامن ----------

    [Fact]
    public async Task Plan_rows_keep_their_ids_deletions_are_explicit_and_stale_edits_are_rejected()
    {
        var api = await NewConstructionTenantAsync();
        var p = await ProjectAsync(api);
        var first = await api.Put(Plans(p), new
        {
            plans = new object[]
            {
                new { contractItemId = p.Concrete, targetPerHour = 10, effectiveFrom = Today.AddDays(-5), notes = "طاقم الصب" },
                new { contractItemId = p.Steel, targetPerHour = 1, effectiveFrom = Today.AddDays(-5) },
            },
            deletedIds = Array.Empty<Guid>(),
        });
        Ok(first, "create plans");
        var concrete = Row(first.Data!, p.Concrete);
        var steel = Row(first.Data!, p.Steel);
        Assert.Equal(40m, concrete["targetPerDay"].D()); // 10 × 4 ساعات
        Assert.Equal("طاقم الصب", concrete["notes"].S());

        // تعديل الخرسانة بمعرّفها ورمزها، وحذف الحديد صراحة: المعرّف باقٍ
        var edited = await api.Put(Plans(p), new
        {
            plans = new object[] { new { id = concrete["id"].S(), rowVersion = concrete["rowVersion"].S(), contractItemId = p.Concrete, targetPerHour = 12, effectiveFrom = Today.AddDays(-5) } },
            deletedIds = new[] { steel["id"].G() },
        });
        Ok(edited, "edit plans");
        var after = edited.Data!.AsArray().Single()!;
        Assert.Equal(concrete["id"].S(), after["id"].S());
        Assert.Equal(12m, after["targetPerHour"].D());

        // الحفظ برمز قديم (عدّله غيرك بعد التحميل) يُرفض ولا يُكتب فوقه
        var stale = await api.Put(Plans(p), new
        {
            plans = new object[] { new { id = concrete["id"].S(), rowVersion = concrete["rowVersion"].S(), contractItemId = p.Concrete, targetPerHour = 99, effectiveFrom = Today.AddDays(-5) } },
            deletedIds = Array.Empty<Guid>(),
        });
        Assert.Equal(409, stale.Status);
        // سطر بمعرّف حُذف
        Assert.Equal(409, (await api.Put(Plans(p), new
        {
            plans = new object[] { new { id = steel["id"].S(), contractItemId = p.Steel, targetPerHour = 2, effectiveFrom = Today } },
            deletedIds = Array.Empty<Guid>(),
        })).Status);
        Assert.Equal(12m, (await api.Get(Plans(p))).Data!.AsArray().Single()!["targetPerHour"].D());

        // مع قائمة حذف: السطر غير المرسل لا يُحذف (أضافه غيرك)
        Ok(await api.Put(Plans(p), new { plans = new object[] { new { contractItemId = p.Steel, targetPerHour = 1, effectiveFrom = Today } }, deletedIds = Array.Empty<Guid>() }), "add only");
        Assert.Equal(2, (await api.Get(Plans(p))).Data!.AsArray().Count);
        // العميل القديم بلا قائمة حذف: استبدال كامل كما كان
        Ok(await api.Put(Plans(p), new { plans = new object[] { new { contractItemId = p.Steel, targetPerHour = 2, effectiveFrom = Today.AddDays(1) } } }), "legacy replace");
        Assert.Single((await api.Get(Plans(p))).Data!.AsArray());
    }

    [Fact]
    public async Task Target_cannot_exceed_the_item_and_the_date_must_fall_within_the_contract()
    {
        var api = await NewConstructionTenantAsync();
        var p = await ProjectAsync(api);
        var bad = await api.Put(Plans(p), new
        {
            plans = new object[]
            {
                new { contractItemId = p.Concrete, targetPerHour = 501, effectiveFrom = Today },          // > كمية البند كلها
                new { contractItemId = p.Steel, targetPerHour = 1, effectiveFrom = Today.AddDays(-60) },  // قبل بداية العقد
                new { contractItemId = p.Steel, targetPerHour = 1, effectiveFrom = Today.AddYears(1) },   // بعد نهايته
            },
            deletedIds = Array.Empty<Guid>(),
        });
        Assert.Equal(400, bad.Status);
        Assert.Equal(3, bad.Body!["errors"]!.AsArray().Count);
    }

    // ---------- التغطية والمقترح ----------

    [Fact]
    public async Task Coverage_shows_daily_target_days_to_finish_items_without_plan_and_phase_delay()
    {
        var api = await NewConstructionTenantAsync();
        var p = await ProjectAsync(api);
        // الخرسانة في مرحلة تنتهي بعد 5 أيام
        Ok(await api.Put($"/commercialcontracts/{p.Id}/project/phases", new
        {
            phases = new[] { new { name = "الهيكل", plannedStart = Today.AddDays(-5), plannedEnd = Today.AddDays(4), weightPercent = 100, contractItemIds = new[] { p.Concrete } } },
        }), "phases");
        Ok(await api.Put(Plans(p), new { plans = new object[] { new { contractItemId = p.Concrete, targetPerHour = 10, effectiveFrom = Today.AddDays(-5) } }, deletedIds = Array.Empty<Guid>() }), "plan");

        // تقرير معتمد نفّذ 100
        var r = await api.Post($"/commercialcontracts/{p.Id}/site-reports", new
        {
            reportDate = Today.AddDays(-1),
            hours = new[] { new { start = "07:00", end = "08:00", contractItemId = p.Concrete, targetQuantity = 10, actualQuantity = 100 } },
        });
        Ok(r, "report");
        Ok(await api.Post($"/commercialcontracts/{p.Id}/site-reports/{r.Data!["id"].S()}/submit"), "submit");
        Ok(await api.Post($"/commercialcontracts/{p.Id}/site-reports/{r.Data!["id"].S()}/approve"), "approve");

        var coverage = (await api.Get($"{Plans(p)}/coverage")).Data!;
        Assert.Equal(4m, coverage["workingHoursPerDay"].D());
        Assert.Equal(1, coverage["itemsWithoutPlan"]!.GetValue<int>()); // الحديد
        var line = Row(coverage["lines"]!, p.Concrete);
        Assert.True(line["hasPlan"]!.GetValue<bool>());
        Assert.Equal(40m, line["targetPerDay"].D());
        Assert.Equal(8m, line["percentPerDay"].D());        // 40 ÷ 500
        Assert.Equal(400m, line["remainingQuantity"].D());
        Assert.Equal(10m, line["daysToComplete"].D());      // 400 ÷ 40
        Assert.Equal(Today.AddDays(9), DateTime.Parse(line["forecastFinish"].S()).Date);
        Assert.True(line["behindPhase"]!.GetValue<bool>());  // المرحلة تنتهي بعد 4 أيام
        Assert.Equal(1, coverage["itemsBehindPhase"]!.GetValue<int>());
        Assert.False(Row(coverage["lines"]!, p.Steel)["hasPlan"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Proposal_from_phases_spreads_remaining_quantity_over_remaining_phase_days()
    {
        var api = await NewConstructionTenantAsync();
        var p = await ProjectAsync(api);
        Ok(await api.Put($"/commercialcontracts/{p.Id}/project/phases", new
        {
            phases = new[]
            {
                new { name = "الهيكل", plannedStart = Today.AddDays(1), plannedEnd = Today.AddDays(10), weightPercent = 60, contractItemIds = new[] { p.Concrete } },
                new { name = "التسليح القديم", plannedStart = Today.AddDays(-20), plannedEnd = Today.AddDays(-10), weightPercent = 40, contractItemIds = new[] { p.Steel } },
            },
        }), "phases");

        var proposal = (await api.Get($"{Plans(p)}/proposal")).Data!.AsArray();
        // المرحلة المنتهية لا يُقترح لبندها
        var only = Assert.Single(proposal)!;
        Assert.Equal(p.Concrete, only["contractItemId"].G());
        Assert.Equal(12.5m, only["targetPerHour"].D());    // 500 ÷ (10 أيام × 4 ساعات)
        Assert.Equal(50m, only["targetPerDay"].D());
        Assert.Equal(Today.AddDays(1), DateTime.Parse(only["effectiveFrom"].S()).Date);
        Assert.Equal("الهيكل", only["phaseName"].S());
        // لا يُحفظ شيء
        Assert.Empty((await api.Get(Plans(p))).Data!.AsArray());
    }

    // ---------- التقرير اليومي ----------

    [Fact]
    public async Task Daily_report_keeps_the_planned_target_next_to_the_supervisor_target()
    {
        var api = await NewConstructionTenantAsync();
        var p = await ProjectAsync(api);
        Ok(await api.Put(Plans(p), new { plans = new object[] { new { contractItemId = p.Concrete, targetPerHour = 10, effectiveFrom = Today.AddDays(-5) } }, deletedIds = Array.Empty<Guid>() }), "plan");

        var template = (await api.Get($"/commercialcontracts/{p.Id}/site-reports/day?date={Today.AddDays(-1):yyyy-MM-dd}")).Data!;
        Assert.All(template["hours"]!.AsArray(), h => Assert.Equal(10m, h!["plannedTargetQuantity"].D()));

        // المشرف خفّض مستهدف الساعة الأولى إلى 5 ونفّذ 9، وأضاف سطراً لبند بلا خطة (يُتجاهل ما يرسله كمستهدف مخطط)
        var saved = await api.Post($"/commercialcontracts/{p.Id}/site-reports", new
        {
            reportDate = Today.AddDays(-1),
            hours = new object[]
            {
                new { start = "07:00", end = "08:00", contractItemId = p.Concrete, targetQuantity = 5, actualQuantity = 9, plannedTargetQuantity = 1 },
                new { start = "07:00", end = "08:00", contractItemId = p.Steel, targetQuantity = 1, actualQuantity = 1 },
            },
        });
        Ok(saved, "report");
        var hours = saved.Data!["hours"]!.AsArray();
        var concrete = hours.First(h => h!["contractItemId"].G() == p.Concrete)!;
        Assert.Equal(10m, concrete["plannedTargetQuantity"].D());
        Assert.Equal(180m, concrete["achievementPercent"].D());        // على مستهدف المشرف
        Assert.Equal(90m, concrete["plannedAchievementPercent"].D());  // على الخطة
        var steel = hours.First(h => h!["contractItemId"].G() == p.Steel)!;
        Assert.Null(steel["plannedTargetQuantity"]?.GetValue<decimal?>());

        var summary = saved.Data["summary"]!;
        Assert.Equal(1, summary["targetOverrides"]!.GetValue<int>());
        Assert.Equal(90m, summary["plannedAchievementPercent"].D());
        var item = summary["items"]!.AsArray().First(i => i!["contractItemId"].G() == p.Concrete)!;
        Assert.Equal(10m, item["plannedTarget"].D());
        Assert.Equal(90m, item["plannedAchievementPercent"].D());
    }

    [Fact]
    public async Task Running_project_without_a_daily_plan_is_flagged_until_one_is_saved()
    {
        var api = await NewConstructionTenantAsync();
        var p = await ProjectAsync(api);
        static List<string> Alerts(Res r) => r.Data!["alerts"]!.AsArray().Select(a => a.S()).ToList();
        Assert.Contains("no_daily_plan", Alerts(await api.Get($"/commercialcontracts/{p.Id}/project/summary")));
        Ok(await api.Put(Plans(p), new { plans = new object[] { new { contractItemId = p.Concrete, targetPerHour = 10, effectiveFrom = Today } }, deletedIds = Array.Empty<Guid>() }), "plan");
        Assert.DoesNotContain("no_daily_plan", Alerts(await api.Get($"/commercialcontracts/{p.Id}/project/summary")));
    }
}
