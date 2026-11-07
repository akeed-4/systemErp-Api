using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// المرحلة 1 لمشاريع المقاولات: تكلفة الساعة لموارد المشروع، وخطة الإنجاز اليومية، والتقرير اليومي بالساعة (المشرف يسجّل ويقدّم
/// والمهندس يعتمد أو يعيد)، وحسابات اليوم (الإنتاج، نسبة التحقيق، التوقف، تكلفة الساعة والوحدة، أسباب الانخفاض)، وتقارير الإنجاز.
/// المثال: صب خرسانة 500 م3 بسعر 300، والمستهدف 10 م3 في الساعة من 07:00 إلى 11:00، والمنفَّذ 9 و12 و8 و10.
/// </summary>
[Collection("api")]
public class SiteDailyReportTests : TestBase
{
    public SiteDailyReportTests(ErpFactory f) : base(f) { }

    private static readonly DateTime Day = new(2026, 11, 1);

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");

    private sealed record Project(string Id, Guid Concrete, Guid Steel);

    /// <summary>عقد مقاولات ببندين، ساعات عمله 07:00–11:00، وخطة 10 م3/ساعة لصب الخرسانة.</summary>
    private static async Task<Project> ProjectAsync(Client api)
    {
        var created = await api.Post("/commercialcontracts", new
        {
            title = "أعمال خرسانية", contractType = "construction_boq", workflow = "simple", requiresGuarantee = false, partyName = "عميل",
            startDate = Day.AddDays(-10), endDate = Day.AddMonths(6), vatRate = 15,
            items = new object[]
            {
                new { description = "صب خرسانة", unit = "م3", quantity = 500, unitPrice = 300, vatRate = 15, lineKind = "measured" },
                new { description = "حديد تسليح", unit = "طن", quantity = 50, unitPrice = 3000, vatRate = 15, lineKind = "measured" },
            },
            milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
        });
        Ok(created, "create");
        var id = created.Data!["id"].S();
        var items = created.Data["items"]!.AsArray();
        var project = new Project(id, items.First(i => i!["description"].S() == "صب خرسانة")!["id"].G(), items.First(i => i!["description"].S() == "حديد تسليح")!["id"].G());
        Ok(await api.Put($"/commercialcontracts/{id}/project/profile", new { workdayStart = "07:00", workdayEnd = "11:00" }), "hours");
        Ok(await api.Put($"/commercialcontracts/{id}/project/daily-plans", new
        {
            plans = new[] { new { contractItemId = project.Concrete, targetPerHour = 10, effectiveFrom = Day.AddDays(-5) } },
        }), "plan");
        return project;
    }

    private static async Task<JsonArray> AddResourceAsync(Client api, string id, object body)
    {
        var r = await api.Post($"/commercialcontracts/{id}/project/resources", body);
        Ok(r, "resource");
        return r.Data!.AsArray();
    }

    private static Guid ResourceId(JsonArray list, string name) => list.First(r => r!["name"].S() == name)!["id"].G();

    // ---------- تكلفة الساعة ----------

