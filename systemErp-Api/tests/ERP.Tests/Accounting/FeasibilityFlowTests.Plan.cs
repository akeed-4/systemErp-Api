using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// خطة التنفيذ من الدراسة المجمّدة. الدراسة 30 يوماً: «حفر» 100 (باب أعمال ترابية) = حفار 0.1 س (180) + عامل 1 س (30) = 48 للوحدة
/// بـ 110 ساعة؛ «صب» 50 (بابه من بند المكتبة: خرسانة) = رمل 0.55 (50) + عامل 2 س = 87.5 بـ 100 ساعة؛ وبند يدوي غير مسعّر (10 × 20).
/// </summary>
public partial class FeasibilityFlowTests
{
    private sealed record Study(Client Api, string Id, Guid Party, Guid Dig, Guid Pour, Guid Manual, Guid Sand, Guid Labor, Guid Excavator, object[] Items);

    private async Task<Study> PricedStudyAsync(string workflow = "simple")
    {
        var api = await NewConstructionTenantAsync();
        await api.Post("/unitsofmeasure", new { code = "PCS", nameAr = "قطعة", isBaseUnit = true, conversionFactor = 1 });
        Ok(await api.Post("/unitsofmeasure", new { code = "HR", nameAr = "ساعة", unitType = "time", isBaseUnit = true, conversionFactor = 1 }), "HR");
        async Task<Guid> Post(string url, object body, string step)
        {
            var r = await api.Post(url, body);
            Ok(r, step);
            return r.Data!["id"].G();
        }
        var sand = await Post("/construction-resources", new { nameAr = "رمل", resourceType = "material", unit = "PCS", costSource = "manual", standardCost = 50 }, "sand");
        var labor = await Post("/construction-resources", new
        {
            nameAr = "عامل", resourceType = "labor", unit = "HR", costSource = "breakdown", hoursPerUnit = 1, category = "عامل",
            breakdown = new { rateBasis = "day", rate = 240, hoursPerDay = 8, overtimeMultiplier = 1.5 },
        }, "labor");
        var excavator = await Post("/construction-resources", new
        {
            nameAr = "حفار", resourceType = "equipment", unit = "HR", costSource = "breakdown", hoursPerUnit = 1, equipmentOwnership = "owned",
            breakdown = new { rateBasis = "hour", rate = 180, overtimeMultiplier = 1 },
        }, "excavator");
        var digWork = await Post("/work-items", new
        {
            nameAr = "حفر", unit = "PCS", pricingMode = "manual", sellPrice = 80, sectionName = "أعمال ترابية",
            components = new object[]
            {
                new { resourceId = excavator, quantityMode = "per_unit", consumptionFactor = 0.1, wastePercent = 0 },
                new { resourceId = labor, quantityMode = "per_unit", consumptionFactor = 1, wastePercent = 0 },
            },
        }, "dig work");
        var pourWork = await Post("/work-items", new
        {
            nameAr = "صب", unit = "PCS", pricingMode = "manual", sellPrice = 150, sectionName = "خرسانة",
            components = new object[]
            {
                new { resourceId = sand, quantityMode = "per_unit", consumptionFactor = 0.5, wastePercent = 10 },
                new { resourceId = labor, quantityMode = "per_unit", consumptionFactor = 2, wastePercent = 0 },
            },
        }, "pour work");

        var (party, _) = await SeedCustomerAsync(api);
        var items = new object[]
        {
            new { description = "حفر", unit = "م3", quantity = 100, unitPrice = 80, vatRate = 15, lineKind = "measured", workItemId = digWork, sectionName = "أعمال ترابية", sortOrder = 1 },
            new { description = "صب", unit = "م3", quantity = 50, unitPrice = 150, vatRate = 15, lineKind = "measured", workItemId = pourWork, sortOrder = 2 },
            new { description = "بند يدوي", unit = "م2", quantity = 10, unitPrice = 40, vatRate = 15, lineKind = "measured", estimatedUnitCost = 20, sortOrder = 3 },
        };
        var body = new
        {
            title = "دراسة", projectName = "مشروع الخطة", contractType = "construction_boq", workflow, requiresGuarantee = false,
            partyName = "عميل", partyId = party, startDate = Today, endDate = Today.AddDays(29), vatRate = 15, items,
            milestones = Array.Empty<object>(), clauses = Array.Empty<object>(), isFeasibilityStudy = true,
        };
        var created = await api.Post("/commercialcontracts", body);
        Ok(created, "study");
        var id = created.Data!["id"].S();
        var lines = created.Data["items"]!.AsArray();
        Guid Line(string d) => lines.Single(l => l!["description"].S() == d)!["id"].G();
        Ok(await api.Post($"/commercialcontracts/{id}/cost-analysis/apply", new { }), "price");
        return new Study(api, id, party, Line("حفر"), Line("صب"), Line("بند يدوي"), sand, labor, excavator, items);
    }

