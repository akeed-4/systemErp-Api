using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// تحليل أداء المشاريع للإدارة (`GET construction-projects/performance`): كل الأرقام من البيانات المحفوظة.
/// المشروع 60 يوماً بدأ قبل 20 يوماً، ودوامه 4 ساعات. بندان بالقيمة نفسها: خرسانة 100 م3 × 300 وحديد 10 طن × 3000.
/// المرحلة 1 (الخرسانة، وزن 50) انتهت أمس، والمرحلة 2 (الحديد، وزن 50) تبدأ اليوم وتستمر 40 يوماً.
/// تقريران معتمدان للخرسانة: المستهدف 40 م3 في كلٍ والمنفَّذ 20 م3، وتقرير ثالث مقدَّم لم يُعتمد (لا يُحتسب).
/// </summary>
[Collection("api")]
public class ProjectPerformanceTests : TestBase
{
    public ProjectPerformanceTests(ErpFactory f) : base(f) { }

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");
    private static readonly DateTime Today = DateTime.UtcNow.AddHours(3).Date;

    private sealed record Project(string Id, Guid Concrete, Guid Steel, Guid Phase1, Guid Phase2, Guid Excavator, Guid Crew);

    private static async Task<Project> ProjectAsync(Client api)
    {
        var created = await api.Post("/commercialcontracts", new
        {
            title = "برج إداري", projectName = "برج إداري", contractType = "construction_boq", workflow = "simple", requiresGuarantee = false, partyName = "عميل",
            startDate = Today.AddDays(-20), endDate = Today.AddDays(39), vatRate = 15,
            items = new object[]
            {
                new { description = "صب خرسانة", unit = "م3", quantity = 100, unitPrice = 300, vatRate = 15, lineKind = "measured" },
                new { description = "حديد تسليح", unit = "طن", quantity = 10, unitPrice = 3000, vatRate = 15, lineKind = "measured" },
            },
            milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
        });
        Ok(created, "contract");
        var id = created.Data!["id"].S();
        var items = created.Data["items"]!.AsArray();
        var concrete = items.First(i => i!["description"].S() == "صب خرسانة")!["id"].G();
        var steel = items.First(i => i!["description"].S() == "حديد تسليح")!["id"].G();
        Ok(await api.Put($"/commercialcontracts/{id}/project/profile", new { workdayStart = "07:00", workdayEnd = "11:00", estimatedBudget = 40000 }), "profile");
        var phases = await api.Put($"/commercialcontracts/{id}/project/phases", new
        {
            phases = new object[]
            {
                new { name = "الخرسانة", plannedStart = Today.AddDays(-20), plannedEnd = Today.AddDays(-1), weightPercent = 50, contractItemIds = new[] { concrete } },
                new { name = "الحديد", plannedStart = Today, plannedEnd = Today.AddDays(39), weightPercent = 50, contractItemIds = new[] { steel } },
            },
        });
        Ok(phases, "phases");
        var ph = phases.Data!["phases"]!.AsArray();
        Guid Phase(string name) => ph.First(p => p!["name"].S() == name)!["id"].G();
        var resources = await api.Post($"/commercialcontracts/{id}/project/resources", new { kind = "owned_equipment", name = "حفار", rate = 100 });
        Ok(resources, "excavator");
        resources = await api.Post($"/commercialcontracts/{id}/project/resources", new { kind = "labor", name = "طاقم صب", rateBasis = "day", rate = 200, hoursPerDay = 8 });
        Ok(resources, "crew");
        Guid Res(string name) => resources.Data!.AsArray().First(r => r!["name"].S() == name)!["id"].G();
        return new Project(id, concrete, steel, Phase("الخرسانة"), Phase("الحديد"), Res("حفار"), Res("طاقم صب"));
    }

    /// <summary>تقرير بأربع فترات للخرسانة (المستهدف 10 والمنفَّذ 5 لكل ساعة)، وتوقف 30 دقيقة لعطل في الفترة الأولى إن طُلب.</summary>
    private static async Task<string> ReportAsync(Client api, Project p, DateTime date, decimal machineHours, decimal machineIdle, bool breakdown, bool approve)
    {
        var slots = new[] { ("07:00", "08:00"), ("08:00", "09:00"), ("09:00", "10:00"), ("10:00", "11:00") };
        var r = await api.Post($"/commercialcontracts/{p.Id}/site-reports", new
        {
            reportDate = date,
            hours = slots.Select((s, k) => new
            {
                start = s.Item1, end = s.Item2, contractItemId = p.Concrete, targetQuantity = 10, actualQuantity = 5,
                downtimeMinutes = breakdown && k == 0 ? 30 : 0, downtimeReason = breakdown && k == 0 ? "equipment_breakdown" : null,
            }),
            resources = new object[]
            {
                new { projectResourceId = p.Excavator, contractItemId = p.Concrete, hours = machineHours, idleHours = machineIdle, idleReason = machineIdle > 0 ? "waiting_inspection" : null },
                new { projectResourceId = p.Crew, contractItemId = p.Concrete, hours = 4 },
            },
        });
        Ok(r, "report " + date.ToString("yyyy-MM-dd"));
        var rid = r.Data!["id"].S();
        Ok(await api.Post($"/commercialcontracts/{p.Id}/site-reports/{rid}/submit"), "submit");
        if (approve) Ok(await api.Post($"/commercialcontracts/{p.Id}/site-reports/{rid}/approve"), "approve");
        return rid;
    }

