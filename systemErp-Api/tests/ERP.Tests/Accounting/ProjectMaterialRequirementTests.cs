using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// احتياجات مواد المشروع من مكتبة المقاولات: الاحتياج يُعرَّف بمادة المكتبة والصنف المخزني اختياري من ربطها. الاشتقاق يشمل
/// المادة غير المربوطة ويشخّص كل سطر في جدول الكميات (اشتُق، أو بتنبيه، أو تُخطي بسببه)، والإضافة اليدوية من مواد تحليل البند
/// بكمية مقترحة (أو المكتبة عامة لسطر غير مرتبط)، والصنف المباشر مرفوض.
/// المثال: صب خرسانة 100 م3 = أسمنت 7 لكل م3 + رمل 0.5 بهدر 10% + فرعي «شدة» برمل 0.2 + عامل؛ والرمل غير مربوط بصنف.
/// </summary>
[Collection("api")]
public class ProjectMaterialRequirementTests : TestBase
{
    public ProjectMaterialRequirementTests(ErpFactory f) : base(f) { }

    private static readonly DateTime Day = new(2026, 11, 1);

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");

    private sealed record Setup(string ContractId, Guid Cement, Guid Sand, Guid Labor, Guid CementProduct,
        Guid Concrete, Guid Manual, Guid ManualSub, Guid LaborOnly, Guid Group, Guid GroupSub, Guid GroupSubNoSource);

    private static async Task<Guid> PostIdAsync(Client api, string url, object body, string step)
    {
        var r = await api.Post(url, body);
        Ok(r, step);
        return r.Data!["id"].G();
    }

    /// <summary>
    /// المكتبة: أسمنت مربوط بصنف (PCS بتكلفة 40)، ورمل يدوي بلا صنف (M3 بتكلفة 50)، وعامل. البند «صب خرسانة» بموادّه وفرعي
    /// «شدة»، والبند «عمالة» بلا مواد. جدول الكميات: صب 100، بند يدوي وفرعيه، عمالة 10، ومجموعة صب 50 بفرعيين (بمصدر وبلا مصدر).
    /// </summary>
    private static async Task<Setup> SetupAsync(Client api)
    {
        var cementProduct = await SeedProductAsync(api, "CEM", stock: 1000, cost: 40);
        Ok(await api.Post("/unitsofmeasure", new { code = "M3", nameAr = "متر مكعب", unitType = "volume", isBaseUnit = true, conversionFactor = 1 }), "unit M3");
        var cement = await PostIdAsync(api, "/construction-resources", new { nameAr = "أسمنت", resourceType = "material", unit = "PCS", costSource = "product", productId = cementProduct }, "cement");
        var sand = await PostIdAsync(api, "/construction-resources", new { nameAr = "رمل", resourceType = "material", unit = "M3", costSource = "manual", standardCost = 50 }, "sand");
        var labor = await PostIdAsync(api, "/construction-resources", new { nameAr = "عامل", resourceType = "labor", unit = "PCS", costSource = "manual", standardCost = 20 }, "labor");

        var work = await api.Post("/work-items", new
        {
            nameAr = "صب خرسانة", unit = "M3", pricingMode = "manual", sellPrice = 400,
            components = new object[]
            {
                new { resourceId = cement, quantityMode = "per_unit", consumptionFactor = 7, wastePercent = 0 },
                new { resourceId = sand, quantityMode = "per_unit", consumptionFactor = 0.5, wastePercent = 10 },
                new { resourceId = labor, quantityMode = "per_unit", consumptionFactor = 1, wastePercent = 0 },
            },
            subItems = new object[]
            {
                new
                {
                    description = "شدة خشبية", unit = "M3", quantityMode = "per_unit", quantityFactor = 1,
                    components = new object[] { new { resourceId = sand, quantityMode = "per_unit", consumptionFactor = 0.2, wastePercent = 0 } },
                },
            },
        });
        Ok(work, "work item");
        var workId = work.Data!["id"].G();
        var librarySub = work.Data["subItems"]!.AsArray().Single()!["id"].G();
        var laborWork = await PostIdAsync(api, "/work-items", new
        {
            nameAr = "أعمال عمالة", unit = "M3", pricingMode = "manual", sellPrice = 50,
            components = new object[] { new { resourceId = labor, quantityMode = "per_unit", consumptionFactor = 2, wastePercent = 0 } },
        }, "labor work");

        var (party, _) = await SeedCustomerAsync(api);
        var created = await api.Post("/commercialcontracts", new
        {
            title = "مشروع خرسانة", contractType = "construction_boq", workflow = "simple", requiresGuarantee = false, partyName = "عميل", partyId = party,
            startDate = Day, endDate = Day.AddMonths(6), vatRate = 15,
            items = new object[]
            {
                new { description = "صب خرسانة", unit = "م3", quantity = 100, unitPrice = 400, vatRate = 15, lineKind = "measured", workItemId = workId, sortOrder = 1 },
                new { description = "بند يدوي", unit = "م2", quantity = 20, unitPrice = 100, vatRate = 15, lineKind = "measured", sortOrder = 2 },
                new { description = "فرعي يدوي", unit = "م2", quantity = 20, unitPrice = 100, vatRate = 15, lineKind = "measured", isSubItem = true, sortOrder = 3 },
                new { description = "عمالة", unit = "م3", quantity = 10, unitPrice = 50, vatRate = 15, lineKind = "measured", workItemId = laborWork, sortOrder = 4 },
                new { description = "صب بفروع", unit = "م3", quantity = 50, unitPrice = 400, vatRate = 15, lineKind = "measured", workItemId = workId, sortOrder = 5 },
                new { description = "شدة الصب", unit = "م3", quantity = 50, unitPrice = 100, vatRate = 15, lineKind = "measured", isSubItem = true, workSubItemId = librarySub, sortOrder = 6 },
                new { description = "فرعي بلا مصدر", unit = "م3", quantity = 5, unitPrice = 100, vatRate = 15, lineKind = "measured", isSubItem = true, sortOrder = 7 },
            },
            milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
        });
        Ok(created, "contract");
        // إعداد الاحتياجات بصلاحية الاستلام: في مراحل التنفيذ
        Ok(await api.Post($"/commercialcontracts/{created.Data!["id"].S()}/move", new { toStatus = "active" }), "activate");
        var items = created.Data!["items"]!.AsArray();
        Guid Line(string d) => items.First(i => i!["description"].S() == d)!["id"].G();
        return new Setup(created.Data["id"].S(), cement, sand, labor, cementProduct, Line("صب خرسانة"), Line("بند يدوي"), Line("فرعي يدوي"),
            Line("عمالة"), Line("صب بفروع"), Line("شدة الصب"), Line("فرعي بلا مصدر"));
    }

