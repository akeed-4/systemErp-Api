using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// تغطية العمالة والمعدات (المرحلة 2): الساعات المتاحة يوماً بيوم داخل تواريخ المرحلة، بدوام المشروع (8 س) وأيام عمل المورد،
/// مطروحاً منها الإجازات المعتمدة واستبعاد الأصل وما خارج الإيجار، ومقسومة مع المشاريع المتداخلة على الموظف أو الأصل نفسه.
/// المشروع 10 أيام بمرحلة واحدة؛ البند 100 وحدة: عامل أ 3 س (300) وعامل ب 1 س (100) بالتخصص نفسه، ورافعة 1 س (100).
/// الوحدة الكاملة في المرحلة = 10 × 8 = 80 ساعة (أيام العمل 30 في الشهر لأرقام واضحة).
/// </summary>
[Collection("api")]
public class ProjectResourceCoverageTests : TestBase
{
    public ProjectResourceCoverageTests(ErpFactory f) : base(f) { }

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");
    private static string Day(DateTime d) => d.ToString("yyyy-MM-dd");

    private sealed record Lib(Client Api, Guid WorkerA, Guid WorkerB, Guid Crane, Guid Work, Guid Customer);

    private async Task<Lib> LibraryAsync()
    {
        var api = await NewTenantWithModulesAsync("accounting", "car_showroom", "pos", "construction", "hr");
        Ok(await api.Post("/unitsofmeasure", new { code = "PCS", nameAr = "عدد", unitType = "count", isBaseUnit = true, conversionFactor = 1 }), "PCS");
        Ok(await api.Post("/unitsofmeasure", new { code = "HR", nameAr = "ساعة", unitType = "time", isBaseUnit = true, conversionFactor = 1 }), "HR");
        async Task<Guid> R(object b) { var r = await api.Post("/construction-resources", b); Ok(r, "resource"); return r.Data!["id"].G(); }
        var a = await R(new { nameAr = "عامل أ", resourceType = "labor", unit = "HR", costSource = "breakdown", hoursPerUnit = 1, category = "مدني", breakdown = new { rateBasis = "hour", rate = 25, overtimeMultiplier = 1.5 } });
        var b = await R(new { nameAr = "عامل ب", resourceType = "labor", unit = "HR", costSource = "breakdown", hoursPerUnit = 1, category = "مدني", breakdown = new { rateBasis = "hour", rate = 20, overtimeMultiplier = 1.5 } });
        var crane = await R(new { nameAr = "رافعة", resourceType = "equipment", unit = "HR", costSource = "breakdown", hoursPerUnit = 1, equipmentOwnership = "owned", breakdown = new { rateBasis = "hour", rate = 200, overtimeMultiplier = 1 } });
        object Need(Guid r, decimal f) => new { resourceId = r, quantityMode = "per_unit", consumptionFactor = f, wastePercent = 0 };
        var work = await api.Post("/work-items", new { nameAr = "تركيب", unit = "PCS", pricingMode = "manual", sellPrice = 900, sectionName = "الأعمال",
            components = new[] { Need(a, 3), Need(b, 1), Need(crane, 1) } });
        Ok(work, "work");
        var (customer, _) = await SeedCustomerAsync(api);
        return new Lib(api, a, b, crane, work.Data!["id"].G(), customer);
    }

    /// <summary>مشروع ساري بمرحلة واحدة من start لعشرة أيام (أو days)، ودوام 07:00–15:00؛ manualHours = احتياج يدوي بلا مورد مكتبة.</summary>
    private static async Task<(string Id, string Number)> ProjectAsync(Lib l, DateTime start, int days = 10, decimal manualHours = 0)
    {
        var api = l.Api;
        var s = await api.Post("/commercialcontracts", new
        {
            title = "تغطية", projectName = "تغطية", contractType = "construction_boq", workflow = "simple", requiresGuarantee = false, partyName = "عميل", partyId = l.Customer,
            startDate = start, endDate = start.AddDays(days - 1), vatRate = 15, isFeasibilityStudy = true, milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
            items = new object[] { new { description = "تركيب", unit = "عدد", quantity = 100, unitPrice = 900, vatRate = 15, lineKind = "measured", workItemId = l.Work, sortOrder = 1 } },
        });
        Ok(s, "study");
        var id = s.Data!["id"].S();
        Ok(await api.Post($"/commercialcontracts/{id}/cost-analysis/apply", new { }), "price");
        var plan = (await api.Post($"/commercialcontracts/{id}/execution-plan/generate", new { })).Data!;
        if (manualHours > 0)
        {
            var items = plan["resources"]!.AsArray().Select(r => r!.DeepClone()).ToList();
            items.Add(new JsonObject { ["resourceType"] = "labor", ["name"] = "مشرف سلامة", ["unit"] = "HR", ["quantity"] = manualHours, ["hoursPerUnit"] = 1, ["unitCost"] = 50, ["phaseId"] = plan["phases"]![0]!["id"]!.DeepClone() });
            Ok(await api.Put($"/commercialcontracts/{id}/execution-plan/resources", new { items }), "manual requirement");
        }
        Ok(await api.Put($"/commercialcontracts/{id}/project/profile", new { workdayStart = "07:00", workdayEnd = "15:00" }), "hours");
        Ok(await api.Post($"/commercialcontracts/{id}/execution-plan/submit", new { }), "submit");
        Ok(await api.Post($"/commercialcontracts/{id}/execution-plan/approve", new { }), "approve");
        var c = await api.Post($"/feasibility-studies/{id}/create-contract", new { });
        Ok(c, "contract");
        Ok(await api.Post($"/commercialcontracts/{id}/move", new { toStatus = "active" }), "activate");
        return (id, c.Data!["contractNumber"].S());
    }

