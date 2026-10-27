using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// مركز الصلاحيات: الأدوار المخصّصة ونسخها وتعطيلها، الإجراءات المستقلة عن CRUD، نطاق الوصول، حد الاعتماد،
/// ربط مراحل العقد بالإجراءات، الصلاحيات الفعلية ومصدرها، فحص الصلاحية، وسجل التغييرات.
/// </summary>
[Collection("api")]
public class PermissionCenterTests : TestBase
{
    public PermissionCenterTests(ErpFactory f) : base(f) { }

    private const string Contracts = "commercial-contracts";

    private async Task<string> NewRoleAsync(Client owner, string name)
    {
        var r = await owner.Post("/roles", new { nameAr = name });
        Assert.Equal(200, r.Status);
        return r.Data!["code"].S();
    }

    /// <summary>صلاحيات دور بالإجراءات (الشاشات غير المذكورة بلا صلاحيات للدور المخصّص).</summary>
    private static async Task SaveRoleAsync(Client owner, string role, Dictionary<string, string[]> screens, object? settings = null)
    {
        var r = await owner.Post("/permissions", new
        {
            roleId = role, settings,
            permissions = screens.Select(s => new { screenId = s.Key, screenNameAr = s.Key, screenNameEn = s.Key, actions = s.Value }).ToList(),
        });
        Assert.Equal(200, r.Status);
    }

    private async Task<(Client Client, Guid Id)> UserWithRoleAsync(Client owner, string roleCode, string? department = null)
    {
        var roles = (await owner.Get("/roles")).Data!.AsArray();
        var roleId = roles.First(r => r!["code"].S() == roleCode)!["id"].G();
        var email = $"u{Guid.NewGuid():N}@test.com";
        var created = await owner.Post("/users", new { name = "موظف " + email[..6], email, phone = "1", password = "Passw0rd!", role = "sales_rep", customRoleId = roleId, department });
        Assert.Equal(200, created.Status);
        var client = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");
        return (client, created.Data!["id"].G());
    }

    /// <summary>عقد توريد (مسار كامل) ببيانات مكتملة للإرسال للمراجعة؛ الطرف يُنشئه المالك لأن الأدوار المخصّصة بلا بيانات رئيسية.</summary>
    private static async Task<string> NewContractAsync(Client api, Guid partyId, decimal value = 1000)
    {
        var r = await api.Post("/commercialcontracts", new
        {
            title = "عقد", contractType = "supply_materials", partyName = "عميل", partyId, contractValue = value, vatRate = 0,
            startDate = DateTime.UtcNow.Date, endDate = DateTime.UtcNow.Date.AddMonths(6),
            items = new[] { new { description = "بند", unit = "PCS", quantity = 1, unitPrice = value, vatRate = 0 } },
            milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
        });
        Assert.Equal(201, r.Status);
        Assert.Equal("draft", r.Data!["stage"].S());
        return r.Data!["id"].S();
    }

    [Fact]
    public async Task Catalog_exposes_modules_actions_routes_and_protected_endpoints()
    {
        var owner = await NewTenantAsync();
        var catalog = (await owner.Get("/permissions/catalog")).Data!;
        var screens = catalog["modules"]!.AsArray().SelectMany(m => m!["screens"]!.AsArray()).ToList();
        var contracts = screens.First(s => s!["id"].S() == Contracts)!;
        var actions = contracts["actions"]!.AsArray().Select(a => a.S()).ToList();
        Assert.Contains("submit", actions);
        Assert.Contains("legal_review", actions);
        Assert.Contains("financial_review", actions);
        Assert.Contains("inspect", actions);
        Assert.Contains(contracts["endpoints"]!.AsArray(), e => e!["route"].S().Contains("commercialcontracts") && e["route"].S().EndsWith("/move"));
        Assert.Contains(contracts["endpoints"]!.AsArray(), e => e!["route"].S().Contains("/bill") && e["action"].S() == "post");
        Assert.Contains(catalog["actions"]!.AsArray(), a => a!["code"].S() == "export" && a["group"].S() == "reports");
    }