    private static async Task<JsonNode> PlanAsync(Client api, string id)
    {
        var r = await api.Get($"/commercialcontracts/{id}/execution-plan");
        Ok(r, "plan");
        return r.Data!;
    }

    private static string[] WarningCodes(JsonNode plan) => plan["warnings"]!.AsArray().Select(w => w!["code"].S()).ToArray();

    [Fact]
    public async Task Plan_is_generated_from_the_frozen_study_by_section_with_suggested_dates_and_requirements()
    {
        var s = await PricedStudyAsync();
        var api = s.Api;
        var before = await PlanAsync(api, s.Id);
        Assert.Equal("none", before["status"].S());
        Assert.Contains("no_plan", WarningCodes(before));

        var generated = await api.Post($"/commercialcontracts/{s.Id}/execution-plan/generate", new { });
        Ok(generated, "generate");
        var plan = generated.Data!;
        Assert.Equal("draft", plan["status"].S());
        Assert.Equal(1, plan["revision"]!.GetValue<int>());

        // المراحل بالأبواب بترتيب ظهورها، وأوزانها من التكلفة (4800 / 4375 / 200 من 9375)
        var phases = plan["phases"]!.AsArray();
        Assert.Equal(new[] { "أعمال ترابية", "خرسانة", "بنود غير مصنّفة" }, phases.Select(p => p!["name"].S()).ToArray());
        Assert.Equal(new[] { 51.2m, 46.67m, 2.13m }, phases.Select(p => p!["weightPercent"].D()).ToArray());
        Assert.Equal(new[] { 4800m, 4375m, 200m }, phases.Select(p => p!["plannedCost"].D()).ToArray());
        Assert.Equal(new[] { 110m, 100m, 0m }, phases.Select(p => p!["hours"].D()).ToArray());
        Assert.All(phases, p => Assert.Equal("derived", p!["source"].S()));
        Assert.All(phases, p => Assert.True(p!["datesSuggested"]!.GetValue<bool>()));
        Assert.StartsWith(Today.ToString("yyyy-MM-dd"), phases[0]!["plannedStart"].S());
        Assert.StartsWith(Today.AddDays(29).ToString("yyyy-MM-dd"), phases[2]!["plannedEnd"].S());
        Assert.Equal(s.Dig, phases[0]!["contractItemIds"]!.AsArray().Single()!.G());

        // المواد: الرمل في مرحلة الصب بتكلفته من الدراسة
        var material = Assert.Single(plan["materials"]!.AsArray())!;
        Assert.Equal(27.5m, material["quantity"].D());
        Assert.Equal(50m, material["plannedUnitCost"].D());
        Assert.Equal(1375m, material["plannedCost"].D());
        Assert.Equal(phases[1]!["id"].S(), material["phaseId"].S());
        Assert.Equal(1, Assert.Single(plan["materialTotals"]!.AsArray())!["lineCount"]!.GetValue<int>());

        // العمالة والمعدات: بلا موظفين ولا أصول، والمعدة بملكية المكتبة، والعدد المقترح من الساعات وأيام المرحلة
        var resources = plan["resources"]!.AsArray();
        Assert.Equal(3, resources.Count);
        var excavator = resources.Single(r => r!["resourceType"].S() == "equipment")!;
        Assert.Equal(10m, excavator["plannedHours"].D());
        Assert.Equal(1800m, excavator["plannedCost"].D());
        Assert.Equal("owned", excavator["ownership"].S());
        Assert.True(excavator["suggestedCount"].D() > 0);
        Assert.Equal(200m, resources.Where(r => r!["resourceType"].S() == "labor").Sum(r => r!["plannedHours"].D()));
        Assert.Empty((await api.Get($"/commercialcontracts/{s.Id}/project/resources")).Data!.AsArray());

        // الميزانية: تُملأ من التكلفة التقديرية (كانت صفراً)، والمخطط من الخطة 9175 فيظهر الفرق تنبيهاً
        var budget = plan["budget"]!;
        Assert.Equal(9375m, budget["estimatedBudget"].D());
        Assert.Equal(9175m, budget["plannedCost"].D());
        Assert.Equal(6000m, budget["laborCost"].D());
        Assert.Equal(1800m, budget["equipmentCost"].D());
        Assert.Equal(1375m, budget["materialsCost"].D());
        var codes = WarningCodes(plan);
        Assert.Contains("lines_not_priced", codes);
        Assert.DoesNotContain("no_dependencies", codes); // المراحل المولَّدة تأتي متسلسلة بترتيب الأبواب
        Assert.Contains("no_manager", codes);
        Assert.Contains("materials_not_stocked", codes);
        Assert.Contains("budget_differs", codes);
        Assert.DoesNotContain("no_plan", codes);
    }