    private static JsonNode Diag(JsonNode derived, Guid line) => derived["diagnostics"]!.AsArray().Single(d => d!["lineId"].G() == line)!;

    private static JsonNode Req(JsonArray lines, Guid boq, Guid? sub, Guid resource) => lines.Single(l =>
        l!["boqItemId"]?.GetValue<string?>() == boq.ToString() && l["subItemId"]?.GetValue<string?>() == sub?.ToString() && l["resourceId"].G() == resource)!;

    // ---------- الاشتقاق والتشخيص ----------

    [Fact]
    public async Task Derive_includes_unlinked_materials_and_diagnoses_every_line()
    {
        var api = await NewTenantAsync();
        var s = await SetupAsync(api);
        var derived = await api.Post($"/commercialcontracts/{s.ContractId}/material-requirements/derive", new { });
        Ok(derived, "derive");
        var lines = derived.Data!["lines"]!.AsArray();

        // صب 100: أسمنت 700 مربوط بالمخزون، ورمل 100 × 0.5 × 1.1 + 100 × 0.2 = 75 غير مربوط (يبقى احتياجاً صحيحاً)
        var cement = Req(lines, s.Concrete, null, s.Cement);
        Assert.Equal(700m, cement["baselineQuantity"].D());
        Assert.True(cement["isStockLinked"]!.GetValue<bool>());
        Assert.Equal(700m, cement["productQuantity"].D());
        Assert.Equal(28000m, cement["estimatedCost"].D());
        var sand = Req(lines, s.Concrete, null, s.Sand);
        Assert.Equal(75m, sand["baselineQuantity"].D());
        Assert.Equal("M3", sand["unit"].S());
        Assert.False(sand["isStockLinked"]!.GetValue<bool>());
        Assert.Null(sand["productId"]?.GetValue<string?>());
        Assert.Null(sand["productQuantity"]?.GetValue<decimal?>());
        Assert.Equal(3750m, sand["estimatedCost"].D());
        // العامل ليس مادة
        Assert.DoesNotContain(lines, l => l!["resourceId"].G() == s.Labor);

        var concrete = Diag(derived.Data, s.Concrete);
        Assert.Equal("warning", concrete["status"].S());
        Assert.Equal(2, concrete["materialCount"]!.GetValue<int>());
        Assert.Equal(1, concrete["otherResourceCount"]!.GetValue<int>());
        var issue = concrete["issues"]!.AsArray().Single()!;
        Assert.Equal("not_stocked", issue["code"].S());
        Assert.Equal(s.Sand, issue["resourceId"].G());

        // كل حالة تخطٍّ ببندها وسببها
        Assert.Equal("not_linked", Diag(derived.Data, s.Manual)["reason"].S());
        Assert.Equal("skipped", Diag(derived.Data, s.Manual)["status"].S());
        Assert.Equal("parent_not_linked", Diag(derived.Data, s.ManualSub)["reason"].S());
        var laborOnly = Diag(derived.Data, s.LaborOnly);
        Assert.Equal("no_materials", laborOnly["reason"].S());
        Assert.Equal(1, laborOnly["otherResourceCount"]!.GetValue<int>());
        Assert.Equal("sub_no_source", Diag(derived.Data, s.GroupSubNoSource)["reason"].S());

        // المجموعة: موادها المباشرة عليها (أسمنت 350، رمل 27.5) وفرعيها بمصدره يأخذ مادة بنده الفرعي (رمل 10)
        Assert.Equal(350m, Req(lines, s.Group, null, s.Cement)["baselineQuantity"].D());
        Assert.Equal(27.5m, Req(lines, s.Group, null, s.Sand)["baselineQuantity"].D());
        Assert.Equal(10m, Req(lines, s.Group, s.GroupSub, s.Sand)["baselineQuantity"].D());
        Assert.Equal("warning", Diag(derived.Data, s.GroupSub)["status"].S());
    }

