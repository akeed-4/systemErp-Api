using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// المعدات والآليات فوق ما هو قائم بلا تكرار: المملوكة بأصلها الثابت (أصل واحد لمعدة واحدة)، والإسناد ينشئ مورد المشروع فتُحمَّل
/// ساعاتها من التقرير اليومي المعتمد، والمستأجرة لا تُسند بلا عقد إيجار سارٍ ويُستحق إيجارها بشروطه، وأمر الصيانة يصرف القطع ويقيد
/// تكلفتها مرة واحدة ويعكسها عند الإلغاء، والعزل بين المنشآت.
/// </summary>
[Collection("api")]
public class EquipmentManagementTests : TestBase
{
    public EquipmentManagementTests(ErpFactory f) : base(f) { }

    private static readonly DateTime Day = new(2026, 11, 1);

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");

    private sealed record Project(Client Api, string Id, Guid Item);

    private async Task<Project> ProjectAsync(params string[] extraModules)
    {
        var api = await NewTenantWithModulesAsync(new[] { "accounting", "construction" }.Concat(extraModules).ToArray());
        var (customer, _) = await SeedCustomerAsync(api); // التفعيل يلزم طرفاً مربوطاً بسجله
        var created = await api.Post("/commercialcontracts", new
        {
            title = "مشروع المعدات", contractType = "construction_boq", workflow = "simple", requiresGuarantee = false, partyName = "عميل", partyId = customer,
            startDate = Day.AddDays(-10), endDate = Day.AddMonths(6), vatRate = 15,
            items = new[] { new { description = "حفر", unit = "م3", quantity = 1000, unitPrice = 50, vatRate = 15, lineKind = "measured" } },
            milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
        });
        Ok(created, "contract");
        var id = created.Data!["id"].S();
        // التنفيذ (التقارير اليومية) لعقد ساري وحده
        Ok(await api.Post($"/commercialcontracts/{id}/move", new { toStatus = "active" }), "activate");
        Ok(await api.Put($"/commercialcontracts/{id}/project/profile", new { workdayStart = "07:00", workdayEnd = "17:00" }), "profile");
        return new Project(api, id, created.Data["items"]!.AsArray()[0]!["id"].G());
    }

    private static async Task<Guid> CostCenterAsync(Client api, string code)
    {
        var c = await api.Post("/costcenters", new { code, nameAr = "مركز " + code, nameEn = code, isActive = true });
        Ok(c, "cost center");
        return c.Data!["id"].G();
    }

    private static async Task ApprovedReportAsync(Project p, Guid resourceId, DateTime date, decimal hours)
    {
        var r = await p.Api.Post($"/commercialcontracts/{p.Id}/site-reports", new
        {
            reportDate = date,
            hours = new[] { new { start = "07:00", end = "08:00", contractItemId = p.Item, targetQuantity = 10, actualQuantity = 10 } },
            resources = new[] { new { projectResourceId = resourceId, contractItemId = p.Item, hours } },
        });
        Ok(r, "report");
        var rid = r.Data!["id"].S();
        Ok(await p.Api.Post($"/commercialcontracts/{p.Id}/site-reports/{rid}/submit"), "submit");
        Ok(await p.Api.Post($"/commercialcontracts/{p.Id}/site-reports/{rid}/approve"), "approve");
    }

    private static async Task<decimal> BalanceAsync(Client api, string code)
    {
        var rows = (await api.Get("/reports/TrialBalance")).Data!.AsArray();
        return rows.Where(r => r!["accountCode"].S() == code).Sum(r => r!["closingDebit"].D() - r!["closingCredit"].D());
    }

    private static async Task AssertBooksBalancedAsync(Client api)
    {
        var rows = (await api.Get("/reports/TrialBalance")).Data!.AsArray();
        Assert.Equal(rows.Sum(r => r!["closingDebit"].D()), rows.Sum(r => r!["closingCredit"].D()));
    }

    private static async Task<string> EquipmentCostAccountAsync(Client api)
    {
        var options = await api.Get("/equipment/options");
        Ok(options, "options");
        var code = options.Data!["defaultExpenseAccountCode"].S();
        if (!string.IsNullOrEmpty(code)) return code;
        return (await api.Get("/accounting-settings")).Data!["projectEquipmentCostAccountCode"].S();
    }