    [Fact]
    public async Task Regeneration_keeps_manager_edits_and_never_duplicates_them()
    {
        var s = await PricedStudyAsync();
        var api = s.Api;
        var plan = (await api.Post($"/commercialcontracts/{s.Id}/execution-plan/generate", new { })).Data!;

        // تعديل عامل الحفر (ساعات أكثر) وإضافة مشرف سلامة يدوي
        var items = plan["resources"]!.AsArray().Select(r => (JsonNode)r!.DeepClone()).ToList();
        var digLabor = items.Single(r => r["resourceType"].S() == "labor" && r["contractItemId"].S() == s.Dig.ToString())!;
        digLabor["quantity"] = 120; // الخطة مستقلة: الكمية تُعدَّل فيها (والدراسة لا تتغير)
        digLabor["suggestedCount"] = 3;
        items.Add(new JsonObject { ["resourceType"] = "labor", ["name"] = "مشرف سلامة", ["unit"] = "HR", ["quantity"] = 40, ["hoursPerUnit"] = 1, ["unitCost"] = 50, ["phaseId"] = plan["phases"]![0]!["id"]!.DeepClone() });
        var saved = await api.Put($"/commercialcontracts/{s.Id}/execution-plan/resources", new { items });
        Ok(saved, "save resources");
        Assert.Equal("modified", saved.Data!["resources"]!.AsArray().Single(r => r!["contractItemId"].S() == s.Dig.ToString() && r["resourceType"].S() == "labor")!["source"].S());

        // تعديل مرحلة مولَّدة يجعلها «معدّلة» وتاريخها غير مقترح
        var project = (await api.Get($"/commercialcontracts/{s.Id}/project")).Data!;
        var phases = project["phases"]!.AsArray().Select(p => p!.DeepClone()).ToList();
        phases[0]!["name"] = "الحفر والردم";
        phases[0]!["plannedEnd"] = Today.AddDays(10);
        Ok(await api.Put($"/commercialcontracts/{s.Id}/project/phases", new { phases }), "edit phase");

        var again = await api.Post($"/commercialcontracts/{s.Id}/execution-plan/generate", new { });
        Ok(again, "regenerate");
        var p2 = again.Data!;
        var regenerated = p2["phases"]!.AsArray();
        var edited = regenerated.Single(p => p!["name"].S() == "الحفر والردم")!;
        Assert.Equal("modified", edited["source"].S());
        Assert.False(edited["datesSuggested"]!.GetValue<bool>());
        Assert.Equal(3, regenerated.Count); // المعدّلة + خرسانة + غير مصنّفة، بلا مرحلة «أعمال ترابية» مكررة
        var res = p2["resources"]!.AsArray();
        Assert.Single(res, r => r!["contractItemId"].S() == s.Dig.ToString() && r["resourceType"].S() == "labor");
        var keptLabor = res.Single(r => r!["contractItemId"].S() == s.Dig.ToString() && r["resourceType"].S() == "labor")!;
        Assert.Equal(120m, keptLabor["quantity"].D()); // تعديل الخطة باقٍ بعد إعادة التوليد
        Assert.Equal(100m, keptLabor["studyQuantity"].D()); // ومرجع الدراسة كما هو
        Assert.Equal(3m, keptLabor["suggestedCount"].D());
        Assert.Contains(res, r => r!["name"].S() == "مشرف سلامة" && r["source"].S() == "manual");
    }

