using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// تحليل تكلفة دراسة الجدوى المجمّد من مكتبة التكاليف: البند «صب» لكل وحدة = رمل 0.5 بهدر 10% (50) + عامل ساعتان (30/س من التفصيل)
/// + حفار 0.1 ساعة (180/س مملوكة) = 27.5 + 60 + 18 = 105.5. التسعير صريح بمعاينة فروق، وتغيّر المكتبة لا يغيّر الدراسة،
/// والتعديل اليدوي بسبب يبقى عند التحديث، وحفظ العقد يأخذ تكلفة السطر من تحليله، وبعد المسودة للقراءة.
/// </summary>
[Collection("api")]
public class ContractCostAnalysisTests : TestBase
{
    public ContractCostAnalysisTests(ErpFactory f) : base(f) { }

    private static readonly DateTime Today = DateTime.UtcNow.Date;

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");

    private sealed record Setup(Client Api, string ContractId, Guid Party, Guid Sand, Guid Labor, Guid Excavator, Guid Work, Guid Line, Guid ManualLine, object[] Items);

    private async Task<Setup> SetupAsync(string workflow = "full")
    {
        var api = await NewConstructionTenantAsync();
        await api.Post("/unitsofmeasure", new { code = "PCS", nameAr = "قطعة", isBaseUnit = true, conversionFactor = 1 });
        Ok(await api.Post("/unitsofmeasure", new { code = "HR", nameAr = "ساعة", unitType = "time", isBaseUnit = true, conversionFactor = 1 }), "HR");

        async Task<Guid> Resource(object body, string step)
        {
            var r = await api.Post("/construction-resources", body);
            Ok(r, step);
            return r.Data!["id"].G();
        }
        var sand = await Resource(new { nameAr = "رمل", resourceType = "material", unit = "PCS", costSource = "manual", standardCost = 50 }, "sand");
        var labor = await Resource(new
        {
            nameAr = "عامل", resourceType = "labor", unit = "HR", costSource = "breakdown", hoursPerUnit = 1,
            breakdown = new { rateBasis = "day", rate = 240, hoursPerDay = 8, overtimeMultiplier = 1.5 },
        }, "labor");
        var excavator = await Resource(new
        {
            nameAr = "حفار", resourceType = "equipment", unit = "HR", costSource = "breakdown", hoursPerUnit = 1, equipmentOwnership = "owned",
            breakdown = new { rateBasis = "hour", rate = 100, fuelPerHour = 40, maintenancePerHour = 10, depreciationPerHour = 25, otherPerHour = 5, overtimeMultiplier = 1 },
        }, "excavator");

        var work = await api.Post("/work-items", new
        {
            nameAr = "صب", unit = "PCS", pricingMode = "manual", sellPrice = 200,
            components = new object[]
            {
                new { resourceId = sand, quantityMode = "per_unit", consumptionFactor = 0.5, wastePercent = 10 },
                new { resourceId = labor, quantityMode = "per_unit", consumptionFactor = 2, wastePercent = 0 },
                new { resourceId = excavator, quantityMode = "per_unit", consumptionFactor = 0.1, wastePercent = 0 },
            },
        });
        Ok(work, "work item");
        var workId = work.Data!["id"].G();

        var (party, _) = await SeedCustomerAsync(api);
        var items = new object[]
        {
            new { description = "صب", unit = "م3", quantity = 100, unitPrice = 200, vatRate = 15, lineKind = "measured", workItemId = workId, estimatedUnitCost = 90, sortOrder = 1 },
            new { description = "بند يدوي", unit = "م2", quantity = 10, unitPrice = 100, vatRate = 15, lineKind = "measured", estimatedUnitCost = 40, sortOrder = 2 },
        };
        var created = await api.Post("/commercialcontracts", Body(party, workflow, items));
        Ok(created, "contract");
        var id = created.Data!["id"].S();
        var lines = created.Data["items"]!.AsArray();
        return new Setup(api, id, party, sand, labor, excavator, workId,
            lines.Single(l => l!["description"].S() == "صب")!["id"].G(), lines.Single(l => l!["description"].S() == "بند يدوي")!["id"].G(), items);
    }

    private static object Body(Guid party, string workflow, object[] items) => new
    {
        title = "دراسة صب", contractType = "construction_boq", workflow, requiresGuarantee = false, partyName = "عميل", partyId = party,
        startDate = Today, endDate = Today.AddMonths(6), vatRate = 15, items, milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
    };