    [Fact]
    public async Task Owned_equipment_links_one_asset_and_charges_its_hours_to_the_project_through_the_daily_report()
    {
        var p = await ProjectAsync("hr");
        var api = p.Api;
        var center = await CostCenterAsync(api, "EQP");
        var asset = await api.Post("/fixedassets", new
        {
            assetCode = "FA-EQ1", nameAr = "حفار", nameEn = "Excavator", purchaseDate = "2026-01-10", purchaseCost = 300000, currentBookValue = 300000,
            depreciationRate = 20, costCenterId = center,
        });
        Ok(asset, "asset");
        var assetId = asset.Data!["id"].G();
        var op = await api.Post("/employees", new { code = "OP1", nameAr = "مشغّل الحفار", hireDate = "2025-01-01", basicSalary = 4000 });
        Ok(op, "operator");

        var created = await api.Post("/equipment", new
        {
            nameAr = "حفار كاتربيلر", category = "حفار", ownership = "owned", fixedAssetId = assetId, costCenterId = center,
            rate = 80, fuelPerHour = 20, serviceIntervalHours = 250,
        });
        Ok(created, "equipment");
        var eq = created.Data!;
        var eqId = eq["id"].G();
        Assert.StartsWith("EQ-", eq["code"].S());
        Assert.Equal(100m, eq["hourlyCost"].D());
        Assert.Equal("FA-EQ1", eq["fixedAssetCode"].S());
        Assert.Equal(300000m, eq["assetPurchaseCost"].D()); // القيمة من الأصل الثابت، لا نسخة ثانية

        // أصل واحد لمعدة واحدة، والقيم السالبة مرفوضة
        Assert.Equal(400, (await api.Post("/equipment", new { nameAr = "معدة ثانية", ownership = "owned", fixedAssetId = assetId, rate = 10 })).Status);
        Assert.Equal(400, (await api.Post("/equipment", new { nameAr = "معدة", ownership = "owned", rate = -1 })).Status);
        Assert.Equal(400, (await api.Post("/equipment", new { nameAr = "معدة", ownership = "leased" })).Status);

        // الإسناد: مورد المشروع بتكلفة ساعة المعدة، والمعدة «مسندة»، ولا إسناد ثانٍ
        var assigned = await api.Post("/equipment/assignments", new
        {
            equipmentId = eqId, contractId = p.Id, siteName = "الموقع أ", operatorEmployeeId = op.Data!["id"].G(), fromDate = Day.AddDays(-5),
        });
        Ok(assigned, "assign");
        var assignmentId = assigned.Data!["id"].S();
        var resourceId = assigned.Data["projectResourceId"].G();
        Assert.Equal("مشغّل الحفار", assigned.Data["operatorName"].S());
        var resources = (await api.Get($"/commercialcontracts/{p.Id}/project/resources")).Data!.AsArray();
        var resource = resources.Single(r => r!["id"].G() == resourceId)!;
        Assert.Equal("owned_equipment", resource["kind"].S());
        Assert.Equal(100m, resource["hourlyCost"].D());
        Assert.Equal(assetId, resource["fixedAssetId"].G());
        Assert.Equal("assigned", (await api.Get($"/equipment/{eqId}")).Data!["status"].S());
        Assert.Equal(409, (await api.Post("/equipment/assignments", new { equipmentId = eqId, contractId = p.Id })).Status);

        // في «تعيين العمال والمعدات»: المورد يظهر مربوطاً بإسناده، ولا يُعدَّل أو يُحذف أو يُكرَّر أصله يدوياً
        Assert.Equal(eqId, resource["equipmentId"].G());
        Assert.True(resource["equipmentAssignmentActive"]!.GetValue<bool>());
        Assert.Equal("الموقع أ", resource["siteName"].S());
        Assert.Equal("مشغّل الحفار", resource["operatorName"].S());
        var edited = resource.DeepClone();
        edited["isActive"] = false;
        Assert.Equal(409, (await api.Put($"/commercialcontracts/{p.Id}/project/resources/{resourceId}", edited)).Status);
        Assert.Equal(409, (await api.Delete($"/commercialcontracts/{p.Id}/project/resources/{resourceId}")).Status);
        Assert.Equal(400, (await api.Post($"/commercialcontracts/{p.Id}/project/resources", new
        {
            kind = "owned_equipment", name = "حفار يدوي", rateBasis = "hour", rate = 90, fixedAssetId = assetId,
        })).Status);

        // الساعات من التقرير اليومي المعتمد (5 × 100) ولا تُسجَّل يدوياً للمعدة المسندة
        await ApprovedReportAsync(p, resourceId, Day, 5);
        var detail = (await api.Get($"/equipment/assignments?equipmentId={eqId}")).Data!.AsArray().Single()!;
        Assert.Equal(5m, detail["reportedHours"].D());
        Assert.Equal(500m, detail["chargedCost"].D());
        var report = await api.Get($"/equipment/cost-report?from={Day:yyyy-MM-dd}&to={Day:yyyy-MM-dd}");
        Ok(report, "cost report");
        Assert.Equal(500m, report.Data!["equipment"]!.AsArray().Single()!["chargedToProjects"].D());
        Assert.Equal(500m, report.Data["projects"]!.AsArray().Single()!["totalCost"].D());
        Assert.Equal(409, (await api.Post("/equipment/logs", new { equipmentId = eqId, kind = "operation", date = Day, hours = 3 })).Status);
        Assert.Equal(400, (await api.Post("/equipment/logs", new { equipmentId = eqId, kind = "downtime", date = Day, hours = 2 })).Status); // بلا سبب
        Ok(await api.Post("/equipment/logs", new { equipmentId = eqId, kind = "downtime", date = Day, hours = 2, reason = "عطل هيدروليك" }), "downtime");

        // الإرجاع: المورد يتوقف ويبقى لتقاريره، والمعدة متاحة، ولا تُحذف وعليها سجل
        Assert.Equal(400, (await api.Post($"/equipment/assignments/{assignmentId}/return", new { date = Day.AddDays(-30) })).Status);
        Ok(await api.Post($"/equipment/assignments/{assignmentId}/return", new { date = Day.AddDays(1), meterReading = 1200 }), "return");
        Assert.Equal("available", (await api.Get($"/equipment/{eqId}")).Data!["status"].S());
        var afterReturn = (await api.Get($"/commercialcontracts/{p.Id}/project/resources")).Data!.AsArray().Single(r => r!["id"].G() == resourceId)!;
        Assert.False(afterReturn["isActive"]!.GetValue<bool>());
        Assert.False(afterReturn["equipmentAssignmentActive"]!.GetValue<bool>());
        // لا يُعاد تفعيله من التعيين بعد الإرجاع (المعدة «متاحة» في السجل)
        var reactivate = afterReturn.DeepClone();
        reactivate["isActive"] = true;
        Assert.Equal(409, (await api.Put($"/commercialcontracts/{p.Id}/project/resources/{resourceId}", reactivate)).Status);
        Assert.Equal(409, (await api.Delete($"/equipment/{eqId}")).Status);
        await AssertBooksBalancedAsync(api);
    }