    [Fact]
    public async Task Hourly_cost_is_computed_for_each_resource_type()
    {
        var api = await NewTenantAsync();
        var p = await ProjectAsync(api);
        var (supplier, _) = await SeedSupplierAsync(api, "مؤجر رافعات");

        await AddResourceAsync(api, p.Id, new { kind = "owned_equipment", name = "حفار 1", code = "EX-01", category = "حفار", rate = 100, fuelPerHour = 30, maintenancePerHour = 10, operatorPerHour = 20 });
        await AddResourceAsync(api, p.Id, new { kind = "rented_equipment", name = "رافعة", category = "رافعة", supplierId = supplier, rateBasis = "day", rate = 1200, hoursPerDay = 8, extraCostPerHour = 10, rentalStart = Day, rentalEnd = Day.AddDays(30) });
        await AddResourceAsync(api, p.Id, new { kind = "engineer", name = "م. خالد", category = "مدني", rateBasis = "month", rate = 20800, hoursPerDay = 8, daysPerMonth = 26 });
        var list = await AddResourceAsync(api, p.Id, new { kind = "labor", name = "نجارون", category = "نجارة", rateBasis = "day", rate = 200, hoursPerDay = 8 });

        JsonNode R(string name) => list.First(r => r!["name"].S() == name)!;
        Assert.Equal(160m, R("حفار 1")["hourlyCost"].D());          // 100 + 30 + 10 + 20
        Assert.Equal(160m, R("حفار 1")["overtimeHourlyCost"].D());  // الآلية: معامل 1 افتراضاً
        Assert.Equal(160m, R("رافعة")["hourlyCost"].D());            // 1200 ÷ 8 + 10
        Assert.Equal(100m, R("م. خالد")["hourlyCost"].D());          // 20800 ÷ (8 × 26)
        Assert.Equal(150m, R("م. خالد")["overtimeHourlyCost"].D());  // × 1.5
        Assert.Equal(25m, R("نجارون")["hourlyCost"].D());            // 200 ÷ 8
        Assert.Equal(37.5m, R("نجارون")["overtimeHourlyCost"].D());

        // المستأجرة بلا مؤجر، والتكلفة الصفرية، والنوع غير المعروف
        Assert.Equal(400, (await api.Post($"/commercialcontracts/{p.Id}/project/resources", new { kind = "rented_equipment", name = "x", rateBasis = "hour", rate = 50 })).Status);
        Assert.Equal(400, (await api.Post($"/commercialcontracts/{p.Id}/project/resources", new { kind = "labor", name = "x", rateBasis = "day", rate = 0 })).Status);
        Assert.Equal(400, (await api.Post($"/commercialcontracts/{p.Id}/project/resources", new { kind = "drone", name = "x", rate = 5 })).Status);
    }

    // ---------- القالب والحسابات ----------