    private static async Task<JsonNode> AnalysisAsync(Client api, string id)
    {
        var r = await api.Get($"/commercialcontracts/{id}/cost-analysis");
        Ok(r, "analysis");
        return r.Data!;
    }

    private static JsonNode Line(JsonNode analysis, Guid lineId) => analysis["lines"]!.AsArray().Single(l => l!["contractItemId"].G() == lineId)!;

    private static JsonNode Row(JsonNode line, Guid resourceId) => line["resources"]!.AsArray().Single(r => r!["resourceId"].G() == resourceId)!;

    [Fact]
    public async Task Pricing_from_the_library_is_explicit_and_freezes_cost_by_resource_type()
    {
        var s = await SetupAsync();
        var api = s.Api;

        // قبل التسعير: السطر المرتبط يدوي بتكلفته المرسلة، وغير المرتبط لا يُحلَّل
        var before = await AnalysisAsync(api, s.ContractId);
        Assert.True(before["editable"]!.GetValue<bool>());
        Assert.Equal("manual", Line(before, s.Line)["status"].S());
        Assert.Equal("skipped", Line(before, s.ManualLine)["status"].S());
        Assert.Equal("not_linked", Line(before, s.ManualLine)["skipReason"].S());
        Assert.Equal(9400m, before["estimatedCost"].D()); // 100 × 90 + 10 × 40

        var preview = await api.Get($"/commercialcontracts/{s.ContractId}/cost-analysis/preview");
        Ok(preview, "preview");
        var pl = preview.Data!["lines"]!.AsArray().Single(l => l!["contractItemId"].G() == s.Line)!;
        Assert.Equal("new", pl["status"].S());
        Assert.Equal(90m, pl["currentUnitCost"].D());
        Assert.Equal(105.5m, pl["newUnitCost"].D());
        Assert.Equal(3, pl["changes"]!.AsArray().Count(c => c!["kind"].S() == "added"));
        Assert.Equal(9400m, preview.Data["currentTotal"].D());
        Assert.Equal(10950m, preview.Data["newTotal"].D());
        // المعاينة لا تحفظ
        Assert.Equal("manual", Line(await AnalysisAsync(api, s.ContractId), s.Line)["status"].S());

        var applied = await api.Post($"/commercialcontracts/{s.ContractId}/cost-analysis/apply", new { });
        Ok(applied, "apply");
        var a = applied.Data!;
        var line = Line(a, s.Line);
        Assert.Equal("priced", line["status"].S());
        Assert.Equal(105.5m, line["estimatedUnitCost"].D());
        Assert.Equal(10950m, a["estimatedCost"].D());
        Assert.Equal(10550m, a["analyzedCost"].D());

        var sand = Row(line, s.Sand);
        Assert.Equal(55m, sand["quantity"].D());
        Assert.Equal(0.55m, sand["quantityPerUnit"].D());
        Assert.Equal(50m, sand["unitCost"].D());
        Assert.Equal("standard", sand["costStatus"].S());
        Assert.StartsWith(Today.ToString("yyyy-MM-dd"), sand["costEffectiveFrom"].S());
        var input = sand["inputs"]!.AsArray().Single()!;
        Assert.Equal(0.5m, input["consumptionFactor"].D());
        Assert.Equal(10m, input["wastePercent"].D());

        var totals = a["totalsByType"]!.AsArray().ToDictionary(t => t!["resourceType"].S(), t => t!);
        Assert.Equal(2750m, totals["material"]["total"].D());
        Assert.Equal(6000m, totals["labor"]["total"].D());
        Assert.Equal(200m, totals["labor"]["hours"].D());
        Assert.Equal(1800m, totals["equipment"]["total"].D());
        Assert.Equal(10m, totals["equipment"]["hours"].D());

        // العقد نفسه يحمل التكلفة التقديرية الجديدة
        var contract = (await api.Get($"/commercialcontracts/{s.ContractId}")).Data!;
        Assert.Equal(10950m, contract["estimatedCost"].D());
    }

