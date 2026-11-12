using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// «إنشاء العقد» من الدراسة والخطة المعتمدتين (السجل نفسه برقم عقد ومركز تكلفة، ورقم الدراسة باقٍ)، وقيد بدء التنفيذ: لا تقرير يومي
/// قبل العقد، ولا قبل سريانه، ولا أثناء مراجعة مفتوحة للخطة. العقد المباشر يعمل كما كان.
/// </summary>
public partial class FeasibilityFlowTests
{
    private static Task<Res> DailyReport(Client api, string id, DateTime day) => api.Post($"/commercialcontracts/{id}/site-reports", new { reportDate = day });

    private static async Task ApprovePlanAsync(Client api, string id)
    {
        Ok(await api.Post($"/commercialcontracts/{id}/execution-plan/generate", new { }), "generate");
        Ok(await api.Post($"/commercialcontracts/{id}/execution-plan/submit", new { }), "submit");
        Ok(await api.Post($"/commercialcontracts/{id}/execution-plan/approve", new { }), "approve");
    }

    [Fact]
    public async Task Contract_is_created_from_the_approved_study_and_execution_waits_for_activation_and_approved_plan()
    {
        var s = await PricedStudyAsync();
        var api = s.Api;
        var fsNumber = (await api.Get($"/commercialcontracts/{s.Id}")).Data!["feasibilityNumber"].S();

        // قبل اعتماد الخطة: لا عقد ولا تنفيذ
        Assert.Equal(409, (await api.Post($"/feasibility-studies/{s.Id}/create-contract", new { })).Status);
        Assert.Equal(409, (await DailyReport(api, s.Id, Today)).Status);

        await ApprovePlanAsync(api, s.Id);
        Assert.Equal(409, (await DailyReport(api, s.Id, Today)).Status); // خطة معتمدة بلا عقد

        var created = await api.Post($"/feasibility-studies/{s.Id}/create-contract", new { });
        Ok(created, "create contract");
        Assert.Equal("contracted", created.Data!["feasibilityStatus"].S());
        Assert.StartsWith("CON-", created.Data["contractNumber"].S());
        Assert.Equal(fsNumber, created.Data["feasibilityNumber"].S());
        Assert.Equal(409, (await api.Post($"/feasibility-studies/{s.Id}/create-contract", new { })).Status); // مرة واحدة

        var contract = (await api.Get($"/commercialcontracts/{s.Id}")).Data!;
        Assert.Equal("contracted", contract["feasibilityStatus"].S());
        Assert.False(string.IsNullOrEmpty(contract["costCenterId"]?.GetValue<string?>()));
        Assert.Equal("draft", contract["stage"].S());
        Assert.Contains(s.Id, (await api.Get("/commercialcontracts?pageSize=100")).Data!["items"]!.AsArray().Select(c => c!["id"].S()));
        Assert.Contains(s.Id, (await api.Get("/construction-projects")).Data!.AsArray().Select(p => p!["contractId"].S()));
        // النسختان المجمّدتان باقيتان على العقد
        var baselines = (await api.Get($"/commercialcontracts/{s.Id}/execution-plan")).Data!["baselines"]!.AsArray();
        Assert.Contains(baselines, b => b!["kind"].S() == "feasibility");
        Assert.Contains(baselines, b => b!["kind"].S() == "plan_baseline");

        // عقد مسودة: لا تنفيذ حتى السريان
        Assert.Equal(409, (await DailyReport(api, s.Id, Today)).Status);
        Ok(await api.Post($"/commercialcontracts/{s.Id}/move", new { toStatus = "active" }), "activate");
        Ok(await DailyReport(api, s.Id, Today), "report after activation");

        // مراجعة مفتوحة للخطة توقف تنفيذاً جديداً حتى اعتمادها
        Ok(await api.Post($"/commercialcontracts/{s.Id}/execution-plan/revise", new { }), "revise");
        Assert.Equal("contracted", (await api.Get($"/commercialcontracts/{s.Id}")).Data!["feasibilityStatus"].S());
        Assert.Equal(409, (await DailyReport(api, s.Id, Today.AddDays(1))).Status);
        Ok(await api.Post($"/commercialcontracts/{s.Id}/execution-plan/submit", new { }), "submit rev");
        Ok(await api.Post($"/commercialcontracts/{s.Id}/execution-plan/approve", new { }), "approve rev");
        Ok(await DailyReport(api, s.Id, Today.AddDays(1)), "report after revision");
    }