    // ---------- الإضافة اليدوية ----------

    [Fact]
    public async Task Manual_add_offers_item_analysis_with_suggested_quantity_and_library_for_unlinked_lines()
    {
        var api = await NewTenantAsync();
        var s = await SetupAsync(api);

        var item = await api.Get($"/commercialcontracts/{s.ContractId}/material-requirements/candidates?lineId={s.Concrete}");
        Ok(item, "item candidates");
        Assert.Equal("item", item.Data!["scope"].S());
        var candidates = item.Data["candidates"]!.AsArray();
        Assert.Equal(2, candidates.Count);
        var sand = candidates.Single(c => c!["resourceId"].G() == s.Sand)!;
        Assert.Equal(75m, sand["suggestedQuantity"].D());
        Assert.False(sand["isStockLinked"]!.GetValue<bool>());
        var origins = sand["origins"]!.AsArray();
        Assert.Equal(2, origins.Count);
        Assert.Contains(origins, o => o!["consumptionFactor"].D() == 0.5m && o["wastePercent"].D() == 10m && o["quantity"].D() == 55m);
        Assert.Contains(origins, o => o!["librarySubItem"].S() == "شدة خشبية" && o["quantity"].D() == 20m);

        // سطر غير مرتبط بالمكتبة: مواد المكتبة عامة بلا كمية مقترحة، ولا موارد غير مادية
        var library = await api.Get($"/commercialcontracts/{s.ContractId}/material-requirements/candidates?lineId={s.Manual}");
        Ok(library, "library candidates");
        Assert.Equal("library", library.Data!["scope"].S());
        Assert.Equal("not_linked", library.Data["reason"].S());
        var all = library.Data["candidates"]!.AsArray();
        Assert.Contains(all, c => c!["resourceId"].G() == s.Cement && c["suggestedQuantity"]?.GetValue<decimal?>() == null);
        Assert.Contains(all, c => c!["resourceId"].G() == s.Sand);
        Assert.DoesNotContain(all, c => c!["resourceId"].G() == s.Labor);

        // الحفظ: مادة من تحليل البند بكمية معدّلة، ومادة عامة لسطر يدوي
        var saved = await api.Put($"/commercialcontracts/{s.ContractId}/material-requirements", new
        {
            lines = new object[]
            {
                new { boqItemId = s.Concrete, resourceId = s.Sand, source = "manual", baselineQuantity = 80 },
                new { boqItemId = s.Manual, resourceId = s.Cement, source = "manual", baselineQuantity = 12 },
            },
        });
        Ok(saved, "save");
        var lines = saved.Data!["lines"]!.AsArray();
        Assert.Equal(80m, Req(lines, s.Concrete, null, s.Sand)["baselineQuantity"].D());
        var cement = Req(lines, s.Manual, null, s.Cement);
        Assert.Equal("PCS", cement["unit"].S());
        Assert.Equal(s.CementProduct, cement["productId"].G());
        Assert.Equal(1000m, cement["onHandQuantity"].D());
    }