    private static async Task<Guid> AssignAsync(Client api, string id, object body)
    {
        var r = await api.Post($"/commercialcontracts/{id}/project/resources", body);
        Ok(r, "assign");
        var name = System.Text.Json.JsonSerializer.SerializeToNode(body)!["name"]!.GetValue<string>();
        return r.Data!.AsArray().First(x => x!["name"].S() == name)!["id"].G();
    }

    /// <summary>أصل ثابت (رافعة) على مركز تكلفة المشروع.</summary>
    private static async Task<Guid> AssetAsync(Client api, string contractId, string code)
    {
        var center = (await api.Get($"/commercialcontracts/{contractId}")).Data!["costCenterId"].G();
        var r = await api.Post("/fixedassets", new { assetCode = code, nameAr = "رافعة " + code, nameEn = code, purchaseDate = "2025-01-10", purchaseCost = 100000, currentBookValue = 100000, depreciationRate = 10, costCenterId = center });
        Ok(r, "asset");
        return r.Data!["id"].G();
    }

    private static object Worker(string name, Guid library, string? employeeId = null) => new
    {
        kind = "labor", name, category = "مدني", rateBasis = "hour", rate = 25, hoursPerDay = 8, daysPerMonth = 30, overtimeMultiplier = 1.5, libraryResourceId = library, employeeId,
    };

    private static async Task<JsonNode> CoverageAsync(Client api, string id)
    {
        var r = await api.Get($"/commercialcontracts/{id}/project/resource-coverage");
        Ok(r, "coverage");
        return r.Data!;
    }

    private static JsonNode Row(JsonNode cov, Guid resource) => cov["rows"]!.AsArray().Single(r => r!["resourceId"]?.GetValue<string?>() == resource.ToString())!;
    private static string[] Codes(JsonNode row) => row["alerts"]!.AsArray().Select(a => a!["code"].S()).ToArray();
    private static decimal AlertQty(JsonNode row, string code) => row["alerts"]!.AsArray().Single(a => a!["code"].S() == code)!["quantity"].D();

