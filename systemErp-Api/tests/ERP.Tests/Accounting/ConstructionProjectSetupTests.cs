using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// المرحلة 0 لمشاريع المقاولات (العقد هو المشروع): ملف المشروع ومركز تكلفته مع العقد، وفترات الإنجاز من ساعات العمل،
/// ومراحل التنفيذ ونسبة الإنجاز المخططة، والأعضاء ونطاق «المسندة إليه»، وأدوار المقاولات الجاهزة، وسجل المراجعة.
/// </summary>
[Collection("api")]
public class ConstructionProjectSetupTests : TestBase
{
    public ConstructionProjectSetupTests(ErpFactory f) : base(f) { }

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");

    /// <summary>عقد مقاولات بجدول كميات: بند عادي، وبند رئيسي بفرعين (فيصبح مجموعة).</summary>
    private static async Task<(string Id, JsonArray Items, JsonNode Contract)> BoqContractAsync(Client api, object? costCenterId = null)
    {
        var created = await api.Post("/commercialcontracts", new
        {
            title = "مشروع خرسانة", projectName = "برج الريان", contractType = "construction_boq", workflow = "simple", requiresGuarantee = false,
            partyName = "عميل", startDate = DateTime.UtcNow.Date, endDate = DateTime.UtcNow.Date.AddMonths(6), vatRate = 15, costCenterId,
            items = new object[]
            {
                new { description = "صب خرسانة", unit = "م3", quantity = 500, unitPrice = 300, vatRate = 15, lineKind = "measured" },
                new { description = "أعمال الحديد", unit = "طن", quantity = 1, unitPrice = 0, vatRate = 15, lineKind = "measured" },
                new { description = "حديد قواعد", unit = "طن", quantity = 20, unitPrice = 3000, vatRate = 15, lineKind = "measured", isSubItem = true },
                new { description = "حديد أعمدة", unit = "طن", quantity = 10, unitPrice = 3200, vatRate = 15, lineKind = "measured", isSubItem = true },
            },
            milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
        });
        Ok(created, "create boq contract");
        return (created.Data!["id"].S(), created.Data["items"]!.AsArray(), created.Data);
    }

    private static Task<Res> Project(Client api, string id) => api.Get($"/commercialcontracts/{id}/project");

    private static Guid ItemId(JsonArray items, string description) => items.First(i => i!["description"].S() == description)!["id"].G();

    // ---------- الإنشاء مع العقد ----------

    [Fact]
    public async Task Boq_contract_gets_project_profile_cost_center_and_hourly_slots_on_creation()
    {
        var api = await NewConstructionTenantAsync();
        var (id, _, contract) = await BoqContractAsync(api);

        var project = await Project(api, id);
        Ok(project, "get project");
        var p = project.Data!;
        Assert.Equal("07:00", p["workdayStart"].S());
        Assert.Equal("17:00", p["workdayEnd"].S());
        Assert.Equal(10, p["hourSlots"]!.AsArray().Count);
        Assert.Equal("07:00", p["hourSlots"]![0]!["start"].S());
        Assert.Equal("08:00", p["hourSlots"]![0]!["end"].S());

        // مركز التكلفة أُنشئ باسم المشروع وربط بالعقد
        Assert.Equal($"PRJ-{contract["contractNumber"].S()}", p["costCenterCode"].S());
        Assert.Equal("برج الريان", p["costCenterName"].S());
        var saved = (await api.Get($"/commercialcontracts/{id}")).Data!;
        Assert.Equal(p["costCenterId"].S(), saved["costCenterId"].S());
    }