    [Fact]
    public async Task Library_changes_do_not_touch_the_study_until_refresh_and_overrides_survive_it()
    {
        var s = await SetupAsync();
        var api = s.Api;
        Ok(await api.Post($"/commercialcontracts/{s.ContractId}/cost-analysis/apply", new { }), "apply");

        // سعر الرمل في المكتبة 80: الدراسة كما هي
        var sandRes = (await api.Get($"/construction-resources/{s.Sand}")).Data!;
        Ok(await api.Put($"/construction-resources/{s.Sand}", new
        {
            code = sandRes["code"].S(), nameAr = "رمل", nameEn = "", resourceType = "material", unit = "PCS", costSource = "manual", standardCost = 80, isActive = true,
        }), "raise sand");
        var unchanged = await AnalysisAsync(api, s.ContractId);
        Assert.Equal(105.5m, Line(unchanged, s.Line)["estimatedUnitCost"].D());
        Assert.Equal(10950m, unchanged["estimatedCost"].D());

        var preview = (await api.Get($"/commercialcontracts/{s.ContractId}/cost-analysis/preview")).Data!;
        var pl = preview["lines"]!.AsArray().Single(l => l!["contractItemId"].G() == s.Line)!;
        Assert.Equal("changed", pl["status"].S());
        Assert.Equal(122m, pl["newUnitCost"].D()); // 0.55 × 80 + 60 + 18
        var change = pl["changes"]!.AsArray().Single()!;
        Assert.Equal(s.Sand, change["resourceId"].G());
        Assert.Equal(50m, change["oldUnitCost"].D());
        Assert.Equal(80m, change["newUnitCost"].D());

        // تعديل يدوي للعامل بسبب (والسبب لازم)
        var laborRow = Row(Line(unchanged, s.Line), s.Labor)["id"].G();
        Assert.Equal(400, (await api.Put($"/commercialcontracts/{s.ContractId}/cost-analysis/{laborRow}", new { unitCost = 25 })).Status);
        var overridden = await api.Put($"/commercialcontracts/{s.ContractId}/cost-analysis/{laborRow}", new { unitCost = 25, reason = "عرض مقاول عمالة" });
        Ok(overridden, "override");
        var ol = Line(overridden.Data!, s.Line);
        Assert.Equal(95.5m, ol["estimatedUnitCost"].D()); // 27.5 + 50 + 18
        Assert.Equal("overridden", Row(ol, s.Labor)["costStatus"].S());
        Assert.Equal(30m, Row(ol, s.Labor)["standardUnitCost"].D());

        // التحديث الصريح: الرمل «محدّث» بسعره الجديد، والعامل يبقى معدّلاً
        var refreshed = await api.Post($"/commercialcontracts/{s.ContractId}/cost-analysis/apply", new { keepOverrides = true });
        Ok(refreshed, "refresh");
        var rl = Line(refreshed.Data!, s.Line);
        Assert.Equal(112m, rl["estimatedUnitCost"].D()); // 44 + 50 + 18
        Assert.Equal("updated", Row(rl, s.Sand)["costStatus"].S());
        Assert.Equal("overridden", Row(rl, s.Labor)["costStatus"].S());
        Assert.Equal("عرض مقاول عمالة", Row(rl, s.Labor)["overrideReason"].S());

        // تسعير بتاريخ ماضٍ قبل رفع السعر يأخذ سعر ذلك اليوم من السجل
        Ok(await api.Post($"/construction-resources/{s.Sand}/costs", new { effectiveFrom = Today.AddDays(-30), unitCost = 45, reason = "سعر الشهر الماضي" }), "past price");
        var past = (await api.Get($"/commercialcontracts/{s.ContractId}/cost-analysis/preview?date={Today.AddDays(-10):yyyy-MM-dd}")).Data!;
        var pastSand = past["lines"]!.AsArray().Single(l => l!["contractItemId"].G() == s.Line)!["changes"]!.AsArray().Single(c => c!["resourceId"].G() == s.Sand)!;
        Assert.Equal(45m, pastSand["newUnitCost"].D());
    }