    [Fact]
    public async Task Maintenance_order_consumes_spare_parts_and_posts_cost_once_and_cancellation_reverses_it()
    {
        var p = await ProjectAsync();
        var api = p.Api;
        var part = await SeedProductAsync(api, "FLT", stock: 10, cost: 40);
        var warehouse = (await api.Get("/warehouses")).Data!["items"]!.AsArray().First(x => x!["isDefault"]!.GetValue<bool>())!["id"].G();
        var center = await CostCenterAsync(api, "WSH");
        var (_, supplierAccount) = await SeedSupplierAsync(api, "محطة وقود");
        var eq = await api.Post("/equipment", new { nameAr = "شيول", ownership = "owned", rate = 60, costCenterId = center, serviceIntervalDays = 90 });
        Ok(eq, "equipment");
        var eqId = eq.Data!["id"].G();
        var account = await EquipmentCostAccountAsync(api);
        var before = await BalanceAsync(api, account);

        // قطعة غيار بلا مستودع أو بكمية صفر مرفوضة
        Assert.Equal(400, (await api.Post("/equipment-maintenance", new { equipmentId = eqId, type = "preventive", description = "تغيير فلاتر", parts = new[] { new { productId = part, quantity = 3 } } })).Status);
        Assert.Equal(400, (await api.Post("/equipment-maintenance", new { equipmentId = eqId, type = "preventive", description = "تغيير فلاتر", warehouseId = warehouse, parts = new[] { new { productId = part, quantity = 0 } } })).Status);

        var order = await api.Post("/equipment-maintenance", new
        {
            equipmentId = eqId, type = "preventive", description = "صيانة 250 ساعة", warehouseId = warehouse, scheduledDate = Day,
            laborCost = 150, parts = new[] { new { productId = part, quantity = 3 } },
        });
        Ok(order, "order");
        var orderId = order.Data!["id"].S();
        Assert.StartsWith("EMO-", order.Data["orderNumber"].S());

        Ok(await api.Post($"/equipment-maintenance/{orderId}/start"), "start");
        Assert.Equal("maintenance", (await api.Get($"/equipment/{eqId}")).Data!["status"].S());
        Assert.Equal(409, (await api.Post("/equipment/assignments", new { equipmentId = eqId, contractId = p.Id })).Status); // في الصيانة

        var done = await api.Post($"/equipment-maintenance/{orderId}/complete", new { completedAt = Day, workDone = "تم تغيير الفلاتر" });
        Ok(done, "complete");
        Assert.Equal("completed", done.Data!["status"].S());
        Assert.Equal(120m, done.Data["partsCost"].D());
        Assert.Equal(270m, done.Data["totalCost"].D()); // العمالة بلا حساب دائن: للمتابعة ولا تُقيَّد
        var eqAfter = (await api.Get($"/equipment/{eqId}")).Data!;
        Assert.Equal("available", eqAfter["status"].S());
        Assert.StartsWith(Day.ToString("yyyy-MM-dd"), eqAfter["lastServiceDate"].S());
        Assert.Equal(120m, await BalanceAsync(api, account) - before); // القطع وحدها، مرة واحدة
        Assert.Equal(409, (await api.Post($"/equipment-maintenance/{orderId}/complete", new { })).Status);

        // وقود بحساب سداد: قيد واحد على مركز المعدة؛ وبلا حساب: متابعة بلا قيد
        var fuel = await api.Post("/equipment/logs", new { equipmentId = eqId, kind = "fuel", date = Day, quantity = 100, amount = 230, creditAccountCode = supplierAccount });
        Ok(fuel, "fuel");
        Assert.True(fuel.Data!["posted"]!.GetValue<bool>());
        var tracked = await api.Post("/equipment/logs", new { equipmentId = eqId, kind = "fuel", date = Day, quantity = 50, amount = 115 });
        Ok(tracked, "fuel tracked");
        Assert.False(tracked.Data!["posted"]!.GetValue<bool>());
        Assert.Equal(350m, await BalanceAsync(api, account) - before);
        Ok(await api.Delete($"/equipment/logs/{fuel.Data["id"].S()}"), "delete fuel");
        Assert.Equal(120m, await BalanceAsync(api, account) - before);

        // الإلغاء بعد الإكمال: بسبب، ويعيد القطع ويعكس القيد
        Assert.Equal(400, (await api.Post($"/equipment-maintenance/{orderId}/cancel", new { })).Status);
        Ok(await api.Post($"/equipment-maintenance/{orderId}/cancel", new { reason = "أُدخل خطأ" }), "cancel");
        Assert.Equal(0m, await BalanceAsync(api, account) - before);
        var stock = (await api.Get($"/products/{part}")).Data!["currentStock"].D();
        Assert.Equal(10m, stock);
        await AssertBooksBalancedAsync(api);

        var report = await api.Get($"/equipment/cost-report?from={Day:yyyy-MM-dd}&to={Day:yyyy-MM-dd}");
        Ok(report, "cost report");
        var row = report.Data!["equipment"]!.AsArray().Single()!;
        Assert.Equal(115m, row["fuelCost"].D());
        Assert.Equal(0m, row["maintenanceCost"].D()); // الملغى لا يُحتسب
    }