    [Fact]
    public async Task Approved_plan_becomes_the_project_budget_for_cost_profit_and_categories_until_the_next_approval()
    {
        var s = await PricedStudyAsync();
        var api = s.Api;
        var plan = (await api.Post($"/commercialcontracts/{s.Id}/execution-plan/generate", new { })).Data!;
        var studyCost = plan["budget"]!["studyCost"].D();

        // تعديل الخطة: العمالة أغلى (+10 للوحدة)
        var items = plan["resources"]!.AsArray().Select(r => (JsonNode)r!.DeepClone()).ToList();
        foreach (var l in items.Where(r => r["resourceType"].S() == "labor")) l["unitCost"] = l["unitCost"].D() + 10;
        var edited = (await api.Put($"/commercialcontracts/{s.Id}/execution-plan/resources", new { items })).Data!;
        var planCost = edited["budget"]!["plannedCost"].D();
        var laborPlan = edited["budget"]!["laborCost"].D();
        Assert.NotEqual(studyCost, planCost);

        // قبل الاعتماد: التقرير على الدراسة (لا خطة معتمدة)
        var before = (await api.Get($"/commercialcontracts/{s.Id}/project/cost-report")).Data!;
        Assert.NotEqual("approved_plan", before["budgetSource"].S());

        Ok(await api.Post($"/commercialcontracts/{s.Id}/execution-plan/submit", new { }), "submit");
        Ok(await api.Post($"/commercialcontracts/{s.Id}/execution-plan/approve", new { }), "approve");

        // بعد الاعتماد: الميزانية = الخطة المعتمدة، والمخطط لكل فئة منها، والهامش والتوقع عليها؛ والدراسة كما هي
        var report = (await api.Get($"/commercialcontracts/{s.Id}/project/cost-report")).Data!;
        Assert.Equal("approved_plan", report["budgetSource"].S());
        Assert.Equal(planCost, report["budgetAtCompletion"].D());
        Assert.Equal(1, report["approvedPlanRevision"]!.GetValue<int>());
        Assert.Equal(studyCost, report["estimatedCost"].D());
        Assert.Equal(report["contractValue"].D() - planCost, report["plannedMargin"].D());
        Assert.Equal(laborPlan, report["categories"]!.AsArray().Single(c => c!["key"].S() == "labor")!["planned"].D());
        Assert.Equal(planCost, report["estimateAtCompletion"].D()); // بلا فعلي: التوقع = الميزانية
        Assert.Equal(planCost, (await api.Get($"/commercialcontracts/{s.Id}/project")).Data!["estimatedBudget"].D());

        // مراجعة بتعديل لم يُعتمد: التقرير يبقى على المعتمد وينبّه
        Ok(await api.Post($"/commercialcontracts/{s.Id}/execution-plan/revise", new { }), "revise");
        var draft = (await api.Get($"/commercialcontracts/{s.Id}/execution-plan")).Data!;
        var items2 = draft["resources"]!.AsArray().Select(r => (JsonNode)r!.DeepClone()).ToList();
        items2.Add(new JsonObject { ["resourceType"] = "labor", ["name"] = "عامل إضافي", ["unit"] = "HR", ["quantity"] = 100, ["hoursPerUnit"] = 1, ["unitCost"] = 20 });
        Ok(await api.Put($"/commercialcontracts/{s.Id}/execution-plan/resources", new { items = items2 }), "edit revision");
        var pending = (await api.Get($"/commercialcontracts/{s.Id}/project/cost-report")).Data!;
        Assert.Equal(planCost, pending["budgetAtCompletion"].D());
        Assert.True(pending["planRevisionPending"]!.GetValue<bool>());

        // اعتماد المراجعة: الميزانية الجديدة
        Ok(await api.Post($"/commercialcontracts/{s.Id}/execution-plan/submit", new { }), "submit 2");
        Ok(await api.Post($"/commercialcontracts/{s.Id}/execution-plan/approve", new { }), "approve 2");
        var after = (await api.Get($"/commercialcontracts/{s.Id}/project/cost-report")).Data!;
        Assert.Equal(planCost + 2000m, after["budgetAtCompletion"].D());
        Assert.Equal(2, after["approvedPlanRevision"]!.GetValue<int>());
        Assert.False(after["planRevisionPending"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Plan_edits_change_plan_budget_keep_study_unchanged_persist_and_are_audited()
    {
        var s = await PricedStudyAsync();
        var api = s.Api;
        var plan = (await api.Post($"/commercialcontracts/{s.Id}/execution-plan/generate", new { })).Data!;
        var studyCost = plan["budget"]!["studyCost"].D();
        var planCost = plan["budget"]!["plannedCost"].D();
        var rowsBefore = await api.Get($"/commercialcontracts/{s.Id}/cost-analysis");

        // تعديل كمية وسعر مورد مولَّد، وحذف آخر، وإضافة يدوي
        var items = plan["resources"]!.AsArray().Select(r => (JsonNode)r!.DeepClone()).ToList();
        var labor = items.First(r => r["resourceType"].S() == "labor")!;
        var oldLaborCost = labor["plannedCost"].D();
        labor["quantity"] = labor["quantity"].D() * 2;
        labor["unitCost"] = labor["unitCost"].D() + 10;
        var newLaborCost = Math.Round(labor["quantity"].D() * labor["unitCost"].D(), 2);
        var removed = items.First(r => r["resourceType"].S() == "equipment")!;
        items.Remove(removed);
        items.Add(new JsonObject { ["resourceType"] = "subcontract", ["name"] = "عزل", ["unit"] = "LS", ["quantity"] = 1, ["hoursPerUnit"] = 0, ["unitCost"] = 5000 });
        var saved = await api.Put($"/commercialcontracts/{s.Id}/execution-plan/resources", new { items, reason = "إعادة تقدير العمالة" });
        Ok(saved, "save resources");

        var expected = planCost - oldLaborCost + newLaborCost - removed["plannedCost"].D() + 5000m;
        Assert.Equal(expected, saved.Data!["budget"]!["plannedCost"].D());
        Assert.Equal(studyCost, saved.Data["budget"]!["studyCost"].D()); // الدراسة لا تتغير

        // بعد إعادة الفتح: التعديلات محفوظة، ومرجع الدراسة على السطر
        var reopened = (await api.Get($"/commercialcontracts/{s.Id}/execution-plan")).Data!;
        Assert.Equal(expected, reopened["budget"]!["plannedCost"].D());
        var edited = reopened["resources"]!.AsArray().Single(r => r!["id"].S() == labor["id"].S())!;
        Assert.Equal("modified", edited["source"].S());
        Assert.Equal(newLaborCost, edited["plannedCost"].D());
        Assert.Equal(oldLaborCost, edited["studyCost"].D());
        Assert.DoesNotContain(reopened["resources"]!.AsArray(), r => r!["id"].S() == removed["id"].S());

        // تحليل الدراسة نفسه لم يتغير
        var rowsAfter = await api.Get($"/commercialcontracts/{s.Id}/cost-analysis");
        Assert.Equal(rowsBefore.Data!.ToJsonString(), rowsAfter.Data!.ToJsonString());

        // السجل: تعديل الكمية والسعر (بقيمة الدراسة) والحذف والإضافة، بالسبب
        var history = (await api.Get($"/commercialcontracts/{s.Id}/execution-plan/history")).Data!.AsArray();
        Assert.Contains(history, h => h!["action"].S() == "modified" && h["field"].S() == "quantity" && h["studyValue"] != null);
        Assert.Contains(history, h => h!["action"].S() == "modified" && h["field"].S() == "unitCost");
        Assert.Contains(history, h => h!["action"].S() == "removed");
        Assert.Contains(history, h => h!["action"].S() == "added" && h["itemName"].S() == "عزل");
        Assert.All(history, h => Assert.Equal("إعادة تقدير العمالة", h!["reason"].S()));
    }

    [Fact]
    public async Task Approval_freezes_study_and_baseline_approves_materials_and_locks_until_revision()
    {
        var s = await PricedStudyAsync();
        var api = s.Api;
        Ok(await api.Post($"/commercialcontracts/{s.Id}/execution-plan/generate", new { }), "generate");
        // الاحتياجات تُدار من الخطة: لا حفظ ولا تقديم مباشر
        Assert.Equal(409, (await api.Post($"/commercialcontracts/{s.Id}/material-requirements/submit", new { })).Status);

        var submitted = await api.Post($"/commercialcontracts/{s.Id}/execution-plan/submit", new { });
        Ok(submitted, "submit");
        Assert.Equal("submitted", submitted.Data!["status"].S());
        Assert.False(submitted.Data["editable"]!.GetValue<bool>());
        Assert.Equal(409, (await api.Post($"/commercialcontracts/{s.Id}/execution-plan/generate", new { })).Status);
        Assert.Equal(409, (await api.Put($"/commercialcontracts/{s.Id}/execution-plan/resources", new { items = Array.Empty<object>() })).Status);
        var phases = (await api.Get($"/commercialcontracts/{s.Id}/project")).Data!["phases"]!.DeepClone();
        Assert.Equal(409, (await api.Put($"/commercialcontracts/{s.Id}/project/phases", new { phases })).Status);

        var noReason = await api.Post($"/commercialcontracts/{s.Id}/execution-plan/return", new { });
        Assert.True(noReason.Status == 400, noReason.Body?.ToJsonString());
        var returned = await api.Post($"/commercialcontracts/{s.Id}/execution-plan/return", new { reason = "راجع التواريخ" });
        Ok(returned, "return");
        Assert.Equal("draft", returned.Data!["status"].S());
        Assert.Equal("راجع التواريخ", returned.Data["returnNote"].S());

        Ok(await api.Post($"/commercialcontracts/{s.Id}/execution-plan/submit", new { }), "resubmit");
        var approved = await api.Post($"/commercialcontracts/{s.Id}/execution-plan/approve", new { });
        Ok(approved, "approve");
        var plan = approved.Data!;
        Assert.Equal("approved", plan["status"].S());
        Assert.Equal("plan_approved", plan["feasibilityStatus"].S());
        Assert.Equal("approved", plan["materialPlanStatus"].S());
        var baselines = plan["baselines"]!.AsArray();
        Assert.Contains(baselines, b => b!["kind"].S() == "feasibility" && b["revision"]!.GetValue<int>() == 1 && b["totalCost"].D() == 9375m);
        Assert.Contains(baselines, b => b!["kind"].S() == "plan_baseline" && b["revision"]!.GetValue<int>() == 1 && b["totalCost"].D() == 9175m);

        // الدراسة والتحليل مجمّدان، والدراسة لا تزال خارج العقود
        var study = (await api.Get($"/commercialcontracts/{s.Id}")).Data!;
        Assert.Equal("plan_approved", study["feasibilityStatus"].S());
        Assert.Equal(400, (await api.Put($"/commercialcontracts/{s.Id}", new
        {
            title = "دراسة", contractType = "construction_boq", workflow = "simple", requiresGuarantee = false, partyName = "عميل", partyId = s.Party,
            startDate = Today, endDate = Today.AddDays(29), vatRate = 15, items = s.Items, milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
        })).Status);
        Assert.Equal(409, (await api.Post($"/commercialcontracts/{s.Id}/cost-analysis/apply", new { })).Status);
        Assert.False((await api.Get($"/commercialcontracts/{s.Id}/cost-analysis")).Data!["editable"]!.GetValue<bool>());
        Assert.Contains("plan_approved", (await api.Get("/feasibility-studies")).Data!.AsArray().Select(r => r!["feasibilityStatus"].S()));

        // المراجعة: مسودة برقم 2، والدراسة تعود «دراسة» والمواد مسودة؛ والنسخ السابقة باقية
        var revised = await api.Post($"/commercialcontracts/{s.Id}/execution-plan/revise", new { });
        Ok(revised, "revise");
        Assert.Equal("draft", revised.Data!["status"].S());
        Assert.Equal(2, revised.Data["revision"]!.GetValue<int>());
        Assert.Equal("study", revised.Data["feasibilityStatus"].S());
        Assert.Equal("draft", revised.Data["materialPlanStatus"].S());
        Assert.Equal(2, revised.Data["baselines"]!.AsArray().Count);

        Ok(await api.Post($"/commercialcontracts/{s.Id}/execution-plan/submit", new { }), "submit rev 2");
        var again = await api.Post($"/commercialcontracts/{s.Id}/execution-plan/approve", new { });
        Ok(again, "approve rev 2");
        var all = again.Data!["baselines"]!.AsArray();
        Assert.Equal(2, all.Count(b => b!["kind"].S() == "plan_baseline"));
        Assert.Equal(2, all.Count(b => b!["kind"].S() == "feasibility"));
    }

    [Fact]
    public async Task Plan_materials_are_edited_in_the_plan_without_touching_the_study_audited_and_kept_on_regeneration()
    {
        var s = await PricedStudyAsync();
        var api = s.Api;
        var plan = (await api.Post($"/commercialcontracts/{s.Id}/execution-plan/generate", new { })).Data!;
        var studyCost = plan["budget"]!["studyCost"].D();
        var analysisBefore = (await api.Get($"/commercialcontracts/{s.Id}/cost-analysis")).Data!.ToJsonString();
        var sand = plan["materials"]!.AsArray().Single()!;
        Assert.Equal(27.5m, sand["studyQuantity"].D());
        Assert.Equal(50m, sand["studyUnitCost"].D());

        // عرض مورد أرخص وكمية مصحّحة للرمل، ومادة يدوية على بند الحفر
        var items = new JsonArray
        {
            new JsonObject { ["id"] = sand["id"]!.DeepClone(), ["resourceId"] = s.Sand.ToString(), ["phaseId"] = sand["phaseId"]!.DeepClone(), ["quantity"] = 30, ["plannedUnitCost"] = 45 },
            new JsonObject { ["resourceId"] = s.Sand.ToString(), ["boqItemId"] = s.Dig.ToString(), ["quantity"] = 4, ["plannedUnitCost"] = 50, ["notes"] = "ردم" },
        };
        var saved = await api.Put($"/commercialcontracts/{s.Id}/execution-plan/materials", new { items, reason = "عرض المورد" });
        Ok(saved, "save materials");
        var p = saved.Data!;
        Assert.Equal(1350m + 200m, p["budget"]!["materialsCost"].D());
        Assert.Equal(9175m - 1375m + 1550m, p["budget"]!["plannedCost"].D());
        Assert.Equal(studyCost, p["budget"]!["studyCost"].D()); // الدراسة لا تتغير
        var edited = p["materials"]!.AsArray().Single(m => m!["id"].S() == sand["id"].S())!;
        Assert.Equal("modified", edited["source"].S());
        Assert.Equal(1350m, edited["plannedCost"].D());
        Assert.Equal(1375m, edited["studyCost"].D());
        Assert.Contains(p["materials"]!.AsArray(), m => m!["source"].S() == "manual" && m["boqItemId"].G() == s.Dig && m["notes"].S() == "ردم");
        Assert.Equal(analysisBefore, (await api.Get($"/commercialcontracts/{s.Id}/cost-analysis")).Data!.ToJsonString());

        // السجل: تعديل الكمية والسعر بقيمة الدراسة، والإضافة، بنوع «مادة» وبالسبب
        var history = (await api.Get($"/commercialcontracts/{s.Id}/execution-plan/history")).Data!.AsArray();
        Assert.Contains(history, h => h!["itemKind"].S() == "material" && h["field"].S() == "quantity" && h["studyValue"].S() == "27.5" && h["newValue"].S() == "30");
        Assert.Contains(history, h => h!["itemKind"].S() == "material" && h["field"].S() == "unitCost" && h["newValue"].S() == "45");
        Assert.Contains(history, h => h!["itemKind"].S() == "material" && h["action"].S() == "added");
        Assert.All(history, h => Assert.Equal("عرض المورد", h!["reason"].S()));

        // مادة مكررة للبند نفسه مرفوضة
        items.Add(new JsonObject { ["resourceId"] = s.Sand.ToString(), ["boqItemId"] = s.Dig.ToString(), ["quantity"] = 1, ["plannedUnitCost"] = 50 });
        Assert.Equal(400, (await api.Put($"/commercialcontracts/{s.Id}/execution-plan/materials", new { items })).Status);

        // إعادة التوليد تُبقي المعدّل واليدوي ولا تكرر الرمل المولَّد
        var again = (await api.Post($"/commercialcontracts/{s.Id}/execution-plan/generate", new { })).Data!;
        var mats = again["materials"]!.AsArray();
        Assert.Equal(2, mats.Count);
        Assert.Equal(30m, mats.Single(m => m!["source"].S() == "modified")!["quantity"].D());
        Assert.Equal(1550m, again["budget"]!["materialsCost"].D());

        // بعد التقديم تُقفل
        Ok(await api.Post($"/commercialcontracts/{s.Id}/execution-plan/submit", new { }), "submit");
        Assert.Equal(409, (await api.Put($"/commercialcontracts/{s.Id}/execution-plan/materials", new { items = Array.Empty<object>() })).Status);
    }

    [Fact]
    public async Task Repricing_the_study_after_generation_warns_that_the_plan_is_stale_until_regenerated()
    {
        var s = await PricedStudyAsync();
        var api = s.Api;
        var plan = (await api.Post($"/commercialcontracts/{s.Id}/execution-plan/generate", new { })).Data!;
        Assert.DoesNotContain("study_changed", WarningCodes(plan));
        var planCost = plan["budget"]!["plannedCost"].D();

        // تعديل سعر العامل في تحليل الدراسة بعد التوليد
        var analysis = (await api.Get($"/commercialcontracts/{s.Id}/cost-analysis")).Data!;
        var row = analysis["lines"]!.AsArray().Single(l => l!["contractItemId"].G() == s.Dig)!["resources"]!.AsArray()
            .Single(r => r!["resourceId"].G() == s.Labor)!["id"].G();
        Ok(await api.Put($"/commercialcontracts/{s.Id}/cost-analysis/{row}", new { unitCost = 40, reason = "عرض مقاول" }), "reprice");

        // الخطة لا تتغير تلقائياً، وتنبّه بتكلفة الدراسة وقت التجهيز
        var stale = await PlanAsync(api, s.Id);
        Assert.Equal(planCost, stale["budget"]!["plannedCost"].D());
        Assert.NotEqual(plan["budget"]!["studyCost"].D(), stale["budget"]!["studyCost"].D());
        var warning = stale["warnings"]!.AsArray().Single(w => w!["code"].S() == "study_changed")!;
        Assert.Equal("9375", warning["detail"].S());

        // إعادة التوليد تأخذ السعر الجديد وتزيل التنبيه
        var regenerated = (await api.Post($"/commercialcontracts/{s.Id}/execution-plan/generate", new { })).Data!;
        Assert.DoesNotContain("study_changed", WarningCodes(regenerated));
        Assert.Equal(planCost + 1000m, regenerated["budget"]!["plannedCost"].D()); // 100 ساعة × (40 − 30)
    }
}
