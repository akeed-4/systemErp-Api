using ERP.Core.Models.Accounting;
using ERP.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Tests;

/// <summary>
/// الموافقة المالية الآلية عند «إنشاء العقد» من دراسة معتمدة (والعقد ساري مع إنشائه): سجل موافقة مالية باسم معتمِد الخطة وتاريخه ومرجع الدراسة
/// (بلا صلاحية المراجعة المالية له)، تُسقط بالرجوع للمسودة أو بتغيّر قيمة العقد فتلزم المراجعة المالية العادية. والضمان البنكي اختيار
/// للعقد (الدراسة التي أُرسلت بلا ضمان تُفعَّل بلا ضمان).
/// </summary>
public partial class FeasibilityFlowTests
{
    /// <summary>قيمة الدراسة: 100 × 80 + 50 × 150 + 10 × 40.</summary>
    private const decimal StudyValue = 15_900m;

    private static Task<Res> Move(Client api, string id, string to, string? note = null)
        => api.Post($"/commercialcontracts/{id}/move", new { toStatus = to, note });

    private static Task<Res> Review(Client api, string id, string kind, string decision = "approve", string? note = null)
        => api.Post($"/commercialcontracts/{id}/review", new { kind, decision, note });

    /// <summary>مستخدم يعتمد خطة التنفيذ (عرض واعتماد العقود) ولا يملك المراجعة المالية ولا القانونية.</summary>
    private async Task<(Client Api, string Name)> PlanApproverAsync(Client owner)
    {
        var role = (await owner.Post("/roles", new { nameAr = "معتمد الدراسات" })).Data!["code"].S();
        Ok(await owner.Post("/permissions", new
        {
            roleId = role,
            permissions = new[] { new { screenId = "commercial-contracts", screenNameAr = "", screenNameEn = "", actions = new[] { "view", "approve" } } },
        }), "approver permissions");
        var roleId = (await owner.Get("/roles")).Data!.AsArray().First(r => r!["code"].S() == role)!["id"].G();
        var email = $"fa{Guid.NewGuid():N}@test.com";
        const string name = "معتمد الدراسة";
        Ok(await owner.Post("/users", new { name, email, phone = "1", password = "Passw0rd!", role = "sales_rep", customRoleId = roleId }), "approver user");
        return (await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!"), name);
    }

    /// <summary>دراسة على المسار الكامل، خطتها معتمدة من مستخدم <paramref name="approverFactory"/> (أو المالك)، ثم «إنشاء العقد».</summary>
    private async Task<Study> ContractFromFullStudyAsync(Func<Client, Task<Client>>? approverFactory = null)
    {
        var s = await PricedStudyAsync("full");
        var by = approverFactory != null ? await approverFactory(s.Api) : s.Api;
        Ok(await s.Api.Post($"/commercialcontracts/{s.Id}/execution-plan/generate", new { }), "generate");
        Ok(await s.Api.Post($"/commercialcontracts/{s.Id}/execution-plan/submit", new { }), "submit");
        Ok(await by.Post($"/commercialcontracts/{s.Id}/execution-plan/approve", new { }), "approve plan");
        Ok(await s.Api.Post($"/feasibility-studies/{s.Id}/create-contract", new { }), "create contract");
        return s;
    }

    private async Task<List<ContractApproval>> ApprovalsAsync(string id)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>();
        return await db.Set<ContractApproval>().IgnoreQueryFilters().AsNoTracking().Where(a => a.ContractId == Guid.Parse(id)).OrderBy(a => a.CreatedAt).ToListAsync();
    }