    [Fact]
    public async Task Rented_equipment_needs_an_active_rental_and_accrues_rent_by_its_terms()
    {
        var p = await ProjectAsync();
        var api = p.Api;
        var (supplier, _) = await SeedSupplierAsync(api, "مؤجر رافعات");
        var eq = await api.Post("/equipment", new { nameAr = "رافعة 50 طن", ownership = "rented", category = "رافعة" });
        Ok(eq, "equipment");
        var eqId = eq.Data!["id"].G();

        Assert.Equal(409, (await api.Post("/equipment/assignments", new { equipmentId = eqId, contractId = p.Id, fromDate = Day })).Status);
        Assert.Equal(400, (await api.Post("/equipment/rentals", new { equipmentId = eqId, supplierId = supplier, startDate = Day, endDate = Day.AddDays(-1), rateBasis = "hour", rate = 100 })).Status);

        var rental = await api.Post("/equipment/rentals", new
        {
            equipmentId = eqId, supplierId = supplier, startDate = Day.AddDays(-10), endDate = Day.AddMonths(2), rateBasis = "hour", rate = 100,
            idleBillingPercent = 50,
        });
        Ok(rental, "rental");
        var rentalId = rental.Data!["id"].S();
        Assert.StartsWith("ERC-", rental.Data["contractNumber"].S());
        Assert.Equal("draft", rental.Data["status"].S());
        Ok(await api.Post($"/equipment/rentals/{rentalId}/activate"), "activate");
        Assert.Equal(409, (await api.Post($"/equipment/rentals/{rentalId}/activate")).Status);

        var assigned = await api.Post("/equipment/assignments", new { equipmentId = eqId, contractId = p.Id, fromDate = Day.AddDays(-2) });
        Ok(assigned, "assign");
        var resourceId = assigned.Data!["projectResourceId"].G();
        var resource = (await api.Get($"/commercialcontracts/{p.Id}/project/resources")).Data!.AsArray().Single(r => r!["id"].G() == resourceId)!;
        Assert.Equal("rented_equipment", resource["kind"].S());
        Assert.Equal(supplier, resource["supplierId"].G());
        Assert.Equal(100m, resource["hourlyCost"].D());
        Assert.Equal(50m, resource["idleBillingPercent"].D());

        await ApprovedReportAsync(p, resourceId, Day, 4);
        var accruals = (await api.Get($"/commercialcontracts/{p.Id}/site-reports/rental-accruals")).Data!.AsArray();
        Assert.Equal(400m, accruals.Sum(a => a!["amount"].D()));
        var listed = (await api.Get($"/equipment/rentals?equipmentId={eqId}")).Data!.AsArray().Single()!;
        Assert.Equal(400m, listed["accruedAmount"].D());
        Assert.Equal(400m, listed["openAmount"].D());

        // لا يُغلق العقد والمعدة مسندة؛ بعد الإرجاع يُغلق وتصير «مُعادة للمؤجر»
        Assert.Equal(409, (await api.Post($"/equipment/rentals/{rentalId}/close")).Status);
        Ok(await api.Post($"/equipment/assignments/{assigned.Data["id"].S()}/return", new { date = Day.AddDays(1) }), "return");
        Ok(await api.Post($"/equipment/rentals/{rentalId}/close"), "close");
        Assert.Equal("returned", (await api.Get($"/equipment/{eqId}")).Data!["status"].S());
        Assert.Equal(409, (await api.Delete($"/equipment/rentals/{rentalId}")).Status);

        var dashboard = await api.Get("/equipment/dashboard");
        Ok(dashboard, "dashboard");
        Assert.Equal(1, dashboard.Data!["rented"]!.GetValue<int>());
    }