    private static async Task<JsonNode> PerformanceAsync(Client api, string query = "")
    {
        var r = await api.Get("/construction-projects/performance" + query);
        Ok(r, "performance");
        return r.Data!;
    }

    private static string[] Codes(JsonNode row) => row["alerts"]!.AsArray().Select(a => a!["code"].S()).ToArray();
    private static JsonNode Alert(JsonNode row, string code) => row["alerts"]!.AsArray().First(a => a!["code"].S() == code)!;

    [Fact]
    public async Task Analysis_uses_only_approved_reports_and_derives_productivity_schedule_forecast_resources_and_alerts()
    {
        var api = await NewConstructionTenantAsync();
        var p = await ProjectAsync(api);
        await ReportAsync(api, p, Today.AddDays(-5), machineHours: 3, machineIdle: 1, breakdown: true, approve: true);
        await ReportAsync(api, p, Today.AddDays(-3), machineHours: 2, machineIdle: 2, breakdown: false, approve: true);
        await ReportAsync(api, p, Today.AddDays(-1), machineHours: 4, machineIdle: 0, breakdown: false, approve: false);

        var data = await PerformanceAsync(api);
        Assert.Equal(Today.AddDays(-29), data["from"]!.GetValue<DateTime>().Date);
        Assert.Equal(Today, data["to"]!.GetValue<DateTime>().Date);
        Assert.Equal("day", data["bucket"].S());
        var row = data["projects"]!.AsArray().Single()!;
        Assert.Equal(p.Id, row["summary"]!["contractId"].S());

        // الإنتاجية: 80 م3 مستهدفة × 300 = 24000، والمنفَّذ 40 × 300 = 12000 (المقدَّم لا يُحتسب)
        var prod = row["productivity"]!;
        Assert.Equal(2, prod["reportsApproved"]!.GetValue<int>());
        Assert.Equal(1, prod["reportsPending"]!.GetValue<int>());
        Assert.Equal(24000m, prod["targetValue"].D());
        Assert.Equal(12000m, prod["actualValue"].D());
        Assert.Equal(50m, prod["productivityPercent"].D());
        Assert.Equal(8m, prod["workingHours"].D());
        Assert.Equal(0.5m, prod["downtimeHours"].D());
        Assert.Equal("equipment_breakdown", prod["downtime"]!.AsArray().Single()!["reason"].S());
        Assert.Equal(3, prod["daysSinceLastReport"]!.GetValue<int>());
        var item = prod["items"]!.AsArray().Single()!;
        Assert.Equal(40m, item["executedToDate"].D());
        Assert.Equal(60m, item["remainingQuantity"].D());
        Assert.Equal("الخرسانة", item["phaseName"].S());

        // العمالة والمعدات من التقارير المعتمدة: الحفار 5 تشغيل و3 توقف، والطاقم 8 ساعات في يومين
        var eq = row["equipment"]!;
        Assert.Equal(5m, eq["operatingHours"].D());
        Assert.Equal(3m, eq["idleHours"].D());
        Assert.Equal(62.5m, eq["utilizationPercent"].D());
        Assert.Equal(62.5m, eq["capacityUtilizationPercent"].D()); // 5 ÷ (يومان × 4 ساعات)
        Assert.Equal(2, eq["equipmentDays"]!.GetValue<int>());
        Assert.Equal(8m, row["labor"]!["hours"].D());
        Assert.Equal(2, row["labor"]!["manDays"]!.GetValue<int>());

        // المراحل: الأولى انتهى موعدها بإنجاز 40% (متأخرة عن موعدها)، والثانية بدأت اليوم
        var sc = row["schedule"]!;
        var phases = sc["phases"]!.AsArray();
        var first = phases.First(x => x!["name"].S() == "الخرسانة")!;
        Assert.Equal(40m, first["actualPercent"].D());
        Assert.Equal("overdue", first["status"].S());
        Assert.Equal(1, first["delayDays"]!.GetValue<int>());
        Assert.Equal("in_progress", phases.First(x => x!["name"].S() == "الحديد")!["status"].S());
        Assert.Equal(1, sc["phasesOverdue"]!.GetValue<int>());
        // المخطط = (50 × 1 + 50 × 1/40) ÷ 100 = 51.25، والفعلي = 50 × 0.4 = 20
        Assert.Equal(51.25m, sc["plannedPercent"].D());
        Assert.Equal(20m, sc["actualPercent"].D());
        Assert.Equal(0.39m, sc["spi"].D());
        // التوقع بمعدل الإنجاز: 21 يوماً لـ 20% ← 105 أيام من البداية، أي بعد نهاية العقد بـ 45 يوماً
        Assert.Equal("rate", sc["forecastBasis"].S());
        Assert.Equal(Today.AddDays(84), sc["forecastEnd"]!.GetValue<DateTime>().Date);
        Assert.Equal(45, sc["forecastDelayDays"]!.GetValue<int>());

        // المال من تقرير التكلفة نفسه
        var fin = row["financial"]!;
        var cost = (await api.Get($"/commercialcontracts/{p.Id}/project/cost-report")).Data!;
        Assert.Equal(cost["actualCost"].D(), fin["actualCost"].D());
        Assert.Equal(cost["estimateAtCompletion"].D(), fin["estimateAtCompletion"].D());
        Assert.Equal(fin["budget"].D() - fin["actualCost"].D(), fin["remainingBudget"].D());
        Assert.Equal(fin["actualCost"].D(), fin["periodCost"].D()); // كل القيود داخل الفترة

        // الاتجاه اليومي: نقطة لكل يوم، ويوم التقرير الأول بمنفَّذه
        var trend = row["trend"]!.AsArray();
        Assert.Equal(30, trend.Count);
        Assert.Equal(6000m, trend.First(t => t!["start"]!.GetValue<DateTime>().Date == Today.AddDays(-5))!["actualValue"].D());

        // التنبيهات بإجراءاتها: الإنتاجية الضعيفة بسبب العطل ← صيانة
        var codes = Codes(row);
        Assert.Contains("schedule_behind", codes);
        Assert.Contains("forecast_late", codes);
        Assert.Contains("phase_overdue", codes);
        Assert.Contains("equipment_breakdown", codes);
        Assert.Contains("reports_pending", codes);
        Assert.DoesNotContain("reports_missing", codes);
        var low = Alert(row, "low_productivity");
        Assert.Equal("critical", low["severity"].S());
        Assert.Equal("schedule_maintenance", low["action"].S());
        Assert.Equal("equipment_breakdown", low["reference"].S());
        Assert.Equal("الخرسانة", Alert(row, "phase_overdue")["reference"].S());
        Assert.Equal("risk", row["health"].S());
    }

