using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// البند المركّب في المكتبة من التحليل إلى الخطة: مثال «قاعدة خرسانية» (7 أسمنت، 10 حديد، 0.5 رمل، ساعتا عامل، نصف ساعة حفار)
/// × 50؛ و«تركيب عمود» باحتياجات مباشرة (رافعة 0.5 س لكل وحدة، وحفار 4 س ثابتة) وفروع (قاعدة، كابلات بهدر 5%، وتركيب بتكلفة يدوية 100).
/// يتحقق: الاحتياجات المباشرة للمجموعة على سطر «الأعمال المباشرة» بلا ضياع ولا تكرار، والتكلفة اليدوية للفرعي داخل الدراسة والخطة،
/// والثابت مرة لكل سطر في المكتبة والدراسة معاً، وتنبيه التكلفة الصفرية، وتجميع ساعات المورد المتكرر في الخطة.
/// </summary>
[Collection("api")]
public class WorkItemCompositeCostingTests : TestBase
{
    public WorkItemCompositeCostingTests(ErpFactory f) : base(f) { }

    private static readonly DateTime Today = DateTime.UtcNow.Date;
    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");

    private sealed record Lib(Client Api, Guid Cement, Guid Steel, Guid Sand, Guid Cable, Guid Worker, Guid Excavator, Guid Crane, Guid Customer);

    private async Task<Lib> LibraryAsync()
    {
        var api = await NewConstructionTenantAsync();
        foreach (var (code, name, type) in new[] { ("PCS", "عدد", "count"), ("KG", "كجم", "weight"), ("M3", "م3", "volume"), ("HR", "ساعة", "time"), ("M", "متر", "length") })
            Ok(await api.Post("/unitsofmeasure", new { code, nameAr = name, unitType = type, isBaseUnit = true, conversionFactor = 1 }), code);
        Ok(await api.Post("/unitsofmeasure", new { code = "BAG", nameAr = "كيس", unitType = "count", isBaseUnit = false, baseUnitCode = "PCS", conversionFactor = 1 }), "BAG");
        async Task<Guid> R(object body) { var r = await api.Post("/construction-resources", body); Ok(r, "resource"); return r.Data!["id"].G(); }
        var (customer, _) = await SeedCustomerAsync(api);
        return new Lib(api,
            await R(new { nameAr = "أسمنت", resourceType = "material", unit = "BAG", costSource = "manual", standardCost = 18 }),
            await R(new { nameAr = "حديد", resourceType = "material", unit = "KG", costSource = "manual", standardCost = 3.5 }),
            await R(new { nameAr = "رمل", resourceType = "material", unit = "M3", costSource = "manual", standardCost = 60 }),
            await R(new { nameAr = "كابل", resourceType = "material", unit = "M", costSource = "manual", standardCost = 34 }),
            await R(new { nameAr = "عامل مدني", resourceType = "labor", unit = "HR", costSource = "breakdown", hoursPerUnit = 1, breakdown = new { rateBasis = "hour", rate = 28, overtimeMultiplier = 1.5 } }),
            await R(new { nameAr = "حفار", resourceType = "equipment", unit = "HR", costSource = "breakdown", hoursPerUnit = 1, equipmentOwnership = "owned", breakdown = new { rateBasis = "hour", rate = 180, overtimeMultiplier = 1 } }),
            await R(new { nameAr = "رافعة", resourceType = "equipment", unit = "HR", costSource = "breakdown", hoursPerUnit = 1, equipmentOwnership = "rented", breakdown = new { rateBasis = "hour", rate = 260, overtimeMultiplier = 1 } }),
            customer);
    }

    private static object Need(Guid r, decimal f, decimal waste = 0) => new { resourceId = r, quantityMode = "per_unit", consumptionFactor = f, wastePercent = waste };

    private static async Task<(string Id, JsonArray Items)> StudyAsync(Lib l, object[] items)
    {
        var s = await l.Api.Post("/commercialcontracts", new
        {
            title = "دراسة بند مركّب", projectName = "إنارة", contractType = "construction_boq", workflow = "simple", requiresGuarantee = false,
            partyName = "عميل", partyId = l.Customer, startDate = Today, endDate = Today.AddDays(29), vatRate = 15, items,
            milestones = Array.Empty<object>(), clauses = Array.Empty<object>(), isFeasibilityStudy = true,
        });
        Ok(s, "study");
        return (s.Data!["id"].S(), s.Data["items"]!.AsArray());
    }