    private async Task<ProjectExecutionPlan> PlanRowAsync(string id)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>();
        return await db.Set<ProjectExecutionPlan>().IgnoreQueryFilters().AsNoTracking().FirstAsync(p => p.ContractId == Guid.Parse(id));
    }

    private static object ContractUpdate(Study s, decimal digPrice) => new
    {
        title = "دراسة", projectName = "مشروع الخطة", contractType = "construction_boq", workflow = "full", partyName = "عميل", partyId = s.Party,
        startDate = Today, endDate = Today.AddDays(29), vatRate = 15, milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
        items = new object[]
        {
            new { description = "حفر", unit = "م3", quantity = 100, unitPrice = digPrice, vatRate = 15, lineKind = "measured", sectionName = "أعمال ترابية", sortOrder = 1 },
            new { description = "صب", unit = "م3", quantity = 50, unitPrice = 150, vatRate = 15, lineKind = "measured", sortOrder = 2 },
            new { description = "بند يدوي", unit = "م2", quantity = 10, unitPrice = 40, vatRate = 15, lineKind = "measured", estimatedUnitCost = 20, sortOrder = 3 },
        },
    };

    private static async Task<bool> FinancialApprovedAsync(Client api, string id)
        => (await api.Get($"/commercialcontracts/{id}/workflow")).Data!["approvals"]!.AsArray()
            .Any(a => a!["kind"].S() == "financial" && a["decision"].S() == "approved");

    [Fact]
    public async Task Contract_from_approved_study_gets_financial_approval_from_the_study_approver_without_financial_review_permission()
    {
        (Client Api, string Name) approver = default;
        var s = await ContractFromFullStudyAsync(approverFactory: async owner => (approver = await PlanApproverAsync(owner)).Api);
        var api = s.Api;
        var fsNumber = (await api.Get($"/commercialcontracts/{s.Id}")).Data!["feasibilityNumber"].S();

        // السجل: موافقة مالية في الجولة الأولى باسم معتمِد الخطة وتاريخ اعتماده ومرجع الدراسة ونسختها المجمّدة وقيمتها
        var plan = await PlanRowAsync(s.Id);
        var auto = Assert.Single(await ApprovalsAsync(s.Id));
        Assert.Equal("financial", auto.Kind);
        Assert.Equal("approved", auto.Decision);
        Assert.Equal(1, auto.Round);
        Assert.Equal(plan.ApprovedByUserId, auto.UserId);
        Assert.Equal(approver.Name, auto.UserName);
        Assert.Equal(plan.ApprovedAt, auto.DecidedAt);
        Assert.Equal(ContractApprovalSources.FeasibilityStudy, auto.Source);
        Assert.Equal(fsNumber, auto.SourceReference);
        Assert.NotNull(auto.SourceId);
        Assert.Equal(StudyValue, auto.ApprovedValue);
        Assert.Null(auto.InvalidatedAt);


        var wf = (await api.Get($"/commercialcontracts/{s.Id}/workflow")).Data!;
        var row = Assert.Single(wf["approvals"]!.AsArray())!;
        Assert.Equal("feasibility_study", row["source"].S());
        Assert.Equal(fsNumber, row["sourceReference"].S());
        Assert.Equal(approver.Name, row["userName"].S());
        Assert.False(wf["requiresGuarantee"]!.GetValue<bool>()); // الدراسة أُرسلت بلا ضمان: لا يُفرض عند إنشاء العقد

        // اعتماد الدراسة والخطة هو قرار التعاقد: العقد ساري مع إنشائه بلا مراجعة ثانية ولا ضمان ولا نسخة موقعة
        Assert.Equal("active", (await api.Get($"/commercialcontracts/{s.Id}")).Data!["stage"].S());
    }

    /// <summary>
    /// العقد الساري من الدراسة لا يعود للمراجعة ولا تُعدَّل قيمته، فتبقى موافقته المالية الآلية سارية بقيمة الدراسة.
    /// (إسقاطها بالرجوع للمسودة أو بتغيّر القيمة يخص العقود المنشأة مسودةً قبل التفعيل المباشر.)
    /// </summary>
    [Fact]
    public async Task Active_contract_from_a_study_keeps_its_automatic_approval_and_cannot_be_edited_or_sent_back()
    {
        var s = await ContractFromFullStudyAsync();
        var api = s.Api;
        Assert.Equal(409, (await api.Put($"/commercialcontracts/{s.Id}", ContractUpdate(s, 90))).Status);
        Assert.Equal(409, (await Move(api, s.Id, "review")).Status);
        Assert.Equal(StudyValue, (await api.Get($"/commercialcontracts/{s.Id}")).Data!["contractValue"].D());
        var auto = Assert.Single(await ApprovalsAsync(s.Id));
        Assert.Null(auto.InvalidatedAt);
        Assert.True(await FinancialApprovedAsync(api, s.Id));
    }

    [Fact]
    public async Task Direct_contract_gets_no_automatic_financial_approval()
    {
        var api = await NewConstructionTenantAsync();
        var (party, _) = await SeedCustomerAsync(api);
        var created = await api.Post("/commercialcontracts", StudyBody(party, SimpleItems, study: false));
        Ok(created, "direct");
        var id = created.Data!["id"].S();
        Assert.Empty(await ApprovalsAsync(id));
    }

    [Fact]
    public async Task Separating_preparer_and_approver_is_optional_and_when_on_the_submitter_cannot_approve()
    {
        var s = await PricedStudyAsync();
        var api = s.Api;
        // الافتراضي معطّل: من يعمل وحده يقدّم ويعتمد
        Assert.False((await api.Get("/accounting-settings")).Data!["constructionSeparateApprover"]!.GetValue<bool>());

        Ok(await api.Put("/accounting-settings", new { constructionSeparateApprover = true }), "enable separation");
        Assert.True((await api.Get("/accounting-settings")).Data!["constructionSeparateApprover"]!.GetValue<bool>());
        Ok(await api.Post($"/commercialcontracts/{s.Id}/execution-plan/generate", new { }), "generate");
        Ok(await api.Post($"/commercialcontracts/{s.Id}/execution-plan/submit", new { }), "submit");
        Assert.Equal(409, (await api.Post($"/commercialcontracts/{s.Id}/execution-plan/approve", new { })).Status);

        var approver = await PlanApproverAsync(api);
        Ok(await approver.Api.Post($"/commercialcontracts/{s.Id}/execution-plan/approve", new { }), "another user approves");

        // تعديل إعداد آخر لا يمس الخيار (التحديث جزئي)
        Ok(await api.Put("/accounting-settings", new { salesReturnPeriodDays = 30 }), "unrelated setting");
        Assert.True((await api.Get("/accounting-settings")).Data!["constructionSeparateApprover"]!.GetValue<bool>());
    }
}