    [Fact]
    public async Task Daily_report_template_follows_plan_and_server_computes_productivity_and_costs()
    {
        var api = await NewTenantAsync();
        var p = await ProjectAsync(api);
        var cement = await SeedProductAsync(api, "CEM", stock: 100, cost: 40);
        var list = await AddResourceAsync(api, p.Id, new { kind = "owned_equipment", name = "حفار 1", rate = 100, fuelPerHour = 30, maintenancePerHour = 10, operatorPerHour = 20 });
        list = await AddResourceAsync(api, p.Id, new { kind = "engineer", name = "م. خالد", rateBasis = "month", rate = 20800, hoursPerDay = 8, daysPerMonth = 26 });
        list = await AddResourceAsync(api, p.Id, new { kind = "labor", name = "عمال صب", rateBasis = "day", rate = 200, hoursPerDay = 8 });

        // القالب: أربع فترات × بند الخطة، المستهدف 10 لكل ساعة
        var template = (await api.Get($"/commercialcontracts/{p.Id}/site-reports/day?date={Day:yyyy-MM-dd}")).Data!;
        Assert.Null(template["id"]?.GetValue<string?>());
        var hours = template["hours"]!.AsArray();
        Assert.Equal(new[] { "07:00", "08:00", "09:00", "10:00" }, hours.Select(h => h!["start"].S()));
        Assert.All(hours, h => Assert.Equal(10m, h!["targetQuantity"].D()));

        var actuals = new[] { 9m, 12m, 8m, 10m };
        var created = await api.Post($"/commercialcontracts/{p.Id}/site-reports", new
        {
            reportDate = Day,
            hours = hours.Select((h, k) => new
            {
                start = h!["start"].S(), end = h["end"].S(), contractItemId = p.Concrete, targetQuantity = 10, actualQuantity = actuals[k],
                downtimeMinutes = k == 2 ? 15 : 0, downtimeReason = k == 2 ? "equipment_breakdown" : null,
            }),
            resources = new object[]
            {
                new { projectResourceId = ResourceId(list, "حفار 1"), contractItemId = p.Concrete, hours = 4 },
                new { projectResourceId = ResourceId(list, "م. خالد"), hours = 4, overtimeHours = 1 },
                new { projectResourceId = ResourceId(list, "عمال صب"), contractItemId = p.Concrete, hours = 4, idleHours = 0.5, idleReason = "weather" },
            },
            materials = new[] { new { contractItemId = p.Concrete, productId = cement, quantity = 10 } },
        });
        Ok(created, "create report");
        var s = created.Data!["summary"]!;
        Assert.Equal(4m, s["workingHours"].D());
        Assert.Equal(0.25m, s["downtimeHours"].D());
        // الموارد: 4 × 160 = 640، والمهندس 4 × 100 + 1 × 150 = 550، والعمال (4 + 0.5 توقف) × 25 = 112.5؛ والمواد 10 × 40 = 400
        Assert.Equal(1302.5m, s["resourceCost"].D());
        Assert.Equal(400m, s["materialCost"].D());
        Assert.Equal(1702.5m, s["totalCost"].D());
        Assert.Equal(425.63m, s["costPerHour"].D());                 // 1702.5 ÷ 4 ساعات
        Assert.Equal(97.5m, s["achievementPercent"].D());            // 39 من 40 (بقيمة البند)
        var item = s["items"]!.AsArray().Single()!;
        Assert.Equal(40m, item["target"].D());
        Assert.Equal(39m, item["actual"].D());
        Assert.Equal(97.5m, item["achievementPercent"].D());
        // المهندس بلا بند يُوزَّع على البند المنتج الوحيد: تكلفة البند كاملة ÷ 39
        Assert.Equal(1702.5m, item["cost"].D());
        Assert.Equal(43.6538m, item["costPerUnit"].D());
        Assert.Equal(0m, s["unallocatedCost"].D());
        // أسباب الانخفاض: الطقس (30 دقيقة توقف عمال) ثم عطل الآلية (15)، والفترة الثالثة دون 90% من المستهدف
        var reasons = s["reasons"]!.AsArray();
        Assert.Equal("weather", reasons[0]!["reason"].S());
        Assert.Equal(30, reasons[0]!["minutes"]!.GetValue<int>());
        Assert.Equal("equipment_breakdown", reasons[1]!["reason"].S());
        var low = s["lowSlots"]!.AsArray().Single()!;
        Assert.Equal("09:00", low["start"].S());
        Assert.Equal("equipment_breakdown", low["reason"].S());

        // اليوم نفسه مرة أخرى مرفوض، وقراءة اليوم تعيد المحفوظ
        Assert.Equal(409, (await api.Post($"/commercialcontracts/{p.Id}/site-reports", new { reportDate = Day })).Status);
        Assert.Equal(created.Data["id"].S(), (await api.Get($"/commercialcontracts/{p.Id}/site-reports/day?date={Day:yyyy-MM-dd}")).Data!["id"].S());
    }