    [Fact]
    public async Task Equipment_is_isolated_per_tenant_and_requires_the_construction_module()
    {
        var p = await ProjectAsync();
        var eq = await p.Api.Post("/equipment", new { nameAr = "مولد", ownership = "owned", rate = 30 });
        Ok(eq, "equipment");
        var eqId = eq.Data!["id"].S();

        var other = await NewConstructionTenantAsync();
        Assert.Equal(404, (await other.Get($"/equipment/{eqId}")).Status);
        Assert.Empty((await other.Get("/equipment")).Data!.AsArray());
        Assert.Equal(404, (await other.Put($"/equipment/{eqId}", new { nameAr = "اختراق", ownership = "owned" })).Status);
        Assert.Equal(400, (await other.Post("/equipment/assignments", new { equipmentId = eqId, contractId = p.Id })).Status is 400 or 404 ? 400 : 0);

        var plain = await NewTenantAsync();
        Assert.Equal(403, (await plain.Get("/equipment")).Status);

        var catalog = (await p.Api.Get("/permissions/catalog")).Data!;
        var screens = catalog["modules"]!.AsArray().SelectMany(m => m!["screens"]!.AsArray()).Select(s => s!["id"].S()).ToList();
        Assert.Contains("equipment", screens);
        Assert.Contains("equipment-maintenance", screens);
    }
}