    [Fact]
    public async Task Custom_role_actions_are_enforced_independently_of_crud()
    {
        var owner = await NewTenantAsync();
        var role = await NewRoleAsync(owner, "محرر عقود");
        await SaveRoleAsync(owner, role, new() { [Contracts] = new[] { "view", "create", "edit", "submit" } });
        var (user, _) = await UserWithRoleAsync(owner, role);
        var (party, _) = await SeedCustomerAsync(owner);

        Assert.Equal(403, (await user.Get("/accounts")).Status); // لا شاشة لم تُمنح
        var id = await NewContractAsync(user, party);
        Assert.Equal(200, (await user.Post($"/commercialcontracts/{id}/move", new { toStatus = "review" })).Status); // submit
        var review = await user.Post($"/commercialcontracts/{id}/move", new { toStatus = "approval" });
        Assert.Equal(403, review.Status); // المراجعة إجراء مستقل لم يُمنح
        Assert.Contains("مراجعة", review.Body!["message"].S());
    }

    [Fact]
    public async Task Contract_workflow_reviews_states_and_approval_limit()
    {
        var owner = await NewTenantAsync();
        var role = await NewRoleAsync(owner, "معتمد عقود");
        await SaveRoleAsync(owner, role, new() { [Contracts] = new[] { "view", "create", "edit", "delete", "submit", "legal_review", "financial_review", "approve" } },
            new { scope = "all", approvalLimit = 500 });
        var (user, userId) = await UserWithRoleAsync(owner, role);
        var (party, _) = await SeedCustomerAsync(owner);

        var id = await NewContractAsync(user, party, 1000);
        // لا انتقال مسودة ← مسودة
        Assert.Equal(409, (await user.Post($"/commercialcontracts/{id}/reject", new { reason = "x" })).Status);
        Assert.Equal(200, (await user.Post($"/commercialcontracts/{id}/move", new { toStatus = "review" })).Status);
        // في المراجعة: الحذف غير مسموح، والإعادة تتطلب سبباً وتعيد للمسودة بجولة مراجعة جديدة
        Assert.Equal(409, (await user.Delete($"/commercialcontracts/{id}")).Status);
        Assert.Equal(400, (await user.Post($"/commercialcontracts/{id}/review", new { kind = "legal", decision = "return" })).Status);
        var returned = await user.Post($"/commercialcontracts/{id}/review", new { kind = "legal", decision = "return", note = "نواقص" });
        Assert.Equal(200, returned.Status);
        Assert.Equal("draft", returned.Data!["status"].S());
        Assert.Equal(2, returned.Data["reviewRound"]!.GetValue<int>());

        // موافقة الاثنين تنقل للاعتماد تلقائياً
        Assert.Equal(200, (await user.Post($"/commercialcontracts/{id}/move", new { toStatus = "review" })).Status);
        var legal = await user.Post($"/commercialcontracts/{id}/review", new { kind = "legal", decision = "approve" });
        Assert.Equal("review", legal.Data!["status"].S());
        Assert.Equal(409, (await user.Post($"/commercialcontracts/{id}/review", new { kind = "legal", decision = "approve" })).Status);
        var financial = await user.Post($"/commercialcontracts/{id}/review", new { kind = "financial", decision = "approve" });
        Assert.Equal("approval", financial.Data!["status"].S());

        var approve = await user.Post($"/commercialcontracts/{id}/move", new { toStatus = "active" });
        Assert.Equal(403, approve.Status); // 1000 > حد 500
        Assert.Contains("500", approve.Body!["message"].S());

        var check = (await owner.Post("/permissions/check", new { userId, screenId = Contracts, action = "approve", recordType = "commercial-contract", recordId = id })).Data!;
        Assert.False(check["allowed"]!.GetValue<bool>());
        Assert.Equal("exceeds_limit", check["reasonCode"].S());
        var smaller = (await owner.Post("/permissions/check", new { userId, screenId = Contracts, action = "approve", recordType = "commercial-contract", recordId = id, amount = 400 })).Data!;
        Assert.True(smaller["allowed"]!.GetValue<bool>());
        var inspect = (await owner.Post("/permissions/check", new { userId, screenId = Contracts, action = "inspect", recordType = "commercial-contract", recordId = id })).Data!;
        Assert.Equal("no_permission", inspect["reasonCode"].S());
    }