    [Fact]
    public async Task Chosen_cost_center_is_kept_and_non_boq_contracts_have_no_project()
    {
        var api = await NewConstructionTenantAsync();
        var center = await api.Post("/costcenters", new { code = "CC-SITE", nameAr = "موقع", nameEn = "Site" });
        Ok(center, "cost center");
        var (id, _, _) = await BoqContractAsync(api, center.Data!["id"].S());
        Assert.Equal("CC-SITE", (await Project(api, id)).Data!["costCenterCode"].S());

        var supply = await api.Post("/commercialcontracts", new
        {
            title = "توريد", contractType = "supply_materials", partyName = "عميل", contractValue = 1000, vatRate = 0,
            startDate = DateTime.UtcNow.Date, endDate = DateTime.UtcNow.Date.AddMonths(1),
            items = new[] { new { description = "صنف", unit = "PCS", quantity = 1, unitPrice = 1000, vatRate = 0 } },
            milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
        });
        Ok(supply, "supply contract");
        Assert.Equal(409, (await Project(api, supply.Data!["id"].S())).Status);
        Assert.True(string.IsNullOrEmpty(supply.Data["costCenterId"]?.ToString()));
    }

    // ---------- الملف وساعات العمل ----------

    [Fact]
    public async Task Profile_hours_generate_hourly_slots_excluding_break_and_are_validated()
    {
        var api = await NewConstructionTenantAsync();
        var (id, _, _) = await BoqContractAsync(api);

        var saved = await api.Put($"/commercialcontracts/{id}/project/profile", new
        {
            estimatedBudget = 120_000, workdayStart = "06:30", workdayEnd = "15:00", breakStart = "12:00", breakEnd = "13:00", notes = "دوام صيفي",
        });
        Ok(saved, "save profile");
        var p = saved.Data!;
        Assert.Equal(120_000m, p["estimatedBudget"].D());
        var slots = p["hourSlots"]!.AsArray().Select(s => $"{s!["start"].S()}-{s["end"].S()}").ToList();
        // 06:30→12:00 (خمس ساعات ونصف: الأخيرة نصف ساعة)، ثم 13:00→15:00
        Assert.Equal(new[] { "06:30-07:30", "07:30-08:30", "08:30-09:30", "09:30-10:30", "10:30-11:30", "11:30-12:00", "13:00-14:00", "14:00-15:00" }, slots);
        Assert.Equal(0.5m, p["hourSlots"]![5]!["hours"].D());

        Assert.Equal(400, (await api.Put($"/commercialcontracts/{id}/project/profile", new { workdayStart = "17:00", workdayEnd = "07:00" })).Status);
        Assert.Equal(400, (await api.Put($"/commercialcontracts/{id}/project/profile", new { workdayStart = "7am", workdayEnd = "17:00" })).Status);
        Assert.Equal(400, (await api.Put($"/commercialcontracts/{id}/project/profile", new { workdayStart = "07:00", workdayEnd = "17:00", breakStart = "12:00" })).Status);
        Assert.Equal(400, (await api.Put($"/commercialcontracts/{id}/project/profile", new { workdayStart = "07:00", workdayEnd = "17:00", breakStart = "18:00", breakEnd = "19:00" })).Status);
        Assert.Equal(400, (await api.Put($"/commercialcontracts/{id}/project/profile", new { workdayStart = "07:00", workdayEnd = "17:00", estimatedBudget = -1 })).Status);
        // الرفض لا يغيّر المحفوظ
        Assert.Equal("06:30", (await Project(api, id)).Data!["workdayStart"].S());
    }

    // ---------- المراحل ونسبة الإنجاز المخططة ----------