    /// <summary>
    /// المقارنة: العمالة في الدراسة والأساس والخطة 6000 (200 ساعة)، والفعلي من تقرير يومي معتمد: عامل 8 ساعات × 30 = 240 على بند الحفر،
    /// فيظهر في فئة العمالة وفي مرحلة «أعمال ترابية». المواد بلا صرف: فعلي صفر.
    /// </summary>
    [Fact]
    public async Task Comparison_shows_study_baseline_current_and_actual_from_approved_reports()
    {
        var s = await PricedStudyAsync();
        var api = s.Api;
        await ApprovePlanAsync(api, s.Id);
        Ok(await api.Post($"/feasibility-studies/{s.Id}/create-contract", new { }), "create contract");
        Ok(await api.Post($"/commercialcontracts/{s.Id}/move", new { toStatus = "active" }), "activate");

        var resources = await api.Post($"/commercialcontracts/{s.Id}/project/resources", new
        {
            kind = "labor", name = "عامل 1", rateBasis = "hour", rate = 30, hoursPerDay = 8, daysPerMonth = 26, overtimeMultiplier = 1.5, libraryResourceId = s.Labor,
        });
        Ok(resources, "project resource");
        var worker = resources.Data!.AsArray().Single()!["id"].G();
        var report = await api.Post($"/commercialcontracts/{s.Id}/site-reports", new
        {
            reportDate = Today, resources = new[] { new { projectResourceId = worker, contractItemId = s.Dig, hours = 8 } },
            hours = new[] { new { start = "07:00", end = "08:00", contractItemId = s.Dig, actualQuantity = 5 } },
        });
        Ok(report, "report");
        var reportId = report.Data!["id"].S();
        Ok(await api.Post($"/commercialcontracts/{s.Id}/site-reports/{reportId}/submit", new { }), "submit report");

        // قبل الاعتماد لا فعلي
        var pending = (await api.Get($"/commercialcontracts/{s.Id}/execution-plan/comparison")).Data!;
        Assert.Equal(0m, pending["total"]!["actual"].D());

        Ok(await api.Post($"/commercialcontracts/{s.Id}/site-reports/{reportId}/approve", new { }), "approve report");
        var cmp = await api.Get($"/commercialcontracts/{s.Id}/execution-plan/comparison");
        Ok(cmp, "comparison");
        var c = cmp.Data!;
        Assert.Equal(1, c["baselineRevision"]!.GetValue<int>());
        var labor = c["categories"]!.AsArray().Single(r => r!["key"].S() == "labor")!;
        Assert.Equal(6000m, labor["study"].D());
        Assert.Equal(6000m, labor["baseline"].D());
        Assert.Equal(6000m, labor["current"].D());
        Assert.Equal(240m, labor["actual"].D());
        Assert.Equal(8m, labor["actualHours"].D());
        Assert.Equal(200m, labor["currentHours"].D());
        Assert.Equal(-5760m, labor["variance"].D());
        Assert.Equal(4m, labor["consumedPercent"].D());
        var material = c["categories"]!.AsArray().Single(r => r!["key"].S() == "material")!;
        Assert.Equal(1375m, material["study"].D());
        Assert.Equal(0m, material["actual"].D());
        var dig = c["phases"]!.AsArray().Single(p => p!["name"].S() == "أعمال ترابية")!;
        Assert.Equal(240m, dig["actual"].D());
        Assert.Equal(4800m, dig["current"].D());
        Assert.Equal(9175m, c["total"]!["study"].D()); // المسعّر من المكتبة (البند اليدوي 200 خارج التحليل)
    }

    [Fact]
    public async Task Direct_contract_without_a_study_keeps_executing_as_before()
    {
        var api = await NewConstructionTenantAsync();
        var (party, _) = await SeedCustomerAsync(api);
        var direct = await api.Post("/commercialcontracts", StudyBody(party, SimpleItems, study: false));
        Ok(direct, "direct");
        var id = direct.Data!["id"].S();
        Ok(await api.Post($"/commercialcontracts/{id}/move", new { toStatus = "active" }), "activate");
        Ok(await DailyReport(api, id, Today), "report without plan");
        Assert.Equal(404, (await api.Post($"/feasibility-studies/{Guid.NewGuid()}/create-contract", new { })).Status);
        Assert.Equal(409, (await api.Post($"/feasibility-studies/{id}/create-contract", new { })).Status);
    }
}