    [Fact]
    public async Task Contract_save_takes_line_cost_from_the_frozen_analysis_and_drops_it_when_the_line_is_relinked()
    {
        var s = await SetupAsync();
        var api = s.Api;
        Ok(await api.Post($"/commercialcontracts/{s.ContractId}/cost-analysis/apply", new { }), "apply");

        // الطلب يرسل تكلفة قديمة للسطر المسعّر: الخادم يبقي تكلفة التحليل
        var items = new object[]
        {
            new { id = s.Line, description = "صب", unit = "م3", quantity = 100, unitPrice = 200, vatRate = 15, lineKind = "measured", workItemId = s.Work, estimatedUnitCost = 1, sortOrder = 1 },
            new { id = s.ManualLine, description = "بند يدوي", unit = "م2", quantity = 10, unitPrice = 100, vatRate = 15, lineKind = "measured", estimatedUnitCost = 40, sortOrder = 2 },
        };
        var saved = await api.Put($"/commercialcontracts/{s.ContractId}", Body(s.Party, "full", items));
        Ok(saved, "save");
        Assert.Equal(105.5m, saved.Data!["items"]!.AsArray().Single(i => i!["id"].G() == s.Line)!["estimatedUnitCost"].D());
        Assert.Equal(10950m, saved.Data["estimatedCost"].D());

        // فك ربط السطر بالمكتبة يُسقط تحليله فتعود تكلفته يدوية
        var unlinked = new object[]
        {
            new { id = s.Line, description = "صب", unit = "م3", quantity = 100, unitPrice = 200, vatRate = 15, lineKind = "measured", estimatedUnitCost = 70, sortOrder = 1 },
            items[1],
        };
        Ok(await api.Put($"/commercialcontracts/{s.ContractId}", Body(s.Party, "full", unlinked)), "unlink");
        var a = await AnalysisAsync(api, s.ContractId);
        Assert.NotEqual("priced", Line(a, s.Line)["status"].S());
        Assert.Equal(70m, Line(a, s.Line)["estimatedUnitCost"].D());
        Assert.Empty(a["totalsByType"]!.AsArray());
    }

    [Fact]
    public async Task Analysis_is_read_only_after_draft_and_overrides_need_the_cost_library_permission()
    {
        var s = await SetupAsync(workflow: "simple");
        var api = s.Api;
        var applied = await api.Post($"/commercialcontracts/{s.ContractId}/cost-analysis/apply", new { });
        Ok(applied, "apply");
        var row = Row(Line(applied.Data!, s.Line), s.Sand)["id"].G();

        Ok(await api.Post($"/commercialcontracts/{s.ContractId}/move", new { toStatus = "active" }), "activate");
        var locked = await AnalysisAsync(api, s.ContractId);
        Assert.False(locked["editable"]!.GetValue<bool>());
        Assert.Equal(409, (await api.Post($"/commercialcontracts/{s.ContractId}/cost-analysis/apply", new { })).Status);
        Assert.Equal(409, (await api.Put($"/commercialcontracts/{s.ContractId}/cost-analysis/{row}", new { unitCost = 1, reason = "x" })).Status);
        Assert.Equal(105.5m, Line(locked, s.Line)["estimatedUnitCost"].D());

        // دور يعدّل العقود دون مكتبة التكاليف: يسعّر ولا يعدّل تكلفة يدوياً
        var other = await SetupAsync();
        var owner = other.Api;
        var role = (await owner.Post("/roles", new { nameAr = "معد دراسات" })).Data!["code"].S();
        Ok(await owner.Post("/permissions", new
        {
            roleId = role,
            permissions = new[]
            {
                new { screenId = "commercial-contracts", screenNameAr = "", screenNameEn = "", actions = new[] { "view", "create", "edit" } },
                new { screenId = "construction-cost-library", screenNameAr = "", screenNameEn = "", actions = new[] { "view" } },
            },
        }), "permissions");
        var roleId = (await owner.Get("/roles")).Data!.AsArray().First(r => r!["code"].S() == role)!["id"].G();
        var email = $"s{Guid.NewGuid():N}@test.com";
        Ok(await owner.Post("/users", new { name = "معد", email, phone = "1", password = "Passw0rd!", role = "sales_rep", customRoleId = roleId }), "user");
        var preparer = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");
        var priced = await preparer.Post($"/commercialcontracts/{other.ContractId}/cost-analysis/apply", new { });
        Ok(priced, "preparer apply");
        var otherRow = Row(Line(priced.Data!, other.Line), other.Sand)["id"].G();
        Assert.Equal(403, (await preparer.Put($"/commercialcontracts/{other.ContractId}/cost-analysis/{otherRow}", new { unitCost = 1, reason = "x" })).Status);
    }

    [Fact]
    public async Task Another_company_cannot_read_the_study_cost_analysis()
    {
        var s = await SetupAsync();
        var stranger = await NewConstructionTenantAsync();
        Assert.Equal(404, (await stranger.Get($"/commercialcontracts/{s.ContractId}/cost-analysis")).Status);
        Assert.Equal(404, (await stranger.Post($"/commercialcontracts/{s.ContractId}/cost-analysis/apply", new { })).Status);
    }
}