    [Fact]
    public async Task Simplified_contract_activation_is_bound_by_the_approval_limit()
    {
        var owner = await NewTenantAsync();
        var role = await NewRoleAsync(owner, "معتمد صيانة");
        await SaveRoleAsync(owner, role, new() { [Contracts] = new[] { "view", "create", "edit", "approve" } }, new { scope = "all", approvalLimit = 500 });
        var (user, _) = await UserWithRoleAsync(owner, role);
        var (party, _) = await SeedCustomerAsync(owner);

        async Task<string> Simple(decimal value)
        {
            var r = await user.Post("/commercialcontracts", new
            {
                title = "صيانة", contractType = "monthly_maintenance", partyName = "عميل", partyId = party, contractValue = value, vatRate = 0,
                startDate = DateTime.UtcNow.Date, endDate = DateTime.UtcNow.Date.AddMonths(6),
                items = Array.Empty<object>(), milestones = new[] { new { title = "دفعة", percentage = 100 } }, clauses = Array.Empty<object>(),
            });
            Assert.Equal("draft", r.Data!["stage"].S()); // الإنشاء لا يتطلب الاعتماد: الحالة مسودة دائماً
            return r.Data["id"].S();
        }

        Assert.Equal(403, (await user.Post($"/commercialcontracts/{await Simple(1000)}/move", new { toStatus = "active" })).Status); // 1000 > 500
        Assert.Equal(200, (await user.Post($"/commercialcontracts/{await Simple(400)}/move", new { toStatus = "active" })).Status);
    }

    [Fact]
    public async Task Own_scope_hides_and_blocks_records_created_by_others()
    {
        var owner = await NewTenantAsync();
        var role = await NewRoleAsync(owner, "مندوب عقود");
        await SaveRoleAsync(owner, role, new() { [Contracts] = new[] { "view", "create", "edit" } }, new { scope = "own" });
        var (a, aId) = await UserWithRoleAsync(owner, role);
        var (b, _) = await UserWithRoleAsync(owner, role);
        var (party, _) = await SeedCustomerAsync(owner);

        var mine = await NewContractAsync(a, party);
        var theirs = await NewContractAsync(b, party);
        var list = (await a.Get("/commercialcontracts")).Data!["items"]!.AsArray().Select(c => c!["id"].S()).ToList();
        Assert.Contains(mine, list);
        Assert.DoesNotContain(theirs, list);
        Assert.Equal(403, (await a.Get($"/commercialcontracts/{theirs}")).Status);
        Assert.Equal(403, (await a.Put($"/commercialcontracts/{theirs}", new { title = "x", contractType = "maintenance", partyName = "x" })).Status);

        // الإسناد يُدخل السجل في نطاق «المسندة إليه» فقط، لا «التي أنشأها»
        Assert.Equal(200, (await owner.Post("/permissions/records/assign", new { recordType = "commercial-contract", recordId = theirs, userId = aId })).Status);
        Assert.Equal(403, (await a.Get($"/commercialcontracts/{theirs}")).Status);
        var check = (await owner.Post("/permissions/check", new { userId = aId, screenId = Contracts, action = "view", recordType = "commercial-contract", recordId = theirs })).Data!;
        Assert.Equal("out_of_scope", check["reasonCode"].S());

        // المالك يرى الكل
        var all = (await owner.Get("/commercialcontracts")).Data!["items"]!.AsArray().Select(c => c!["id"].S()).ToList();
        Assert.Contains(theirs, all);
    }