    [Fact]
    public async Task Phase_and_date_filters_narrow_the_analysis_and_invalid_ranges_are_rejected()
    {
        var api = await NewConstructionTenantAsync();
        var p = await ProjectAsync(api);
        await ReportAsync(api, p, Today.AddDays(-5), machineHours: 3, machineIdle: 1, breakdown: true, approve: true);
        await ReportAsync(api, p, Today.AddDays(-3), machineHours: 2, machineIdle: 2, breakdown: false, approve: true);

        // المرحلة الأولى وحدها: إنجازها 40% ومخططها 100%
        var one = (await PerformanceAsync(api, $"?contractId={p.Id}&phaseId={p.Phase1}"))["projects"]!.AsArray().Single()!;
        Assert.Single(one["schedule"]!["phases"]!.AsArray());
        Assert.Equal(100m, one["schedule"]!["plannedPercent"].D());
        Assert.Equal(40m, one["schedule"]!["actualPercent"].D());
        Assert.Equal(12000m, one["productivity"]!["actualValue"].D());

        // المرحلة الثانية: لا إنتاج ولا موارد على بنودها
        var two = (await PerformanceAsync(api, $"?contractId={p.Id}&phaseId={p.Phase2}"))["projects"]!.AsArray().Single()!;
        Assert.Equal(0m, two["productivity"]!["targetValue"].D());
        Assert.Null(two["productivity"]!["productivityPercent"]?.GetValue<decimal?>());
        Assert.Equal(0m, two["equipment"]!["operatingHours"].D());

        // فترة بعد التقرير الأول: تقرير واحد فقط في الإنتاجية، والإنجاز التراكمي يبقى من كل ما سبق
        var from = Today.AddDays(-4).ToString("yyyy-MM-dd");
        var narrow = (await PerformanceAsync(api, $"?from={from}"))["projects"]!.AsArray().Single()!;
        Assert.Equal(1, narrow["productivity"]!["reportsApproved"]!.GetValue<int>());
        Assert.Equal(6000m, narrow["productivity"]!["actualValue"].D());
        Assert.Equal(20m, narrow["schedule"]!["actualPercent"].D());

        // فترة مقلوبة، أو مرحلة بلا مشروع، مرفوضة
        Assert.Equal(400, (await api.Get($"/construction-projects/performance?from={Today:yyyy-MM-dd}&to={Today.AddDays(-3):yyyy-MM-dd}")).Status);
        Assert.Equal(400, (await api.Get($"/construction-projects/performance?phaseId={p.Phase1}")).Status);
        Assert.Equal(400, (await api.Get($"/construction-projects/performance?from={Today.AddDays(-400):yyyy-MM-dd}")).Status);
    }

    [Fact]
    public async Task Another_tenant_cannot_analyze_the_project()
    {
        var api = await NewConstructionTenantAsync();
        var p = await ProjectAsync(api);
        var other = await NewConstructionTenantAsync();
        Assert.Empty((await PerformanceAsync(other))["projects"]!.AsArray());
        Assert.Equal(404, (await other.Get($"/construction-projects/performance?contractId={p.Id}")).Status);
    }
}
