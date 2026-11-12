using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// مكتبة تكاليف المقاولات: الكتالوج نفسه (موارد المكتبة) بتكلفة لها سجل بتواريخ سريان لا يُعدَّل. المادة يدوية أو من الصنف،
/// والعمالة والمعدات من تفصيل التكلفة (الأساس والسعر وتكاليف الساعة) بالمعادلة نفسها لمورد المشروع، والسعر المجدول والتصحيح
/// الماضي من السجل، والتكلفة في تاريخ تقرأ السجل. وإدخال التكلفة يتطلب صلاحية «مكتبة تكاليف المقاولات»، والمنشآت معزولة.
/// </summary>
[Collection("api")]
public class ConstructionCostLibraryTests : TestBase
{
    public ConstructionCostLibraryTests(ErpFactory f) : base(f) { }

    private static readonly DateTime Today = DateTime.UtcNow.Date;
    private static string Day(DateTime d) => d.ToString("yyyy-MM-dd");

    /// <summary>منشأة بإضافة المقاولات ووحدة القطعة (PCS) كما يعرّفها <see cref="TestBase.SeedProductAsync"/>.</summary>
    private async Task<Client> TenantAsync()
    {
        var api = await NewConstructionTenantAsync();
        await api.Post("/unitsofmeasure", new { code = "PCS", nameAr = "قطعة", isBaseUnit = true, conversionFactor = 1 });
        return api;
    }

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");

    private static void Rejected(Res r, string step, params int[] statuses)
        => Assert.True((statuses.Length == 0 ? new[] { 400 } : statuses).Contains(r.Status), $"{step}: {r.Status} {r.Body?.ToJsonString()}");

    private static async Task<JsonNode> CreateAsync(Client api, object body, string step)
    {
        var r = await api.Post("/construction-resources", body);
        Ok(r, step);
        return r.Data!;
    }

    private static async Task<JsonNode> HistoryAsync(Client api, Guid id)
    {
        var r = await api.Get($"/construction-resources/{id}/costs");
        Ok(r, "history");
        return r.Data!;
    }

    private static async Task<JsonArray> CostsAtAsync(Client api, DateTime date, params Guid[] ids)
    {
        var query = string.Join("&", ids.Select(i => $"ids={i}"));
        var r = await api.Get($"/construction-resources/costs-at?date={Day(date)}&{query}");
        Ok(r, "costs-at");
        return r.Data!.AsArray();
    }