    [Fact]
    public async Task Copy_disable_and_effective_permissions_with_source()
    {
        var owner = await NewTenantAsync();
        var copy = await owner.Post("/roles/sales_rep/copy", new { nameAr = "مندوب 2" });
        Assert.Equal(200, copy.Status);
        var code = copy.Data!["code"].S();
        var salesRep = (await owner.Get("/roles/sales_rep")).Data!;
        Assert.Equal(salesRep["permissionsCount"]!.GetValue<int>(), copy.Data["permissionsCount"]!.GetValue<int>());

        var (user, userId) = await UserWithRoleAsync(owner, code);
        Assert.Equal(200, (await user.Get("/customers")).Status);

        // صلاحية مباشرة للمستخدم تتفوق على دوره، ومصدرها «direct»
        var current = (await owner.Get($"/permissions?userId={userId}")).Data!.AsArray()
            .Where(p => p!["screenId"].S() != "platform-admin")
            .Select(p => new
            {
                screenId = p!["screenId"].S(), screenNameAr = "", screenNameEn = "",
                actions = p["actions"]!.AsArray().Select(a => a.S()).Concat(p["screenId"].S() == "reports" ? new[] { "view" } : Array.Empty<string>()).ToList(),
            }).ToList();
        Assert.Equal(200, (await owner.Post("/permissions", new { userId, permissions = current })).Status);
        Assert.Equal(200, (await user.Get("/reports/TrialBalance")).Status);

        var effective = (await owner.Get($"/permissions/effective/{userId}")).Data!;
        Assert.True(effective["hasDirect"]!.GetValue<bool>());
        var reportsView = effective["screens"]!.AsArray().First(s => s!["screenId"].S() == "reports")!["actions"]!.AsArray().First(a => a!["action"].S() == "view")!;
        Assert.Equal("direct", reportsView["source"].S());
        var customersView = effective["screens"]!.AsArray().First(s => s!["screenId"].S() == "master-data")!["actions"]!.AsArray().First(a => a!["action"].S() == "view")!;
        Assert.Equal("role", customersView["source"].S());

        // التعطيل يحجب كل الشاشات عن مستخدمي الدور، والدور المعطّل لا يُسند لمستخدم جديد
        Assert.Equal(200, (await owner.Delete($"/permissions/users/{userId}")).Status);
        Assert.Equal(200, (await owner.Post($"/roles/{code}/deactivate")).Status);
        Assert.Equal(403, (await user.Get("/customers")).Status);
        var roleId = copy.Data["id"].G();
        Assert.Equal(409, (await owner.Post("/users", new { name = "x", email = $"x{Guid.NewGuid():N}@t.com", phone = "1", password = "Passw0rd!", role = "sales_rep", customRoleId = roleId })).Status);
        Assert.Equal(409, (await owner.Post("/roles/owner/deactivate")).Status);
    }

    [Fact]
    public async Task Permission_changes_are_audited_with_old_and_new_values()
    {
        var owner = await NewTenantAsync();
        var role = await NewRoleAsync(owner, "مراجع");
        await SaveRoleAsync(owner, role, new() { ["accounts"] = new[] { "view" } }, new { scope = "all", approvalLimit = 50000 });
        await SaveRoleAsync(owner, role, new() { ["accounts"] = new[] { "view", "post" } }, new { scope = "all", approvalLimit = 250000 });

        var log = (await owner.Get("/permissions/changes?pageSize=50")).Data!["items"]!.AsArray();
        var post = log.First(l => l!["permission"].S() == "accounts.post" && l["targetId"].S() == role)!;
        Assert.Equal("denied", post["oldValue"].S());
        Assert.Equal("granted", post["newValue"].S());
        Assert.False(string.IsNullOrEmpty(post["changedByName"].S()));
        var limit = log.First(l => l!["permission"].S() == "approval_limit" && l["newValue"].S() == "250000")!;
        Assert.Equal("50000", limit["oldValue"].S());
        Assert.Contains(log, l => l!["permission"].S() == "role.create" && l["targetId"].S() == role);
    }

    [Fact]
    public async Task Legacy_saved_permissions_keep_old_endpoint_behaviour()
    {
        var owner = await NewTenantAsync();
        var email = $"s{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await owner.Post("/users", new { name = "مندوب", email, phone = "1", password = "Passw0rd!", role = "sales_rep" })).Status);
        var rep = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");

        // المندوب الافتراضي: تعديل المبيعات نعم، الاعتماد لا ← العقود (ترث المبيعات) تُرسل للمراجعة ولا تُراجع
        var (party, _) = await SeedCustomerAsync(owner);
        var id = await NewContractAsync(rep, party);
        Assert.Equal(200, (await rep.Post($"/commercialcontracts/{id}/AdvanceStage", new { stage = "review" })).Status);
        Assert.Equal(403, (await rep.Post($"/commercialcontracts/{id}/AdvanceStage", new { stage = "approval" })).Status);

        var mine = (await rep.Get("/permissions/me")).Data!.AsArray();
        var sales = mine.First(p => p!["screenId"].S() == "sales")!;
        var actions = sales["actions"]!.AsArray().Select(a => a.S()).ToList();
        Assert.Contains("post", actions);       // مشتق من التعديل
        Assert.DoesNotContain("discount", actions); // مشتق من الاعتماد
        Assert.Contains("print", actions);      // مشتق من العرض
    }
}
