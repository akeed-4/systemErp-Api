using ERP.Tests.Infrastructure;
using Xunit;

namespace ERP.Tests.Shared;

/// <summary>
/// المرحلة 1 من مركز الاعتماد (تقوية محرك الاعتماد): منع اعتماد الطلب الذاتي حتى للمالك، ورؤية الطلبات لمن يخصّه،
/// واعتماد مدير المركز للمستوى الحالي وحده، ورفض قرار على مستوى تغيّر، والإعادة للتعديل، والتزامن، والمهل، وسجل التدقيق.
/// </summary>
[Collection("api")]
public class ApprovalEngineHardeningTests : TestBase
{
    public ApprovalEngineHardeningTests(ErpFactory f) : base(f) { }

    private async Task<Client> UserAsync(Client owner, string role)
    {
        var email = $"u{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await owner.Post("/users", new { name = $"مستخدم {role}", email, phone = "1", password = "Passw0rd!", role })).Status);
        var user = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");
        Assert.NotNull(user.Token);
        return user;
    }

    /// <summary>سياسة على طلبات المبيعات (فحص من الواجهة) بمستويات أدوار مرتّبة، ومهلة اختيارية.</summary>
    private static async Task PolicyAsync(Client owner, int? slaHours, params string[] roles)
        => Assert.Equal(201, (await owner.Post("/ApprovalPolicies", new
        {
            nameAr = "اعتماد المبيعات", documentTypes = new[] { "sales" }, actionType = "create", minAmountTrigger = 1, isActive = true,
            steps = roles.Select((r, i) => new { level = i + 1, approverRole = r, titleAr = r, slaHours }).ToArray(),
        })).Status);

    /// <summary>طلب اعتماد على مستند مبيعات افتراضي (المقدّم هو العميل المرسِل).</summary>
    private static async Task<string> RequestAsync(Client requester, decimal amount = 5000)
    {
        var check = await requester.Post("/approvals/check", new
        {
            documentType = "sales", actionType = "create", documentId = Guid.NewGuid(), documentNumber = $"S-{Guid.NewGuid():N}"[..10], documentAmount = amount,
        });
        Assert.True(check.Data!["approvalRequired"]!.GetValue<bool>());
        return check.Data["request"]!["id"].S();
    }

    private static async Task<List<string>> VisibleIdsAsync(Client api)
        => (await api.Get("/approvals?pageSize=200")).Data!["items"]!.AsArray().Select(r => r!["id"].S()).ToList();

    [Fact]
    public async Task Nobody_including_the_owner_approves_their_own_request()
    {
        var owner = await NewTenantAsync();
        await PolicyAsync(owner, null, "owner");
        var id = await RequestAsync(owner);

        // المالك معتمِد المستوى نفسه لكنه مقدّم الطلب: يُمنع
        Assert.Equal(403, (await owner.Post($"/approvals/{id}/approve", new { comment = "ذاتي" })).Status);
        Assert.Equal("pending", (await owner.Get("/approvals?pageSize=200")).Data!["items"]!.AsArray().Single(r => r!["id"].S() == id)!["status"].S());
    }

    [Fact]
    public async Task Request_list_shows_each_user_only_what_concerns_them()
    {
        var owner = await NewTenantAsync();
        await PolicyAsync(owner, null, "general_manager");
        var manager = await UserAsync(owner, "general_manager");
        var rep = await UserAsync(owner, "sales_rep");
        var id = await RequestAsync(owner);
        var repOwn = await RequestAsync(rep);

        Assert.Contains(id, await VisibleIdsAsync(owner));                // صاحب الصلاحية الكاملة يرى الكل
        Assert.Contains(id, await VisibleIdsAsync(manager));              // معتمِد المستوى بدوره
        var repSees = await VisibleIdsAsync(rep);
        Assert.DoesNotContain(id, repSees);                               // لا يخصّه: لا يُرى (كان يراه كل من يملك لوحة المؤشرات)
        Assert.Contains(repOwn, repSees);                                 // ما قدّمه يراه
    }