    [Fact]
    public async Task Labor_coverage_is_time_aware_subtracts_leave_and_never_counts_a_shared_employee_twice()
    {
        var l = await LibraryAsync();
        var api = l.Api;
        var start = DateTime.UtcNow.Date.AddDays(5);
        var (a, _) = await ProjectAsync(l, start, manualHours: 40);

        // بلا إسناد: غير مغطّى بنقص مؤكد، والاحتياج اليدوي بلا مورد مكتبة لا يُحكم عليه
        var cov = await CoverageAsync(api, a);
        Assert.Equal(8m, cov["dailyHours"].D());
        var rowA = Row(cov, l.WorkerA);
        Assert.Equal(300m, rowA["requiredHours"].D());
        Assert.Equal("none", rowA["status"].S());
        Assert.Equal(300m, AlertQty(rowA, "none_assigned"));
        var manual = cov["rows"]!.AsArray().Single(r => r!["name"].S() == "مشرف سلامة")!;
        Assert.Equal("unverifiable", manual["status"].S());
        Assert.Null(manual["coveragePercent"]?.GetValue<decimal?>());
        Assert.Contains("requirement_not_linked", Codes(manual));

        // مورد بالتخصص نفسه غير مربوط بالمكتبة: لا يُحسب، ويُنبَّه عليه
        await AssignAsync(api, a, new { kind = "labor", name = "عامل غير مربوط", category = "مدني", rateBasis = "hour", rate = 20, hoursPerDay = 8, daysPerMonth = 30 });
        cov = await CoverageAsync(api, a);
        Assert.Single(cov["unmatched"]!.AsArray());
        Assert.Contains(cov["alerts"]!.AsArray(), x => x!["code"].S() == "unmatched_assigned");
        Assert.Contains("no_matching_link", Codes(Row(cov, l.WorkerA)));

        // موظف واحد: 10 أيام × 8 = 80 من 300 — تغطية جزئية
        var employee = await api.Post("/employees", new { code = "E1", nameAr = "عامل موظف", hireDate = Day(DateTime.UtcNow.AddYears(-2)), basicSalary = 4000, housingAllowance = 1000, transportAllowance = 0 });
        Assert.Equal(201, employee.Status);
        var e1 = employee.Data!["id"].S();
        await AssignAsync(api, a, Worker("عامل موظف", l.WorkerA, e1));
        // فريق بلا موظف للعامل ب: 80 من 100
        await AssignAsync(api, a, Worker("فريق ب", l.WorkerB));
        cov = await CoverageAsync(api, a);
        rowA = Row(cov, l.WorkerA);
        Assert.Equal(80m, rowA["availableHours"].D());
        Assert.Equal(80m, rowA["coveredHours"].D());
        Assert.Equal(220m, rowA["shortageHours"].D());
        Assert.Equal(26.7m, rowA["coveragePercent"].D());
        Assert.Equal("partial", rowA["status"].S());
        Assert.Equal(1, rowA["assignedCount"]!.GetValue<int>());
        Assert.Equal(20m, Row(cov, l.WorkerB)["shortageHours"].D());

        // إجازة معتمدة آخر يومين: 64 متاحة و16 غير متاحة
        var types = (await api.Get("/leavetypes?pageSize=100")).Data!["items"]!.AsArray().ToDictionary(t => t!["code"].S(), t => t!["id"].S());
        var leave = await api.Post("/leaverequests", new { employeeId = e1, leaveTypeId = types["unpaid"], startDate = Day(start.AddDays(8)), endDate = Day(start.AddDays(9)), reason = "اختبار" });
        Ok(leave, "leave");
        Ok(await api.Post($"/leaverequests/{leave.Data!["id"].S()}/approve"), "approve leave");
        rowA = Row(await CoverageAsync(api, a), l.WorkerA);
        Assert.Equal(64m, rowA["availableHours"].D());
        Assert.Equal(16m, AlertQty(rowA, "absence"));

        // الموظف نفسه في مشروع آخر يتداخل أول 5 أيام: تُقسم ساعات الأيام المتداخلة (4 + 4)، ولا تتجاوز 8 في اليوم
        var (b, bNumber) = await ProjectAsync(l, start, days: 5);
        await AssignAsync(api, b, Worker("عامل موظف", l.WorkerA, e1));
        cov = await CoverageAsync(api, a);
        rowA = Row(cov, l.WorkerA);
        Assert.Equal(44m, rowA["availableHours"].D());          // 5 × 4 + 3 × 8 + إجازة يومين
        Assert.Equal(20m, AlertQty(rowA, "shared_other_project"));
        var assignedA = rowA["assigned"]!.AsArray().Single()!;
        Assert.Contains(bNumber, assignedA["sharedWith"]!.AsArray().Select(x => x.S()));
        var rowB = Row(await CoverageAsync(api, b), l.WorkerA);
        Assert.Equal(20m, rowB["availableHours"].D());          // 5 × 4
        // الأيام المتداخلة: 20 في (أ) + 20 في (ب) = 40 = 5 × 8 — بلا عدّ مزدوج
        Assert.Equal(40m, assignedA["availableHours"].D() - 3 * 8 + rowB["availableHours"].D());

        // التجميع: لكل مورد، وللعمالة بالتخصص (أ + ب بالتخصص «مدني»)
        cov = await CoverageAsync(api, a);
        var civil = cov["byCategory"]!.AsArray().Single(c => c!["name"].S() == "مدني")!;
        Assert.Equal(400m, civil["requiredHours"].D());
        Assert.Equal(124m, civil["coveredHours"].D());   // 44 + 80
        Assert.Equal("partial", civil["status"].S());
        Assert.Equal(300m, cov["byResource"]!.AsArray().Single(t => t!["resourceId"]?.GetValue<string?>() == l.WorkerA.ToString())!["requiredHours"].D());
    }