    [Fact]
    public async Task Phases_compute_planned_progress_and_reject_invalid_weights_and_items()
    {
        var api = await NewConstructionTenantAsync();
        var (id, items, _) = await BoqContractAsync(api);
        var concrete = ItemId(items, "صب خرسانة");
        var group = ItemId(items, "أعمال الحديد");
        var footing = ItemId(items, "حديد قواعد");
        var columns = ItemId(items, "حديد أعمدة");
        // اليوم بتوقيت السعودية (كما يحسبه الخادم)
        var today = DateTime.UtcNow.AddHours(3).Date;

        // مرحلة منتهية (وزن 40 → 40)، ومرحلة 10 أيام مضى نصفها (وزن 60 → 30): المخطط حتى اليوم 70%
        var phases = new object[]
        {
            new { name = "الحفر والقواعد", plannedStart = today.AddDays(-20), plannedEnd = today.AddDays(-10), weightPercent = 40, contractItemIds = new[] { footing } },
            new { name = "الهيكل", plannedStart = today.AddDays(-4), plannedEnd = today.AddDays(5), weightPercent = 60, contractItemIds = new[] { concrete, columns } },
        };
        var saved = await api.Put($"/commercialcontracts/{id}/project/phases", new { phases });
        Ok(saved, "save phases");
        var p = saved.Data!;
        Assert.Equal(100m, p["phaseWeightTotal"].D());
        Assert.Equal(70m, p["plannedProgressPercent"].D());
        Assert.Equal(100m, p["phases"]![0]!["plannedCompletionPercent"].D());
        Assert.Equal(50m, p["phases"]![1]!["plannedCompletionPercent"].D());
        Assert.Equal(2, p["phases"]![1]!["contractItemIds"]!.AsArray().Count);

        // التعديل بالمعرّف يُبقي المرحلة، والغائبة تُحذف
        var keepId = p["phases"]![1]!["id"].S();
        var updated = await api.Put($"/commercialcontracts/{id}/project/phases", new
        {
            phases = new[] { new { id = keepId, name = "الهيكل الخرساني", plannedStart = today.AddDays(-4), plannedEnd = today.AddDays(5), weightPercent = 60, contractItemIds = new[] { concrete } } },
        });
        Ok(updated, "update phases");
        Assert.Single(updated.Data!["phases"]!.AsArray());
        Assert.Equal(keepId, updated.Data["phases"]![0]!["id"].S());
        Assert.Equal("الهيكل الخرساني", updated.Data["phases"]![0]!["name"].S());
        Assert.Equal(30m, updated.Data["plannedProgressPercent"].D());

        object Phase(string name, decimal weight, params Guid[] ids) => new { name, plannedStart = today, plannedEnd = today.AddDays(3), weightPercent = weight, contractItemIds = ids };
        async Task Rejected(string why, params object[] list)
            => Assert.True((await api.Put($"/commercialcontracts/{id}/project/phases", new { phases = list })).Status == 400, why);

        await Rejected("weights above 100", Phase("أ", 70), Phase("ب", 40));
        await Rejected("zero weight", Phase("أ", 0));
        await Rejected("item in two phases", Phase("أ", 30, concrete), Phase("ب", 30, concrete));
        await Rejected("group line", Phase("أ", 30, group));
        await Rejected("foreign item", Phase("أ", 30, Guid.NewGuid()));
        await Rejected("end before start", new { name = "أ", plannedStart = today, plannedEnd = today.AddDays(-1), weightPercent = 10, contractItemIds = Array.Empty<Guid>() });
        await Rejected("no name", Phase(" ", 10));
        Assert.Equal(30m, (await Project(api, id)).Data!["plannedProgressPercent"].D());
    }

    // ---------- الأعضاء والنطاق والأدوار الجاهزة ----------