    [Fact]
    public async Task Center_manager_approves_the_current_level_only_and_one_level_per_request()
    {
        var owner = await NewTenantAsync();
        await PolicyAsync(owner, null, "chief_accountant", "general_manager");
        var admin = await UserAsync(owner, "admin");          // approval-center.manage (صلاحية كاملة)
        var manager = await UserAsync(owner, "general_manager");
        var id = await RequestAsync(owner);

        // المدير العام ليس معتمِد المستوى الأول ولا يملك إدارة المركز افتراضياً: لا يتجاوز الترتيب
        Assert.Equal(403, (await manager.Post($"/approvals/{id}/approve", new { comment = "مبكر" })).Status);

        // مدير المركز يعتمد عن المستوى الأول وحده: الطلب ينتقل للثاني ويبقى معلّقاً
        var level1 = await admin.Post($"/approvals/{id}/approve", new { comment = "عن المستوى الأول", expectedLevel = 1 });
        Assert.Equal(200, level1.Status);
        Assert.Equal("pending", level1.Data!["status"].S());
        Assert.Equal(2, level1.Data["currentLevel"]!.GetValue<int>());

        // ولا يعتمد مستوىً ثانياً من الطلب نفسه: الباقي لمعتمِده المعيَّن
        Assert.Equal(409, (await admin.Post($"/approvals/{id}/approve", new { comment = "مرة ثانية", expectedLevel = 2 })).Status);
        var final = await manager.Post($"/approvals/{id}/approve", new { comment = "نهائي", expectedLevel = 2 });
        Assert.Equal(200, final.Status);
        Assert.Equal("approved", final.Data!["status"].S());
    }

    [Fact]
    public async Task Decision_on_a_level_that_changed_meanwhile_is_rejected()
    {
        var owner = await NewTenantAsync();
        await PolicyAsync(owner, null, "general_manager", "general_manager");
        var manager = await UserAsync(owner, "general_manager");
        var id = await RequestAsync(owner);

        Assert.Equal(200, (await manager.Post($"/approvals/{id}/approve", new { comment = "أول", expectedLevel = 1 })).Status);
        // نقر مكرر على الشاشة القديمة (المستوى 1): يُرفض ولا يعتمد المستوى الثاني بالخطأ
        var stale = await manager.Post($"/approvals/{id}/approve", new { comment = "مكرر", expectedLevel = 1 });
        Assert.Equal(409, stale.Status);
        var request = (await owner.Get("/approvals?pageSize=200")).Data!["items"]!.AsArray().Single(r => r!["id"].S() == id)!;
        Assert.Equal("pending", request["status"].S());
        Assert.Equal(2, request["currentLevel"]!.GetValue<int>());
    }

    [Fact]
    public async Task Returned_request_closes_with_reason_and_keeps_the_invoice_as_draft()
    {
        var owner = await NewTenantAsync();
        var product = await SeedProductAsync(owner);
        await PolicyAsync(owner, null, "general_manager");
        var manager = await UserAsync(owner, "general_manager");
        var draft = await owner.PostInvoice(new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "draft",
            items = new[] { new { itemId = product, quantity = 1, unitPrice = 100, vatRate = 15 } } });
        var invoiceId = draft.Data!["id"].S();
        var check = await owner.Post("/approvals/check", new { documentType = "sales", actionType = "create", documentId = invoiceId,
            documentNumber = draft.Data["invoiceNumber"].S(), documentAmount = 115 });
        var id = check.Data!["request"]!["id"].S();

        Assert.Equal(400, (await manager.Post($"/approvals/{id}/return", new { comment = "" })).Status); // السبب إلزامي
        var returned = await manager.Post($"/approvals/{id}/return", new { comment = "صحّح الكمية", expectedLevel = 1 });
        Assert.Equal(200, returned.Status);
        Assert.Equal("returned", returned.Data!["status"].S());
        Assert.Equal("صحّح الكمية", returned.Data["rejectionReason"].S());
        Assert.Equal(new[] { "submitted", "returned" }, returned.Data["history"]!.AsArray().Select(h => h!["action"].S()).ToArray());