    [Fact]
    public async Task Supervisor_can_shorten_slots_and_invalid_rows_are_reported_line_by_line()
    {
        var api = await NewTenantAsync();
        var p = await ProjectAsync(api);
        var list = await AddResourceAsync(api, p.Id, new { kind = "labor", name = "عمال", rateBasis = "hour", rate = 25 });

        // المشرف يعدّل الفترة (نصف ساعة)
        var ok = await api.Post($"/commercialcontracts/{p.Id}/site-reports", new
        {
            reportDate = Day,
            hours = new[] { new { start = "07:00", end = "07:30", contractItemId = p.Concrete, targetQuantity = 5, actualQuantity = 5 } },
        });
        Ok(ok, "custom slot");
        Assert.Equal(0.5m, ok.Data!["summary"]!["workingHours"].D());

        var bad = await api.Post($"/commercialcontracts/{p.Id}/site-reports", new
        {
            reportDate = Day.AddDays(1),
            hours = new object[]
            {
                new { start = "08:00", end = "07:00", contractItemId = p.Concrete, targetQuantity = 10, actualQuantity = 5 },          // فترة مقلوبة
                new { start = "09:00", end = "10:00", contractItemId = Guid.NewGuid(), targetQuantity = 10, actualQuantity = 5 },     // بند غريب
                new { start = "10:00", end = "11:00", contractItemId = p.Concrete, targetQuantity = 10, actualQuantity = 5, downtimeMinutes = 20 }, // بلا سبب
                new { start = "10:00", end = "11:00", contractItemId = p.Concrete, targetQuantity = 10, actualQuantity = -1 },        // مكرر وسالب
            },
            resources = new object[] { new { projectResourceId = ResourceId(list, "عمال"), hours = 0 }, new { projectResourceId = Guid.NewGuid(), hours = 2 } },
            materials = new object[] { new { contractItemId = p.Concrete, productId = Guid.NewGuid(), quantity = 0 } },
        });
        Assert.Equal(400, bad.Status);
        var errors = bad.Body!["errors"]!.AsArray().Select(e => e.S()).ToList();
        Assert.True(errors.Count >= 9, string.Join(" | ", errors));
        Assert.Contains(errors, e => e.Contains("السطر 1") && e.Contains("الفترة"));
        Assert.Contains(errors, e => e.Contains("السطر 2") && e.Contains("البند"));
        Assert.Contains(errors, e => e.Contains("السطر 3") && e.Contains("سبب التوقف"));
        Assert.Contains(errors, e => e.Contains("السطر 4") && e.Contains("مكرر"));
        Assert.Contains(errors, e => e.StartsWith("الموارد، السطر 2"));
        Assert.Contains(errors, e => e.StartsWith("المواد، السطر 1"));
    }

    // ---------- دورة الاعتماد والصلاحيات ----------