    /// <summary>جسم التعديل من المورد كما قُرئ، مع تغيير ما يلزم.</summary>
    private static Dictionary<string, object?> Body(JsonNode r, Action<Dictionary<string, object?>>? change = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["code"] = r["code"].S(), ["nameAr"] = r["nameAr"].S(), ["nameEn"] = r["nameEn"].S(), ["resourceType"] = r["resourceType"].S(),
            ["unit"] = r["unit"].S(), ["standardCost"] = r["standardCost"].D(), ["costSource"] = r["costSource"].S(),
            ["hoursPerUnit"] = r["hoursPerUnit"].D(), ["category"] = r["category"]?.GetValue<string>(),
            ["equipmentOwnership"] = r["equipmentOwnership"]?.GetValue<string>(), ["isActive"] = true,
        };
        change?.Invoke(body);
        return body;
    }

    private static async Task TimeUnitsAsync(Client api)
    {
        Ok(await api.Post("/unitsofmeasure", new { code = "HR", nameAr = "ساعة", unitType = "time", isBaseUnit = true, conversionFactor = 1 }), "unit HR");
        Ok(await api.Post("/unitsofmeasure", new { code = "DAY", nameAr = "يوم", unitType = "time", isBaseUnit = false, conversionFactor = 8 }), "unit DAY");
    }

    // ---------- المادة والسجل ----------

    [Fact]
    public async Task Material_cost_changes_append_to_history_and_never_rewrite_it()
    {
        var api = await TenantAsync();
        var sand = await CreateAsync(api, new { nameAr = "رمل", resourceType = "material", unit = "PCS", costSource = "manual", standardCost = 50, category = "مواد ردم" }, "sand");
        var id = sand["id"].G();
        Assert.Equal("مواد ردم", sand["category"].S());
        Assert.StartsWith(Day(Today), sand["costEffectiveFrom"].S());

        var history = await HistoryAsync(api, id);
        var first = Assert.Single(history["entries"]!.AsArray())!;
        Assert.Equal(50m, first["unitCost"].D());
        Assert.Equal("created", first["reason"].S());
        Assert.Equal("current", first["status"].S());
        Assert.False(string.IsNullOrEmpty(first["currency"].S()));
        var firstId = first["id"].G();

        // تغيير التكلفة سطر جديد، والسطر الأول كما هو
        Ok(await api.Put($"/construction-resources/{id}", Body(sand, b => { b["standardCost"] = 55m; b["costReason"] = "عرض مورد جديد"; })), "raise");
        history = await HistoryAsync(api, id);
        var entries = history["entries"]!.AsArray();
        Assert.Equal(2, entries.Count);
        Assert.Equal(55m, history["current"]!["unitCost"].D());
        Assert.Equal("عرض مورد جديد", history["current"]!["reason"].S());
        var old = entries.Single(e => e!["id"].G() == firstId)!;
        Assert.Equal(50m, old["unitCost"].D());
        Assert.Equal("past", old["status"].S());

        // تعديل بلا تغيير تكلفة لا يضيف سطراً
        var current = (await api.Get($"/construction-resources/{id}")).Data!;
        Ok(await api.Put($"/construction-resources/{id}", Body(current, b => b["nameAr"] = "رمل ناعم")), "rename");
        Assert.Equal(2, (await HistoryAsync(api, id))["entries"]!.AsArray().Count);

        // نموذج المورد: السريان لا يتجاوز اليوم، ولا يسبق آخر سطر ساري
        Rejected(await api.Put($"/construction-resources/{id}", Body(current, b => { b["standardCost"] = 60m; b["effectiveFrom"] = Today.AddDays(5); })), "future in form");
        Rejected(await api.Put($"/construction-resources/{id}", Body(current, b => { b["standardCost"] = 60m; b["effectiveFrom"] = Today.AddDays(-5); })), "before latest");

        // لا مسار لتعديل سطر السجل أو حذفه
        Assert.True((await api.Delete($"/construction-resources/{id}/costs/{firstId}")).Status is 404 or 405);
        Assert.True((await api.Put($"/construction-resources/{id}/costs/{firstId}", new { unitCost = 1 })).Status is 404 or 405);
        Assert.Equal(2, (await HistoryAsync(api, id))["entries"]!.AsArray().Count);
    }

    [Fact]
    public async Task Linking_a_material_to_an_item_records_the_new_source_in_history()
    {
        var api = await TenantAsync();
        var product = await SeedProductAsync(api, "CEM", stock: 100, cost: 40);
        var cement = await CreateAsync(api, new { nameAr = "أسمنت", resourceType = "material", unit = "PCS", costSource = "manual", standardCost = 35 }, "cement");
        var link = await api.Post("/construction-resources/link-products", new { links = new[] { new { resourceId = cement["id"].G(), productId = product } } });
        Ok(link, "link");
        Assert.Equal(1, link.Data!["linked"]!.GetValue<int>());

        var history = await HistoryAsync(api, cement["id"].G());
        Assert.Equal(2, history["entries"]!.AsArray().Count);
        Assert.Equal("product", history["current"]!["source"].S());
        Assert.Equal("product_link", history["current"]!["reason"].S());
        Assert.Equal(40m, history["current"]!["unitCost"].D());
    }

    // ---------- العمالة والمعدات ----------

    [Fact]
    public async Task Labor_cost_from_breakdown_by_hour_or_day_with_overtime()
    {
        var api = await TenantAsync();
        await TimeUnitsAsync(api);
        var breakdown = new { rateBasis = "day", rate = 240, hoursPerDay = 8, daysPerMonth = 26, overtimeMultiplier = 1.5 };

        var hourly = await CreateAsync(api, new { nameAr = "مهندس موقع", resourceType = "labor", unit = "HR", costSource = "breakdown", hoursPerUnit = 1, category = "مهندس", breakdown }, "engineer/hour");
        Assert.Equal(30m, hourly["standardCost"].D());
        var row = (await HistoryAsync(api, hourly["id"].G()))["current"]!;
        Assert.Equal(30m, row["hourlyCost"].D());
        Assert.Equal(45m, row["overtimeHourlyCost"].D());
        Assert.Equal("day", row["breakdown"]!["rateBasis"].S());
        Assert.Equal(240m, row["breakdown"]!["rate"].D());

        var daily = await CreateAsync(api, new { nameAr = "عامل", resourceType = "labor", unit = "DAY", costSource = "breakdown", hoursPerUnit = 8, category = "عامل", breakdown }, "worker/day");
        Assert.Equal(240m, daily["standardCost"].D());

        var summary = (await api.Get("/construction-resources/cost-summary")).Data!;
        var laborCategories = summary["categories"]!["labor"]!.AsArray().Select(c => c.S()).ToList();
        Assert.Contains("مهندس", laborCategories);
        Assert.Contains("عامل", laborCategories);
        Assert.Equal(2, summary["types"]!.AsArray().Single(t => t!["resourceType"].S() == "labor")!["active"]!.GetValue<int>());

        // التفصيل للعمالة والمعدات فقط، ويحتاج ساعات وحدة موجبة وتكلفة ساعة موجبة
        Rejected(await api.Post("/construction-resources", new { nameAr = "رمل", resourceType = "material", unit = "PCS", costSource = "breakdown", breakdown }), "material breakdown");
        Rejected(await api.Post("/construction-resources", new { nameAr = "عامل", resourceType = "labor", unit = "HR", costSource = "breakdown", hoursPerUnit = 0, breakdown }), "zero hours");
        Rejected(await api.Post("/construction-resources", new { nameAr = "عامل", resourceType = "labor", unit = "HR", costSource = "breakdown", hoursPerUnit = 1, breakdown = new { rateBasis = "hour", rate = 0 } }), "zero cost");
        Rejected(await api.Post("/construction-resources", new { nameAr = "عامل", resourceType = "labor", unit = "HR", costSource = "breakdown", hoursPerUnit = 1 }), "missing breakdown");
    }

    [Fact]
    public async Task Owned_and_rented_equipment_costs_from_breakdown()
    {
        var api = await TenantAsync();
        await TimeUnitsAsync(api);
        var (supplier, _) = await SeedSupplierAsync(api, "مؤجر معدات");

        var owned = await CreateAsync(api, new
        {
            nameAr = "حفار", resourceType = "equipment", unit = "HR", costSource = "breakdown", hoursPerUnit = 1, category = "حفار",
            equipmentOwnership = "owned",
            breakdown = new { rateBasis = "hour", rate = 100, fuelPerHour = 40, maintenancePerHour = 10, depreciationPerHour = 25, otherPerHour = 5, overtimeMultiplier = 1 },
        }, "owned excavator");
        Assert.Equal(180m, owned["standardCost"].D());
        Assert.Equal("owned", owned["equipmentOwnership"].S());
        Assert.Equal(25m, (await HistoryAsync(api, owned["id"].G()))["current"]!["breakdown"]!["depreciationPerHour"].D());

        var rented = await CreateAsync(api, new
        {
            nameAr = "رافعة", resourceType = "equipment", unit = "HR", costSource = "breakdown", hoursPerUnit = 1, equipmentOwnership = "rented",
            supplierId = supplier, breakdown = new { rateBasis = "day", rate = 1600, hoursPerDay = 8, otherPerHour = 20, overtimeMultiplier = 1 },
        }, "rented crane");
        Assert.Equal(220m, rented["standardCost"].D());
        Assert.Equal(supplier, rented["supplierId"].G());

        // التفصيل للمعدة يحتاج الملكية، والمورد المرتبط موجود
        Rejected(await api.Post("/construction-resources", new
        {
            nameAr = "لودر", resourceType = "equipment", unit = "HR", costSource = "breakdown", hoursPerUnit = 1,
            breakdown = new { rateBasis = "hour", rate = 90 },
        }), "no ownership");
        Rejected(await api.Post("/construction-resources", new
        {
            nameAr = "لودر", resourceType = "equipment", unit = "HR", costSource = "manual", standardCost = 90, supplierId = Guid.NewGuid(),
        }), "unknown supplier");

        // مورد المشروع بالمعادلة نفسها: مملوكة 100 + 40 + 10 + 5 = 155
        var (party, _) = await SeedCustomerAsync(api);
        var contract = await api.Post("/commercialcontracts", new
        {
            title = "مشروع", contractType = "construction_boq", workflow = "simple", requiresGuarantee = false, partyName = "عميل", partyId = party,
            startDate = Today, endDate = Today.AddMonths(3), vatRate = 15,
            items = new object[] { new { description = "حفر", unit = "م3", quantity = 10, unitPrice = 100, vatRate = 15, lineKind = "measured", sortOrder = 1 } },
        });
        Ok(contract, "contract");
        var project = await api.Post($"/commercialcontracts/{contract.Data!["id"].S()}/project/resources", new
        {
            kind = "owned_equipment", name = "حفار 1", rateBasis = "hour", rate = 100, fuelPerHour = 40, maintenancePerHour = 10, extraCostPerHour = 5,
            hoursPerDay = 8, daysPerMonth = 26, overtimeMultiplier = 1.5, libraryResourceId = owned["id"].G(),
        });
        Ok(project, "project resource");
        Assert.Equal(155m, project.Data!.AsArray().Single()!["hourlyCost"].D());
        // المكتبة لا تُنشئ موارد مشروع: لم يُضف إلا ما أُدخل
        Assert.Single(project.Data!.AsArray());
    }

    // ---------- السعر المجدول والماضي والتكلفة في تاريخ ----------

    [Fact]
    public async Task Scheduled_and_backdated_prices_resolve_by_effective_date()
    {
        var api = await TenantAsync();
        var steel = await CreateAsync(api, new { nameAr = "حديد", resourceType = "material", unit = "PCS", costSource = "manual", standardCost = 100 }, "steel");
        var id = steel["id"].G();

        var scheduled = await api.Post($"/construction-resources/{id}/costs", new { effectiveFrom = Today.AddDays(30), unitCost = 120, reason = "زيادة الشهر القادم" });
        Ok(scheduled, "scheduled");
        Assert.Equal(100m, scheduled.Data!["current"]!["unitCost"].D());
        Assert.Contains(scheduled.Data["entries"]!.AsArray(), e => e!["status"].S() == "scheduled" && e["unitCost"].D() == 120m);
        Assert.Equal(100m, (await api.Get($"/construction-resources/{id}")).Data!["standardCost"].D());

        Ok(await api.Post($"/construction-resources/{id}/costs", new { effectiveFrom = Today.AddDays(-60), unitCost = 80, reason = "تصحيح" }), "backdated");

        Assert.Equal(100m, (await CostsAtAsync(api, Today, id)).Single()!["unitCost"].D());
        Assert.Equal(120m, (await CostsAtAsync(api, Today.AddDays(45), id)).Single()!["unitCost"].D());
        var past = (await CostsAtAsync(api, Today.AddDays(-30), id)).Single()!;
        Assert.Equal(80m, past["unitCost"].D());
        Assert.StartsWith(Day(Today.AddDays(-60)), past["effectiveFrom"].S());
        Assert.NotNull(past["costId"]);
        // قبل أول سطر: لا تكلفة
        var before = (await CostsAtAsync(api, Today.AddDays(-90), id)).Single()!;
        Assert.Equal(0m, before["unitCost"].D());
        Assert.Null(before["costId"]);

        Assert.Equal(1, (await api.Get("/construction-resources/cost-summary")).Data!["scheduled"]!.GetValue<int>());
        Assert.Equal(3, (await HistoryAsync(api, id))["entries"]!.AsArray().Count);

        // السعر المجدول للمصدر اليدوي والتفصيل وحدهما؛ والمحسوب من مصدره
        var product = await SeedProductAsync(api, "SAND", stock: 10, cost: 30);
        var linked = await CreateAsync(api, new { nameAr = "رمل", resourceType = "material", unit = "PCS", costSource = "product", productId = product }, "linked");
        Rejected(await api.Post($"/construction-resources/{linked["id"].G()}/costs", new { effectiveFrom = Today, unitCost = 1 }), "computed source");
        Rejected(await api.Post($"/construction-resources/{id}/costs", new { unitCost = 1 }), "no date");
        Rejected(await api.Post($"/construction-resources/{id}/costs", new { effectiveFrom = Today }), "no value");
    }

    [Fact]
    public async Task Work_item_cost_edit_is_recorded_in_history()
    {
        var api = await TenantAsync();
        var sand = await CreateAsync(api, new { nameAr = "رمل", resourceType = "material", unit = "PCS", costSource = "manual", standardCost = 50 }, "sand");
        var work = await api.Post("/work-items", new
        {
            nameAr = "ردم", unit = "PCS", pricingMode = "manual", sellPrice = 100,
            components = new object[] { new { resourceId = sand["id"].G(), quantityMode = "per_unit", consumptionFactor = 1, wastePercent = 0, unitCost = 60 } },
        });
        Ok(work, "work item");

        var history = await HistoryAsync(api, sand["id"].G());
        Assert.Equal(2, history["entries"]!.AsArray().Count);
        Assert.Equal(60m, history["current"]!["unitCost"].D());
        Assert.Equal("work_item", history["current"]!["reason"].S());
    }

    // ---------- العزل والصلاحيات ----------

    [Fact]
    public async Task Another_company_cannot_read_or_price_the_cost_library()
    {
        var a = await TenantAsync();
        var b = await TenantAsync();
        var sand = await CreateAsync(a, new { nameAr = "رمل", resourceType = "material", unit = "PCS", costSource = "manual", standardCost = 50 }, "sand");
        var id = sand["id"].G();

        Assert.Equal(404, (await b.Get($"/construction-resources/{id}/costs")).Status);
        Assert.Equal(404, (await b.Post($"/construction-resources/{id}/costs", new { effectiveFrom = Today, unitCost = 1 })).Status);
        Assert.Empty(await CostsAtAsync(b, Today, id));
        Assert.Equal(0, (await b.Get("/construction-resources/cost-summary")).Data!["types"]!.AsArray().Sum(t => t!["active"]!.GetValue<int>()));
        Assert.Single((await HistoryAsync(a, id))["entries"]!.AsArray());
    }

    [Fact]
    public async Task Entering_costs_requires_the_cost_library_edit_permission()
    {
        var owner = await TenantAsync();
        var role = (await owner.Post("/roles", new { nameAr = "محرر المكتبة" })).Data!["code"].S();
        Ok(await owner.Post("/permissions", new
        {
            roleId = role,
            permissions = new[]
            {
                new { screenId = "construction-library", screenNameAr = "", screenNameEn = "", actions = new[] { "view", "create", "edit" } },
                new { screenId = "construction-cost-library", screenNameAr = "", screenNameEn = "", actions = new[] { "view" } },
            },
        }), "permissions");
        var roleId = (await owner.Get("/roles")).Data!.AsArray().First(r => r!["code"].S() == role)!["id"].G();
        var email = $"c{Guid.NewGuid():N}@test.com";
        Ok(await owner.Post("/users", new { name = "محرر", email, phone = "1", password = "Passw0rd!", role = "sales_rep", customRoleId = roleId }), "user");
        var editor = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");

        // تعريف المورد بلا تكلفة مسموح، وإدخال التكلفة لا
        var bare = await CreateAsync(editor, new { nameAr = "أنابيب", resourceType = "material", unit = "PCS", costSource = "manual", standardCost = 0 }, "bare");
        Rejected(await editor.Post("/construction-resources", new { nameAr = "كابل", resourceType = "material", unit = "PCS", costSource = "manual", standardCost = 10 }), "create with cost", 403);
        Ok(await editor.Put($"/construction-resources/{bare["id"].G()}", Body(bare, b => b["nameAr"] = "أنابيب PVC")), "rename");
        Rejected(await editor.Put($"/construction-resources/{bare["id"].G()}", Body(bare, b => b["standardCost"] = 12m)), "set cost", 403);
        Rejected(await editor.Post($"/construction-resources/{bare["id"].G()}/costs", new { effectiveFrom = Today, unitCost = 12 }), "add price", 403);
        Ok(await editor.Get($"/construction-resources/{bare["id"].G()}/costs"), "view history");

        // تعديل التكلفة من محرر البنود بالصلاحية نفسها
        var priced = await CreateAsync(owner, new { nameAr = "رمل", resourceType = "material", unit = "PCS", costSource = "manual", standardCost = 50 }, "owner priced");
        Rejected(await editor.Post("/work-items", new
        {
            nameAr = "ردم", unit = "PCS", pricingMode = "manual", sellPrice = 100,
            components = new object[] { new { resourceId = priced["id"].G(), quantityMode = "per_unit", consumptionFactor = 1, wastePercent = 0, unitCost = 70 } },
        }), "work item cost", 403);
        Assert.Single((await HistoryAsync(owner, priced["id"].G()))["entries"]!.AsArray());

        // المالك (صلاحية كاملة) يدخل السعر
        Ok(await owner.Post($"/construction-resources/{bare["id"].G()}/costs", new { effectiveFrom = Today, unitCost = 12 }), "owner price");
    }

    /// <summary>شبكة الشاشة على مسار القراءة العام (construction-resources/load) بمرشحاتها: النوع، والفعّال بلا تكلفة، والفئة، والبحث.</summary>
    [Fact]
    public async Task Cost_library_grid_filters_work_on_the_generic_load_endpoint()
    {
        var api = await TenantAsync();
        await CreateAsync(api, new { nameAr = "رمل", resourceType = "material", unit = "PCS", costSource = "manual", standardCost = 50, category = "ردم" }, "priced");
        await CreateAsync(api, new { nameAr = "بحص", resourceType = "material", unit = "PCS", costSource = "manual", standardCost = 0, category = "ردم" }, "unpriced");
        await CreateAsync(api, new { nameAr = "جبس", resourceType = "material", unit = "PCS", costSource = "manual", standardCost = 0, isActive = false }, "inactive");
        await CreateAsync(api, new { nameAr = "عامل", resourceType = "labor", unit = "PCS", costSource = "manual", standardCost = 0 }, "labor");

        async Task<JsonNode> Load(string filter)
        {
            var r = await api.Get("/construction-resources/load?requireTotalCount=true&filter=" + Uri.EscapeDataString(filter));
            Ok(r, "load " + filter);
            return r.Body!;
        }

        var noCost = await Load("[[\"resourceType\",\"=\",\"material\"],\"and\",[[\"isActive\",\"=\",true],\"and\",[\"standardCost\",\"<=\",0]]]");
        Assert.Equal(1, noCost["totalCount"]!.GetValue<int>());
        Assert.Equal("بحص", noCost["data"]![0]!["nameAr"].S());

        var category = await Load("[[\"resourceType\",\"=\",\"material\"],\"and\",[\"category\",\"=\",\"ردم\"]]");
        Assert.Equal(2, category["totalCount"]!.GetValue<int>());
        var row = category["data"]!.AsArray().Single(r => r!["nameAr"].S() == "رمل")!;
        Assert.StartsWith(Day(Today), row["costEffectiveFrom"].S()); // الحقول الجديدة في صف الشبكة

        var search = await Load("[[\"resourceType\",\"=\",\"material\"],\"and\",[[\"code\",\"contains\",\"جبس\"],\"or\",[\"nameAr\",\"contains\",\"جبس\"]]]");
        Assert.Equal(1, search["totalCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task Deleting_an_unused_resource_removes_its_history_with_it()
    {
        var api = await TenantAsync();
        var temp = await CreateAsync(api, new { nameAr = "مؤقت", resourceType = "other", unit = "PCS", costSource = "manual", standardCost = 5 }, "temp");
        Ok(await api.Delete($"/construction-resources/{temp["id"].G()}"), "delete");
        Assert.Equal(404, (await api.Get($"/construction-resources/{temp["id"].G()}/costs")).Status);
    }
}