    /// <summary>عمود: مباشر (رافعة 0.5 س لكل وحدة = 130، وحفار 4 س ثابتة = 720)، وفروع: قاعدة 156، كابلات 25 م بهدر 5% = 892.5، وتركيب يدوي 100.</summary>
    private static async Task<JsonNode> PoleAsync(Lib l)
    {
        var w = await l.Api.Post("/work-items", new
        {
            code = "POLE-01", nameAr = "تركيب عمود إنارة", unit = "PCS", pricingMode = "manual", sellPrice = 4000, sectionName = "الأعمدة",
            components = new object[] { Need(l.Crane, 0.5m), new { resourceId = l.Excavator, quantityMode = "fixed", fixedQuantity = 4, wastePercent = 0 } },
            subItems = new object[]
            {
                new { lineCode = "1.1", description = "قاعدة خرسانية", unit = "PCS", quantityMode = "per_unit", quantityFactor = 1, components = new[] { Need(l.Cement, 7), Need(l.Sand, 0.5m) } },
                new { lineCode = "1.2", description = "كابلات", unit = "M", quantityMode = "per_unit", quantityFactor = 25, components = new[] { Need(l.Cable, 1, 5) } },
                new { lineCode = "1.3", description = "أعمال تركيب", unit = "PCS", quantityMode = "per_unit", quantityFactor = 1, unitCost = 100, components = Array.Empty<object>() },
            },
        });
        Ok(w, "pole");
        return w.Data!;
    }

    private static decimal Qty(JsonArray rows, Guid resource) => rows.Where(r => r!["resourceId"].G() == resource).Sum(r => r!["quantity"].D());

    [Fact]
    public async Task Concrete_base_example_derives_quantities_costs_and_plan_hours()
    {
        var l = await LibraryAsync();
        var api = l.Api;
        var w = await api.Post("/work-items", new
        {
            code = "BASE-01", nameAr = "تنفيذ قاعدة خرسانية لعمود إنارة", unit = "PCS", pricingMode = "manual", sellPrice = 450, sectionName = "قواعد الأعمدة",
            components = new[] { Need(l.Cement, 7), Need(l.Steel, 10), Need(l.Sand, 0.5m), Need(l.Worker, 2), Need(l.Excavator, 0.5m) },
        });
        Ok(w, "base");
        Assert.Equal(337m, w.Data!["unitCost"].D()); // 126 + 35 + 30 + 56 + 90
        Assert.Equal(0m, w.Data["fixedCost"].D());

        var (id, _) = await StudyAsync(l, new object[]
        {
            new { description = "قاعدة خرسانية", unit = "عدد", quantity = 50, unitPrice = 450, vatRate = 15, lineKind = "measured", workItemId = w.Data["id"].G(), sortOrder = 1 },
        });
        var applied = (await api.Post($"/commercialcontracts/{id}/cost-analysis/apply", new { })).Data!;
        var rows = applied["lines"]![0]!["resources"]!.AsArray();
        Assert.Equal(350m, Qty(rows, l.Cement));
        Assert.Equal(500m, Qty(rows, l.Steel));
        Assert.Equal(25m, Qty(rows, l.Sand));
        Assert.Equal(100m, Qty(rows, l.Worker));
        Assert.Equal(25m, Qty(rows, l.Excavator));
        Assert.Equal(16_850m, applied["analyzedCost"].D());
        var byType = applied["totalsByType"]!.AsArray().ToDictionary(t => t!["resourceType"].S(), t => t!);
        Assert.Equal(9_550m, byType["material"]["total"].D());
        Assert.Equal(2_800m, byType["labor"]["total"].D());
        Assert.Equal(100m, byType["labor"]["hours"].D());
        Assert.Equal(4_500m, byType["equipment"]["total"].D());

        var plan = (await api.Post($"/commercialcontracts/{id}/execution-plan/generate", new { })).Data!;
        Assert.Equal(350m, plan["materialTotals"]!.AsArray().Single(m => m!["resourceId"].G() == l.Cement)!["quantity"].D());
        var totals = plan["resourceTotals"]!.AsArray();
        Assert.Equal(100m, totals.Single(t => t!["resourceId"].G() == l.Worker)!["plannedHours"].D());
        Assert.Equal(25m, totals.Single(t => t!["resourceId"].G() == l.Excavator)!["plannedHours"].D());
        Assert.Equal(16_850m, plan["budget"]!["plannedCost"].D());
    }