    private async Task<(Client Client, Guid Id)> UserWithRoleAsync(Client owner, string roleNameAr)
    {
        var roles = (await owner.Get("/roles")).Data!.AsArray();
        var roleId = roles.First(r => r!["nameAr"].S() == roleNameAr)!["id"].G();
        var email = $"u{Guid.NewGuid():N}@test.com";
        var created = await owner.Post("/users", new { name = roleNameAr + " " + email[..5], email, phone = "1", password = "Passw0rd!", role = "sales_rep", customRoleId = roleId });
        Ok(created, "user");
        return (await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!"), created.Data!["id"].G());
    }

    [Fact]
    public async Task Supervisor_records_and_submits_engineer_approves_or_returns_and_approved_is_locked()
    {
        var owner = await NewTenantAsync();
        Ok(await owner.Post("/construction-projects/role-templates"), "templates");
        var (supervisor, supervisorId) = await UserWithRoleAsync(owner, "مشرف الموقع");
        var (engineer, engineerId) = await UserWithRoleAsync(owner, "المهندس المشرف");
        var p = await ProjectAsync(owner);
        await ProjectAsync(owner); // مشروع آخر لا يصل إليه أحدهما

        // قبل الإسناد لا يصل المشرف للمشروع
        Assert.Equal(403, (await supervisor.Get($"/commercialcontracts/{p.Id}/site-reports/day?date={Day:yyyy-MM-dd}")).Status);
        Ok(await owner.Post($"/commercialcontracts/{p.Id}/project/members", new { userId = supervisorId, role = "site_supervisor" }), "member supervisor");
        Ok(await owner.Post($"/commercialcontracts/{p.Id}/project/members", new { userId = engineerId, role = "engineer" }), "member engineer");

        object Body(decimal actual) => new
        {
            reportDate = Day,
            hours = new[] { new { start = "07:00", end = "08:00", contractItemId = p.Concrete, targetQuantity = 10, actualQuantity = actual } },
        };
        var created = await supervisor.Post($"/commercialcontracts/{p.Id}/site-reports", Body(9));
        Ok(created, "supervisor records");
        var rid = created.Data!["id"].S();
        Assert.Equal("draft", created.Data["status"].S());

        // المشرف لا يعتمد، ولا يُعتمد ما لم يُقدَّم
        Assert.Equal(409, (await engineer.Post($"/commercialcontracts/{p.Id}/site-reports/{rid}/approve")).Status);
        Ok(await supervisor.Post($"/commercialcontracts/{p.Id}/site-reports/{rid}/submit"), "submit");
        Assert.Equal(403, (await supervisor.Post($"/commercialcontracts/{p.Id}/site-reports/{rid}/approve")).Status);
        // المقدَّم لا يُعدَّل
        Assert.Equal(409, (await supervisor.Put($"/commercialcontracts/{p.Id}/site-reports/{rid}", Body(10))).Status);

        // الإعادة بسبب إلزامي ثم التعديل وإعادة التقديم
        Assert.Equal(400, (await engineer.Post($"/commercialcontracts/{p.Id}/site-reports/{rid}/return", new { note = " " })).Status);
        var returned = await engineer.Post($"/commercialcontracts/{p.Id}/site-reports/{rid}/return", new { note = "راجع الكمية" });
        Ok(returned, "return");
        Assert.Equal("draft", returned.Data!["status"].S());
        Assert.Equal("راجع الكمية", returned.Data["returnNote"].S());
        Ok(await supervisor.Put($"/commercialcontracts/{p.Id}/site-reports/{rid}", Body(10)), "fix");
        Ok(await supervisor.Post($"/commercialcontracts/{p.Id}/site-reports/{rid}/submit"), "resubmit");

        var approved = await engineer.Post($"/commercialcontracts/{p.Id}/site-reports/{rid}/approve");
        Ok(approved, "approve");
        Assert.Equal("approved", approved.Data!["status"].S());
        Assert.False(string.IsNullOrEmpty(approved.Data["approvedBy"].S()));
        // المعتمد مقفل: لا تعديل ولا حذف ولا إعادة
        Assert.Equal(409, (await supervisor.Put($"/commercialcontracts/{p.Id}/site-reports/{rid}", Body(11))).Status);
        Assert.Equal(409, (await owner.Delete($"/commercialcontracts/{p.Id}/site-reports/{rid}")).Status);
        Assert.Equal(409, (await engineer.Post($"/commercialcontracts/{p.Id}/site-reports/{rid}/return", new { note = "x" })).Status);

        // سجل المراجعة: حالة التقرير قبل ← بعد
        var logs = (await owner.Get("/auditlogs?entityName=SiteDailyReport&pageSize=100")).Data!;
        var details = (logs["items"] ?? logs).AsArray().Select(r => r!["details"].S()).ToList();
        Assert.Contains(details, d => d.Contains("Status") && d.Contains("submitted") && d.Contains("approved"));
    }

    // ---------- تقارير الإنجاز ----------

    [Fact]
    public async Task Hourly_productivity_and_resource_reports_use_approved_reports_by_default_and_filter()
    {
        var api = await NewTenantAsync();
        var p = await ProjectAsync(api);
        var list = await AddResourceAsync(api, p.Id, new { kind = "owned_equipment", name = "حفار 1", rate = 160 });
        list = await AddResourceAsync(api, p.Id, new { kind = "labor", name = "عمال", rateBasis = "hour", rate = 25 });

        async Task<string> Report(DateTime date, decimal concrete, decimal steel)
        {
            var r = await api.Post($"/commercialcontracts/{p.Id}/site-reports", new
            {
                reportDate = date,
                hours = new object[]
                {
                    new { start = "07:00", end = "08:00", contractItemId = p.Concrete, targetQuantity = 10, actualQuantity = concrete },
                    new { start = "07:00", end = "08:00", contractItemId = p.Steel, targetQuantity = 1, actualQuantity = steel },
                },
                resources = new object[]
                {
                    new { projectResourceId = ResourceId(list, "حفار 1"), contractItemId = p.Concrete, hours = 1 },
                    new { projectResourceId = ResourceId(list, "عمال"), contractItemId = p.Steel, hours = 2 },
                },
            });
            Ok(r, "report " + date.ToString("MM-dd"));
            return r.Data!["id"].S();
        }
        var approvedId = await Report(Day, 8, 1);
        Ok(await api.Post($"/commercialcontracts/{p.Id}/site-reports/{approvedId}/submit"), "submit");
        Ok(await api.Post($"/commercialcontracts/{p.Id}/site-reports/{approvedId}/approve"), "approve");
        await Report(Day.AddDays(1), 10, 1); // مسودة

        var q = $"from={Day:yyyy-MM-dd}&to={Day.AddDays(5):yyyy-MM-dd}";
        var hourly = (await api.Get($"/commercialcontracts/{p.Id}/site-reports/hourly?{q}")).Data!.AsArray();
        Assert.Equal(2, hourly.Count);
        Assert.All(hourly, r => Assert.Equal("approved", r!["status"].S()));
        Assert.Equal(4, (await api.Get($"/commercialcontracts/{p.Id}/site-reports/hourly?{q}&includeUnapproved=true")).Data!.AsArray().Count);
        var concreteOnly = (await api.Get($"/commercialcontracts/{p.Id}/site-reports/hourly?{q}&contractItemId={p.Concrete}")).Data!.AsArray();
        Assert.Equal(80m, concreteOnly.Single()!["achievementPercent"].D());

        var productivity = (await api.Get($"/commercialcontracts/{p.Id}/site-reports/productivity?{q}&contractItemId={p.Concrete}")).Data!.AsArray().Single()!;
        Assert.Equal(8m, productivity["actual"].D());
        Assert.Equal(160m, productivity["cost"].D());
        Assert.Equal(20m, productivity["costPerUnit"].D());

        var resources = (await api.Get($"/commercialcontracts/{p.Id}/site-reports/resources?{q}&includeUnapproved=true&projectResourceId={ResourceId(list, "عمال")}")).Data!.AsArray();
        Assert.Equal(2, resources.Count);
        Assert.All(resources, r => Assert.Equal(50m, r!["cost"].D()));

        var listing = (await api.Get($"/commercialcontracts/{p.Id}/site-reports?{q}")).Data!.AsArray();
        Assert.Equal(2, listing.Count);

        // المورد المستخدم لا يُحذف (يُعطَّل)
        Assert.Equal(409, (await api.Delete($"/commercialcontracts/{p.Id}/project/resources/{ResourceId(list, "عمال")}")).Status);
    }

    [Fact]
    public async Task Daily_plan_validates_items_targets_and_duplicates()
    {
        var api = await NewTenantAsync();
        var p = await ProjectAsync(api);
        var bad = await api.Put($"/commercialcontracts/{p.Id}/project/daily-plans", new
        {
            plans = new object[]
            {
                new { contractItemId = p.Concrete, targetPerHour = 0, effectiveFrom = Day },
                new { contractItemId = Guid.NewGuid(), targetPerHour = 5, effectiveFrom = Day },
                new { contractItemId = p.Steel, targetPerHour = 1, effectiveFrom = Day },
                new { contractItemId = p.Steel, targetPerHour = 2, effectiveFrom = Day },
            },
        });
        Assert.Equal(400, bad.Status);
        Assert.Equal(3, bad.Body!["errors"]!.AsArray().Count);

        // خطة أحدث تسري من تاريخها: القالب قبلها وبعدها
        Ok(await api.Put($"/commercialcontracts/{p.Id}/project/daily-plans", new
        {
            plans = new[]
            {
                new { contractItemId = p.Concrete, targetPerHour = 10m, effectiveFrom = Day.AddDays(-5) },
                new { contractItemId = p.Concrete, targetPerHour = 12.5m, effectiveFrom = Day.AddDays(3) },
            },
        }), "plans");
        decimal Target(JsonNode d) => d["hours"]![0]!["targetQuantity"].D();
        Assert.Equal(10m, Target((await api.Get($"/commercialcontracts/{p.Id}/site-reports/day?date={Day:yyyy-MM-dd}")).Data!));
        Assert.Equal(12.5m, Target((await api.Get($"/commercialcontracts/{p.Id}/site-reports/day?date={Day.AddDays(3):yyyy-MM-dd}")).Data!));
    }
}