    [Fact]
    public async Task Direct_stock_items_and_non_material_resources_are_rejected()
    {
        var api = await NewTenantAsync();
        var s = await SetupAsync(api);
        var url = $"/commercialcontracts/{s.ContractId}/material-requirements";

        // صنف مخزني مباشر بلا مادة مكتبة
        var direct = await api.Put(url, new { lines = new object[] { new { boqItemId = s.Concrete, productId = s.CementProduct, source = "manual", baselineQuantity = 5 } } });
        Assert.Equal(400, direct.Status);
        // مورد عمالة، ومادة غير موجودة، وتكرار المادة على البند نفسه
        var bad = await api.Put(url, new
        {
            lines = new object[]
            {
                new { boqItemId = s.Concrete, resourceId = s.Labor, source = "manual", baselineQuantity = 5 },
                new { boqItemId = s.Concrete, resourceId = Guid.NewGuid(), source = "manual", baselineQuantity = 5 },
                new { boqItemId = s.Concrete, resourceId = s.Sand, source = "manual", baselineQuantity = 5 },
                new { boqItemId = s.Concrete, resourceId = s.Sand, source = "manual", baselineQuantity = 6 },
            },
        });
        Assert.Equal(400, bad.Status);
        var errors = bad.Body!["errors"]!.AsArray().Select(e => e.S()).ToList();
        Assert.Contains(errors, e => e.Contains("السطر 1") && e.Contains("مكتبة"));
        Assert.Contains(errors, e => e.Contains("السطر 2") && e.Contains("مكتبة"));
        Assert.Contains(errors, e => e.Contains("السطر 4") && e.Contains("مكررة"));
    }

    // ---------- الربط بالمخزون لاحقاً ----------

    [Fact]
    public async Task Linking_a_library_material_to_stock_later_shows_on_saved_requirements()
    {
        var api = await NewTenantAsync();
        var s = await SetupAsync(api);
        var derived = (await api.Post($"/commercialcontracts/{s.ContractId}/material-requirements/derive", new { })).Data!["lines"]!.AsArray();
        var saved = await api.Put($"/commercialcontracts/{s.ContractId}/material-requirements", new
        {
            lines = derived.Select(l => new
            {
                boqItemId = l!["boqItemId"]?.GetValue<string?>(), subItemId = l["subItemId"]?.GetValue<string?>(), resourceId = l["resourceId"].G(),
                source = l["source"].S(), baselineQuantity = l["baselineQuantity"].D(),
            }),
        });
        Ok(saved, "save derived");
        Assert.False(Req(saved.Data!["lines"]!.AsArray(), s.Concrete, null, s.Sand)["isStockLinked"]!.GetValue<bool>());

        // صنف الرمل بالمتر المكعب، ثم ربط المادة به من مكانها
        var sandProduct = await api.Post("/products", new { sku = "SAND", nameAr = "رمل ناعم", category = "C1", unit = "M3", sellingPrice = 80, vatRate = 15 });
        Assert.Equal(201, sandProduct.Status);
        var link = await api.Put($"/construction-resources/{s.Sand}", new
        {
            nameAr = "رمل", resourceType = "material", unit = "M3", costSource = "manual", standardCost = 50, productId = sandProduct.Data!["id"].G(),
        });
        Ok(link, "link");

        var plan = await api.Get($"/commercialcontracts/{s.ContractId}/material-requirements");
        var sand = Req(plan.Data!["lines"]!.AsArray(), s.Concrete, null, s.Sand);
        Assert.True(sand["isStockLinked"]!.GetValue<bool>());
        Assert.Equal(75m, sand["productQuantity"].D());
        Assert.Equal(0m, sand["onHandQuantity"].D());

        // المادة عليها احتياج: لا تُحذف
        Assert.Equal(409, (await api.Delete($"/construction-resources/{s.Sand}")).Status);
    }
}