    [Fact]
    public async Task Library_unit_cost_excludes_the_fixed_part_which_the_study_counts_once_per_line()
    {
        var l = await LibraryAsync();
        var lib = await PoleAsync(l);
        // لكل وحدة: رافعة 130 + قاعدة 156 + كابلات 892.5 + تركيب 100 = 1,278.5؛ والثابت حفار 4 × 180 = 720 لكل سطر
        Assert.Equal(1_278.5m, lib["unitCost"].D());
        Assert.Equal(720m, lib["fixedCost"].D());
        Assert.Equal(1_998.5m, lib["totalCost"].D());

        // سطر واحد (استيراد بالرمز): 50 × 1,278.5 + 720 = 64,645 — يطابق المكتبة، ومنها التكلفة اليدوية 5,000
        var (id, _) = await StudyAsync(l, new object[]
        {
            new { description = "تركيب عمود", unit = "عدد", quantity = 50, unitPrice = 4000, vatRate = 15, lineKind = "measured", workItemId = lib["id"].G(), sortOrder = 1 },
        });
        var applied = (await l.Api.Post($"/commercialcontracts/{id}/cost-analysis/apply", new { })).Data!;
        Assert.Equal(64_645m, applied["analyzedCost"].D());
        Assert.Equal(64_645m, applied["estimatedCost"].D());
        var manual = applied["lines"]![0]!["resources"]!.AsArray().Single(r => r!["costSource"].S() == "sub_item_manual")!;
        Assert.Equal("other", manual["resourceType"].S());
        Assert.Equal(50m, manual["quantity"].D());
        Assert.Equal(5_000m, manual["totalCost"].D());
        Assert.Equal(4m, Qty(applied["lines"]![0]!["resources"]!.AsArray(), l.Excavator));

        // الخطة: التكلفة اليدوية احتياج «أخرى» فيطابق المخطط الدراسة، وإعادة التوليد لا تكرره
        var plan = (await l.Api.Post($"/commercialcontracts/{id}/execution-plan/generate", new { })).Data!;
        Assert.Equal(64_645m, plan["budget"]!["plannedCost"].D());
        Assert.Equal(5_000m, plan["budget"]!["otherCost"].D());
        var again = (await l.Api.Post($"/commercialcontracts/{id}/execution-plan/generate", new { })).Data!;
        Assert.Single(again["resources"]!.AsArray(), r => r!["resourceType"].S() == "other");
    }

    [Fact]
    public async Task Group_direct_needs_are_carried_by_the_direct_work_line_without_loss_or_double_count()
    {
        var l = await LibraryAsync();
        var lib = await PoleAsync(l);
        var subs = lib["subItems"]!.AsArray();
        var workId = lib["id"].G();

        // كما تبنيه «إضافة من المكتبة»: الرئيسي مجموعة، وتحته سطر الأعمال المباشرة (بند المكتبة نفسه) ثم الفروع
        var (id, items) = await StudyAsync(l, new object[]
        {
            new { description = "تركيب عمود", unit = "عدد", quantity = 50, unitPrice = 4000, vatRate = 15, lineKind = "measured", workItemId = workId, sortOrder = 1 },
            new { description = "تركيب عمود — الأعمال المباشرة", unit = "عدد", quantity = 50, unitPrice = 500, vatRate = 15, lineKind = "measured", isSubItem = true, workItemId = workId, sortOrder = 2 },
            new { description = "قاعدة خرسانية", unit = "عدد", quantity = 50, unitPrice = 1000, vatRate = 15, lineKind = "measured", isSubItem = true, workSubItemId = subs[0]!["id"].G(), sortOrder = 3 },
            new { description = "كابلات", unit = "م", quantity = 1250, unitPrice = 80, vatRate = 15, lineKind = "measured", isSubItem = true, workSubItemId = subs[1]!["id"].G(), sortOrder = 4 },
            new { description = "أعمال تركيب", unit = "عدد", quantity = 50, unitPrice = 1000, vatRate = 15, lineKind = "measured", isSubItem = true, workSubItemId = subs[2]!["id"].G(), sortOrder = 5 },
        });
        Assert.Equal(workId, items.Single(i => i!["sortOrder"]!.GetValue<int>() == 2)!["workItemId"].G());

        var applied = (await l.Api.Post($"/commercialcontracts/{id}/cost-analysis/apply", new { })).Data!;
        var lines = applied["lines"]!.AsArray();
        var direct = lines.Single(x => x!["description"].S().Contains("المباشرة"))!;
        Assert.Equal("priced", direct["status"].S());
        Assert.Equal(25m, Qty(direct["resources"]!.AsArray(), l.Crane));       // 50 × 0.5
        Assert.Equal(4m, Qty(direct["resources"]!.AsArray(), l.Excavator));    // ثابت مرة واحدة
        Assert.Equal(7_220m, direct["estimatedTotal"].D());                     // 6,500 + 720
        Assert.Equal("priced", lines.Single(x => x!["description"].S() == "أعمال تركيب")!["status"].S());
        Assert.Null(lines.Single(x => x!["status"].S() == "group")!["skipReason"]?.GetValue<string?>());
        // كل الاحتياجات مرة واحدة: 7,220 + 7,800 + 44,625 + 5,000 = 64,645 (= السطر الواحد)
        Assert.Equal(64_645m, applied["analyzedCost"].D());
        Assert.Equal(64_645m, applied["estimatedCost"].D());
        var totalsByType = applied["totalsByType"]!.AsArray().ToDictionary(t => t!["resourceType"].S(), t => t!["total"].D());
        Assert.Equal(7_220m, totalsByType["equipment"]);

        var plan = (await l.Api.Post($"/commercialcontracts/{id}/execution-plan/generate", new { })).Data!;
        Assert.Equal(64_645m, plan["budget"]!["plannedCost"].D());
        Assert.Equal(25m, plan["resourceTotals"]!.AsArray().Single(t => t!["resourceId"].G() == l.Crane)!["plannedHours"].D());
        Assert.DoesNotContain(plan["warnings"]!.AsArray(), w => w!["code"].S() == "group_direct_unassigned");

        // الحفظ يبقي سطر الأعمال المباشرة وتحليله
        var saved = (await l.Api.Get($"/commercialcontracts/{id}/cost-analysis")).Data!;
        Assert.Equal(64_645m, saved["analyzedCost"].D());
    }