    private async Task<(Client Client, Guid Id)> UserWithRoleAsync(Client owner, string roleNameAr)
    {
        var roles = (await owner.Get("/roles")).Data!.AsArray();
        var roleId = roles.First(r => r!["nameAr"].S() == roleNameAr)!["id"].G();
        var email = $"u{Guid.NewGuid():N}@test.com";
        var created = await owner.Post("/users", new { name = "مشرف " + email[..6], email, phone = "1", password = "Passw0rd!", role = "sales_rep", customRoleId = roleId });
        Ok(created, "create user");
        return (await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!"), created.Data!["id"].G());
    }

    private static async Task<List<string>> ContractIdsAsync(Client api)
        => (await api.Get("/commercialcontracts")).Data!["items"]!.AsArray().Select(c => c!["id"].S()).ToList();

    [Fact]
    public async Task Role_templates_are_created_once_with_their_permissions()
    {
        var owner = await NewConstructionTenantAsync();
        var first = await owner.Post("/construction-projects/role-templates");
        Ok(first, "templates");
        Assert.Equal(8, first.Data!["created"]!.AsArray().Count);
        var again = (await owner.Post("/construction-projects/role-templates")).Data!;
        Assert.Empty(again["created"]!.AsArray());
        Assert.Equal(8, again["existing"]!.AsArray().Count);

        var roles = (await owner.Get("/roles")).Data!.AsArray();
        var pm = roles.First(r => r!["nameAr"].S() == "مدير المشروع")!["code"].S();
        var screens = (await owner.Get($"/permissions?roleId={pm}")).Data!.AsArray();
        var projects = screens.First(s => s!["screenId"].S() == "construction-projects")!;
        var actions = projects["actions"]!.AsArray().Select(a => a.S()).ToList();
        Assert.Contains("view", actions);
        Assert.Contains("edit", actions);
        var settings = (await owner.Get($"/permissions/settings?roleId={pm}")).Data!;
        Assert.Equal("assigned", settings["scope"].S(), ignoreCase: true);
        // الإدارة العليا نطاقها الكل
        var exec = roles.First(r => r!["nameAr"].S() == "الإدارة العليا")!["code"].S();
        Assert.Equal("all", (await owner.Get($"/permissions/settings?roleId={exec}")).Data!["scope"].S(), ignoreCase: true);
    }

    [Fact]
    public async Task Supervisor_sees_only_projects_assigned_through_membership_and_management_sees_all()
    {
        var owner = await NewConstructionTenantAsync();
        Ok(await owner.Post("/construction-projects/role-templates"), "templates");
        var (supervisor, supervisorId) = await UserWithRoleAsync(owner, "المهندس المشرف");
        var (executive, _) = await UserWithRoleAsync(owner, "الإدارة العليا");
        var (mine, _, _) = await BoqContractAsync(owner);
        var (other, _, _) = await BoqContractAsync(owner);

        // قبل الإسناد: لا يرى أياً منهما
        var before = await ContractIdsAsync(supervisor);
        Assert.DoesNotContain(mine, before);
        Assert.Equal(403, (await Project(supervisor, mine)).Status);

        var added = await owner.Post($"/commercialcontracts/{mine}/project/members", new { userId = supervisorId, role = "engineer" });
        Ok(added, "add member");
        var member = added.Data!["members"]!.AsArray().Single()!;
        Assert.Equal("engineer", member["role"].S());

        var after = await ContractIdsAsync(supervisor);
        Assert.Contains(mine, after);
        Assert.DoesNotContain(other, after);
        Ok(await Project(supervisor, mine), "member reads project");
        Ok(await supervisor.Get($"/commercialcontracts/{mine}"), "member reads contract");
        Assert.Equal(403, (await Project(supervisor, other)).Status);
        Assert.Equal(403, (await supervisor.Get($"/commercialcontracts/{other}")).Status);
        // المهندس المشرف (قالبه) يعرض الإعداد ولا يعدّله
        Assert.Equal(403, (await supervisor.Put($"/commercialcontracts/{mine}/project/profile", new { workdayStart = "07:00", workdayEnd = "15:00" })).Status);

        // الإدارة العليا (نطاق الكل) ترى المشروعين بلا عضوية
        var all = await ContractIdsAsync(executive);
        Assert.Contains(mine, all);
        Assert.Contains(other, all);

        // التكرار والدور غير المعروف والمستخدم غير الموجود
        Assert.Equal(409, (await owner.Post($"/commercialcontracts/{mine}/project/members", new { userId = supervisorId, role = "site_supervisor" })).Status);
        Assert.Equal(400, (await owner.Post($"/commercialcontracts/{other}/project/members", new { userId = supervisorId, role = "boss" })).Status);
        Assert.Equal(400, (await owner.Post($"/commercialcontracts/{other}/project/members", new { userId = Guid.NewGuid(), role = "engineer" })).Status);

        // تغيير الدور ثم الإزالة تُخرج المشروع من نطاقه
        var memberId = member["id"].S();
        var changed = await owner.Put($"/commercialcontracts/{mine}/project/members/{memberId}", new { userId = supervisorId, role = "site_supervisor" });
        Ok(changed, "change role");
        Assert.Equal("site_supervisor", changed.Data!["members"]![0]!["role"].S());
        Ok(await owner.Delete($"/commercialcontracts/{mine}/project/members/{memberId}"), "remove member");
        Assert.DoesNotContain(mine, await ContractIdsAsync(supervisor));
        Assert.Equal(403, (await Project(supervisor, mine)).Status);
    }

    [Fact]
    public async Task Project_setup_changes_are_audited_with_old_and_new_values()
    {
        var api = await NewConstructionTenantAsync();
        var (id, _, _) = await BoqContractAsync(api);
        Ok(await api.Put($"/commercialcontracts/{id}/project/profile", new { estimatedBudget = 1000, workdayStart = "07:00", workdayEnd = "17:00" }), "first");
        Ok(await api.Put($"/commercialcontracts/{id}/project/profile", new { estimatedBudget = 2500, workdayStart = "07:00", workdayEnd = "16:00" }), "second");

        var logs = (await api.Get("/auditlogs?entityName=ConstructionProjectProfile&pageSize=50")).Data!;
        var rows = (logs["items"] ?? logs).AsArray();
        var update = rows.Select(r => r!["details"].S()).FirstOrDefault(d => d.Contains("EstimatedBudget"));
        Assert.NotNull(update);
        Assert.Contains("1000", update);
        Assert.Contains("2500", update);
        Assert.Contains("WorkdayEndMinutes", update);
    }
    // ---------- قائمة المشاريع وملخصها ----------

    [Fact]
    public async Task Project_list_and_summary_report_progress_statuses_and_alerts_within_access_scope()
    {
        var owner = await NewConstructionTenantAsync();
        Ok(await owner.Post("/construction-projects/role-templates"), "templates");
        var (supervisor, supervisorId) = await UserWithRoleAsync(owner, "المهندس المشرف");
        var (mine, items, _) = await BoqContractAsync(owner);
        var (other, _, _) = await BoqContractAsync(owner);
        var today = DateTime.UtcNow.AddHours(3).Date;

        // مرحلة مضى نصفها بوزن 100 → مخطط 50%، ولا مستخلص معتمد → متأخر عن المخطط
        Ok(await owner.Put($"/commercialcontracts/{mine}/project/phases", new
        {
            phases = new[] { new { name = "الهيكل", plannedStart = today.AddDays(-4), plannedEnd = today.AddDays(5), weightPercent = 100, contractItemIds = new[] { ItemId(items, "صب خرسانة") } } },
        }), "phases");
        Ok(await owner.Put($"/commercialcontracts/{mine}/project/profile", new { estimatedBudget = 1000, workdayStart = "07:00", workdayEnd = "17:00" }), "budget");
        Ok(await owner.Post($"/commercialcontracts/{mine}/project/members", new { userId = supervisorId, role = "site_supervisor" }), "member");

        var summary = await owner.Get($"/commercialcontracts/{mine}/project/summary");
        Ok(summary, "summary");
        var s = summary.Data!;
        Assert.Equal(50m, s["plannedProgressPercent"].D());
        Assert.Equal(1, s["phaseCount"]!.GetValue<int>());
        Assert.Equal(0m, s["certifiedProgressPercent"].D());
        Assert.Equal(1000m, s["estimatedBudget"].D());
        Assert.Null(s["materialPlanStatus"]?.GetValue<string?>());
        var alerts = s["alerts"]!.AsArray().Select(a => a.S()).ToList();
        Assert.Contains("behind_plan", alerts);
        Assert.Contains("materials_not_approved", alerts);
        Assert.DoesNotContain("no_phases", alerts);
        Assert.Equal("site_supervisor", s["team"]!.AsArray().Single()!["role"].S());

        // القائمة: المالك يرى المشروعين، والمشرف مشروعه فقط (نطاق العضوية)
        var list = await owner.Get("/construction-projects");
        Ok(list, "list");
        var ids = list.Data!.AsArray().Select(p => p!["contractId"].S()).ToList();
        Assert.Contains(mine, ids);
        Assert.Contains(other, ids);
        Assert.Contains("no_phases", list.Data!.AsArray().Single(p => p!["contractId"].S() == other)!["alerts"]!.AsArray().Select(a => a.S()));
        var supervised = await supervisor.Get("/construction-projects");
        Ok(supervised, "supervisor list");
        Assert.Equal(new[] { mine }, supervised.Data!.AsArray().Select(p => p!["contractId"].S()).ToArray());
        Assert.Equal(403, (await supervisor.Get($"/commercialcontracts/{other}/project/summary")).Status);
    }
}