        Assert.Equal("draft", (await owner.Get(await owner.InvoicePath(invoiceId))).Data!["status"].S()); // لم يُرحَّل
        Assert.Equal(409, (await manager.Post($"/approvals/{id}/approve", new { comment = "بعد الإعادة" })).Status); // أُغلق
        Assert.Contains((await owner.Get("/notifications")).Data!.AsArray(), n => n!["type"].S() == "approval_returned");
    }

    [Fact]
    public async Task Concurrent_approvals_execute_the_held_operation_once()
    {
        var owner = await NewTenantAsync();
        Assert.Equal(201, (await owner.Post("/ApprovalPolicies", new { nameAr = "اعتماد القيود", documentTypes = new[] { "journal_entry" },
            actionType = "create", minAmountTrigger = 1000, isActive = true, steps = new[] { new { level = 1, approverRole = "general_manager", titleAr = "المدير العام" } } })).Status);
        var manager = await UserAsync(owner, "general_manager");
        var admin = await UserAsync(owner, "admin");

        var before = (await owner.Get("/journalentries")).Data!["totalCount"].D();
        var held = await owner.Post("/journalentries", new { date = DateTime.UtcNow.Date, description = "رأس المال",
            lines = new[] { new { accountCode = "1111", debit = 5000m, credit = 0m }, new { accountCode = "31", debit = 0m, credit = 5000m } } });
        Assert.Equal(202, held.Status);
        var id = held.Data!["id"].S();

        // معتمِد المستوى ومدير المركز يعتمدان في اللحظة نفسها: ينجح قرار واحد ويُرفض الآخر بتعارض
        var results = await Task.WhenAll(
            manager.Post($"/approvals/{id}/approve", new { comment = "أ" }),
            admin.Post($"/approvals/{id}/approve", new { comment = "ب" }));
        Assert.Equal(1, results.Count(r => r.Status == 200));
        Assert.All(results.Where(r => r.Status != 200), r => Assert.Equal(409, r.Status));

        // العملية المحجوزة نُفّذت مرة واحدة
        Assert.Equal(before + 1, (await owner.Get("/journalentries")).Data!["totalCount"].D());
        var history = (await owner.Get("/approvals?pageSize=200")).Data!["items"]!.AsArray().Single(r => r!["id"].S() == id)!["history"]!.AsArray();
        Assert.Equal(1, history.Count(h => h!["action"].S() == "approved"));
    }

    [Fact]
    public async Task Level_deadline_follows_the_step_sla()
    {
        var owner = await NewTenantAsync();
        await PolicyAsync(owner, 24, "general_manager");
        var id = await RequestAsync(owner);
        var request = (await owner.Get("/approvals?pageSize=200")).Data!["items"]!.AsArray().Single(r => r!["id"].S() == id)!;
        var due = DateTime.Parse(request["dueAt"].S(), null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
        Assert.InRange(due, DateTime.UtcNow.AddHours(23), DateTime.UtcNow.AddHours(25));

        Assert.Equal(400, (await owner.Post("/ApprovalPolicies", new { nameAr = "مهلة غير صالحة", documentTypes = new[] { "sales" }, actionType = "create", isActive = true,
            steps = new[] { new { level = 1, approverRole = "owner", titleAr = "x", slaHours = 0 } } })).Status);
    }

    [Fact]
    public async Task Withdrawal_is_recorded_in_the_audit_trail()
    {
        var owner = await NewTenantAsync();
        await PolicyAsync(owner, null, "general_manager");
        var id = await RequestAsync(owner);
        var withdrawn = await owner.Delete($"/approvals/{id}");
        Assert.Equal(200, withdrawn.Status);
        Assert.Equal("cancelled", withdrawn.Data!["status"].S());
        Assert.Equal(new[] { "submitted", "withdrawn" }, withdrawn.Data["history"]!.AsArray().Select(h => h!["action"].S()).ToArray());
    }
}