    [Fact]
    public async Task Group_without_direct_work_line_is_flagged_instead_of_silently_losing_its_needs()
    {
        var l = await LibraryAsync();
        var lib = await PoleAsync(l);
        var subs = lib["subItems"]!.AsArray();
        var (id, _) = await StudyAsync(l, new object[]
        {
            new { description = "تركيب عمود", unit = "عدد", quantity = 50, unitPrice = 4000, vatRate = 15, lineKind = "measured", workItemId = lib["id"].G(), sortOrder = 1 },
            new { description = "قاعدة خرسانية", unit = "عدد", quantity = 50, unitPrice = 1000, vatRate = 15, lineKind = "measured", isSubItem = true, workSubItemId = subs[0]!["id"].G(), sortOrder = 2 },
        });
        var applied = (await l.Api.Post($"/commercialcontracts/{id}/cost-analysis/apply", new { })).Data!;
        Assert.Equal("group_direct_unassigned", applied["lines"]!.AsArray().Single(x => x!["status"].S() == "group")!["skipReason"].S());
        var plan = (await l.Api.Post($"/commercialcontracts/{id}/execution-plan/generate", new { })).Data!;
        Assert.Contains(plan["warnings"]!.AsArray(), w => w!["code"].S() == "group_direct_unassigned" && w["count"]!.GetValue<int>() == 1);
    }

    [Fact]
    public async Task Zero_cost_resource_and_repeated_hours_are_reported_in_the_plan()
    {
        var l = await LibraryAsync();
        var api = l.Api;
        var free = await api.Post("/construction-resources", new { nameAr = "شريط تحذير", resourceType = "material", unit = "M", costSource = "manual", standardCost = 0 });
        Ok(free, "zero cost");
        var dig = await api.Post("/work-items", new { nameAr = "حفر", unit = "M", pricingMode = "manual", sellPrice = 40, sectionName = "مدني",
            components = new[] { Need(l.Worker, 0.2m), Need(free.Data!["id"].G(), 1) } });
        var lay = await api.Post("/work-items", new { nameAr = "تمديد كابل", unit = "M", pricingMode = "manual", sellPrice = 60, sectionName = "كهرباء",
            components = new[] { Need(l.Worker, 0.1m), Need(l.Cable, 1) } });
        Ok(dig, "dig"); Ok(lay, "lay");
        var (id, _) = await StudyAsync(l, new object[]
        {
            new { description = "حفر", unit = "م", quantity = 1000, unitPrice = 40, vatRate = 15, lineKind = "measured", workItemId = dig.Data!["id"].G(), sortOrder = 1 },
            new { description = "تمديد", unit = "م", quantity = 1000, unitPrice = 60, vatRate = 15, lineKind = "measured", workItemId = lay.Data!["id"].G(), sortOrder = 2 },
        });
        Ok(await api.Post($"/commercialcontracts/{id}/cost-analysis/apply", new { }), "price");
        var plan = (await api.Post($"/commercialcontracts/{id}/execution-plan/generate", new { })).Data!;

        var zero = plan["warnings"]!.AsArray().Single(w => w!["code"].S() == "resources_without_cost")!;
        Assert.Equal(1, zero["count"]!.GetValue<int>());
        Assert.Contains("شريط تحذير", zero["detail"].S());
        // العامل في بندين: سطران في الاحتياجات، وسطر مجمّع 200 + 100 = 300 ساعة
        Assert.Equal(2, plan["resources"]!.AsArray().Count(r => r!["resourceId"].G() == l.Worker));
        var worker = plan["resourceTotals"]!.AsArray().Single(t => t!["resourceId"].G() == l.Worker)!;
        Assert.Equal(300m, worker["plannedHours"].D());
        Assert.Equal(8_400m, worker["plannedCost"].D());
        Assert.Equal(2, worker["lineCount"]!.GetValue<int>());
    }
}