    [Fact]
    public async Task Equipment_coverage_handles_owned_assets_rentals_missing_data_and_disposal()
    {
        var l = await LibraryAsync();
        var api = l.Api;
        var start = DateTime.UtcNow.Date.AddDays(-20);
        var (c, _) = await ProjectAsync(l, start);
        var assetId = await AssetAsync(api, c, "CR-1");

        // بلا معدة: نقص مؤكد
        var row = Row(await CoverageAsync(api, c), l.Crane);
        Assert.Equal(100m, row["requiredHours"].D());
        Assert.Equal("none", row["status"].S());
        Assert.Equal("owned", row["ownership"].S());

        // مملوكة: 80 من 100، ويلزم استئجار وحدة
        await AssignAsync(api, c, new { kind = "owned_equipment", name = "رافعة الشركة", category = "رافعة", rate = 150, fuelPerHour = 30, maintenancePerHour = 10, operatorPerHour = 10, hoursPerDay = 8, daysPerMonth = 30, fixedAssetId = assetId, libraryResourceId = l.Crane });
        row = Row(await CoverageAsync(api, c), l.Crane);
        Assert.Equal(80m, row["availableHours"].D());
        Assert.Equal("partial", row["status"].S());
        Assert.Equal(20m, row["shortageHours"].D());
        Assert.Equal(1, row["additionalUnitsNeeded"]!.GetValue<int>());
        Assert.Contains("rental_needed", Codes(row));

        // مستأجرة بلا فترة إيجار: لا يكفي الموثّق فلا يُحكم (لا نسبة مضللة)
        var (supplier, _) = await SeedSupplierAsync(api, "مؤجر رافعات");
        var rented = await AssignAsync(api, c, new { kind = "rented_equipment", name = "رافعة مستأجرة", category = "رافعة", supplierId = supplier, rateBasis = "day", rate = 1200, hoursPerDay = 8, daysPerMonth = 30, libraryResourceId = l.Crane });
        row = Row(await CoverageAsync(api, c), l.Crane);
        Assert.Equal("unverifiable", row["status"].S());
        Assert.Null(row["coveragePercent"]?.GetValue<decimal?>());
        Assert.Contains("rental_incomplete", Codes(row));

        // فترة الإيجار أول 5 أيام: 40 + 80 = 120 ≥ 100 مغطّاة، وتنبيه انتهاء الإيجار قبل نهاية المرحلة
        Ok(await api.Put($"/commercialcontracts/{c}/project/resources/{rented}", new
        {
            kind = "rented_equipment", name = "رافعة مستأجرة", category = "رافعة", supplierId = supplier, rateBasis = "day", rate = 1200, hoursPerDay = 8, daysPerMonth = 30,
            libraryResourceId = l.Crane, rentalStart = start, rentalEnd = start.AddDays(4),
        }), "rental dates");
        row = Row(await CoverageAsync(api, c), l.Crane);
        Assert.Equal(120m, row["availableHours"].D());
        Assert.Equal(100m, row["coveredHours"].D());
        Assert.Equal("full", row["status"].S());
        Assert.Equal(100m, row["coveragePercent"].D());
        Assert.Contains("rental_expired", Codes(row));

        // استبعاد الأصل في اليوم السادس: المملوكة 40 فقط → 80 جزئية
        Ok(await api.Post($"/fixedassets/{assetId}/dispose", new { date = Day(start.AddDays(5)), proceeds = 0 }), "dispose");
        row = Row(await CoverageAsync(api, c), l.Crane);
        Assert.Equal(80m, row["availableHours"].D());
        Assert.Equal("partial", row["status"].S());
        Assert.Contains("asset_disposed", Codes(row));
        Assert.Contains("equipment_unavailable", Codes(row));
    }

    [Fact]
    public async Task Owned_asset_shared_with_an_overlapping_project_is_split_not_duplicated()
    {
        var l = await LibraryAsync();
        var api = l.Api;
        var start = DateTime.UtcNow.Date.AddDays(3);
        var (c1, _) = await ProjectAsync(l, start);
        var (c2, n2) = await ProjectAsync(l, start);      // متداخل كلياً
        var asset = await AssetAsync(api, c1, "CR-9");
        object Owned() => new { kind = "owned_equipment", name = "رافعة مشتركة", rate = 150, hoursPerDay = 8, daysPerMonth = 30, fixedAssetId = asset, libraryResourceId = l.Crane };
        await AssignAsync(api, c1, Owned());
        await AssignAsync(api, c2, Owned());
        var r1 = Row(await CoverageAsync(api, c1), l.Crane);
        var r2 = Row(await CoverageAsync(api, c2), l.Crane);
        Assert.Equal(40m, r1["availableHours"].D());
        Assert.Equal(40m, r2["availableHours"].D());
        Assert.Equal(80m, r1["availableHours"].D() + r2["availableHours"].D()); // = 10 × 8 للأصل الواحد
        Assert.Equal(40m, AlertQty(r1, "shared_other_project"));
        Assert.Contains(n2, r1["assigned"]![0]!["sharedWith"]!.AsArray().Select(x => x.S()));
    }
}
