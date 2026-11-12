using ERP.Core.Models.Accounting;
using ERP.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Tests;

/// <summary>
/// الموافقة المالية الآلية عند «إنشاء العقد» من دراسة معتمدة: سجل موافقة مالية في الجولة الأولى باسم معتمِد الخطة وتاريخه ومرجع الدراسة
/// (بلا صلاحية المراجعة المالية له)، تُسقط بالرجوع للمسودة أو بتغيّر قيمة العقد فتلزم المراجعة المالية العادية. والضمان البنكي إلزامي
/// لعقد المقاولات على المسار الكامل.
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
        Assert.True(wf["requiresGuarantee"]!.GetValue<bool>()); // مقاولات + مسار كامل

        // معتمِد الدراسة لا يملك المراجعة المالية على العقد (الموافقة لم تمر بها)
        Ok(await Move(api, s.Id, "review"), "submit for review");
        Assert.Equal(403, (await Review(approver.Api, s.Id, "financial")).Status);
        Assert.False((await api.Get($"/commercialcontracts/{s.Id}/workflow")).Data!["canReviewFinancial"]!.GetValue<bool>());

        // الموافقة القانونية وحدها تكمل الجولة: المالية محسوبة من الدراسة
        Ok(await Review(api, s.Id, "legal"), "legal");
        Assert.Equal("approval", (await api.Get($"/commercialcontracts/{s.Id}")).Data!["stage"].S());
        Assert.Single(await ApprovalsAsync(s.Id), a => a.Kind == "financial"); // لا موافقة مالية يدوية

        // الضمان البنكي إلزامي قبل التفعيل (أُرسلت الدراسة بلا ضمان)
        var blocked = await Move(api, s.Id, "active");
        Assert.Equal(409, blocked.Status);
        Assert.Contains("الضمان البنكي", blocked.Body!["message"].S());
        Assert.Equal(200, await UploadFileAsync(api, $"/commercialcontracts/{s.Id}/attachments?kind=signed_copy", "signed.pdf", SmallPdf));
        Ok(await api.Put($"/commercialcontracts/{s.Id}/signing", new
        {
            guaranteeNumber = "LG-1", guaranteeBank = "بنك", guaranteeAmount = 1000, guaranteeExpiry = Today.AddYears(1),
        }), "signing");
        Ok(await Move(api, s.Id, "active"), "activate");
    }

    [Fact]
    public async Task Returning_to_draft_invalidates_the_automatic_approval_and_the_next_round_needs_normal_financial_review()
    {
        var s = await ContractFromFullStudyAsync();
        var api = s.Api;
        Ok(await Move(api, s.Id, "review"), "submit");
        Ok(await Review(api, s.Id, "legal", "return", "عدّل البنود"), "legal return");
        Assert.Equal("draft", (await api.Get($"/commercialcontracts/{s.Id}")).Data!["stage"].S());

        // السجل باقٍ بمعتمِده وتاريخه ومرجعه، ومُسقط بسبب الرجوع للمسودة
        var plan = await PlanRowAsync(s.Id);
        var auto = Assert.Single(await ApprovalsAsync(s.Id), a => a.Source == ContractApprovalSources.FeasibilityStudy);
        Assert.NotNull(auto.InvalidatedAt);
        Assert.Equal(ContractApprovalSources.DraftReset, auto.InvalidationReason);
        Assert.Equal(plan.ApprovedByUserId, auto.UserId);
        Assert.Equal(plan.ApprovedAt, auto.DecidedAt);
        Assert.Equal(1, auto.Round);
        Assert.False(await FinancialApprovedAsync(api, s.Id));

        // الجولة الثانية: القانونية وحدها لا تكفي، والمراجعة المالية العادية مطلوبة
        Ok(await Move(api, s.Id, "review"), "resubmit");
        Assert.True((await api.Get($"/commercialcontracts/{s.Id}/workflow")).Data!["canReviewFinancial"]!.GetValue<bool>());
        Ok(await Review(api, s.Id, "legal"), "legal round 2");
        Assert.Equal("review", (await api.Get($"/commercialcontracts/{s.Id}")).Data!["stage"].S());
        Ok(await Review(api, s.Id, "financial"), "financial round 2");
        Assert.Equal("approval", (await api.Get($"/commercialcontracts/{s.Id}")).Data!["stage"].S());
        var manual = Assert.Single(await ApprovalsAsync(s.Id), a => a.Kind == "financial" && a.Round == 2);
        Assert.Null(manual.Source);
    }

    [Fact]
    public async Task Changing_the_contract_value_invalidates_the_automatic_approval_permanently()
    {
        var s = await ContractFromFullStudyAsync();
        var api = s.Api;

        // تعديل لا يغيّر القيمة يبقي الموافقة
        var same = await api.Put($"/commercialcontracts/{s.Id}", ContractUpdate(s, 80));
        Ok(same, "same value edit");
        Assert.Equal(StudyValue, same.Data!["contractValue"].D());
        Assert.True(await FinancialApprovedAsync(api, s.Id));
        Assert.Null(Assert.Single(await ApprovalsAsync(s.Id)).InvalidatedAt);

        // تغيّر القيمة يسقطها (والسجل باقٍ بقيمته المعتمدة)
        var changed = await api.Put($"/commercialcontracts/{s.Id}", ContractUpdate(s, 90));
        Ok(changed, "value change");
        Assert.Equal(StudyValue + 1_000m, changed.Data!["contractValue"].D());
        var auto = Assert.Single(await ApprovalsAsync(s.Id));
        Assert.Equal(ContractApprovalSources.ValueChanged, auto.InvalidationReason);
        Assert.Equal(StudyValue, auto.ApprovedValue);
        Assert.False(await FinancialApprovedAsync(api, s.Id));

        // إعادة القيمة كما كانت لا تعيد الموافقة الآلية
        Ok(await api.Put($"/commercialcontracts/{s.Id}", ContractUpdate(s, 80)), "value restored");
        Assert.False(await FinancialApprovedAsync(api, s.Id));

        Ok(await Move(api, s.Id, "review"), "submit");
        Ok(await Review(api, s.Id, "legal"), "legal");
        Assert.Equal("review", (await api.Get($"/commercialcontracts/{s.Id}")).Data!["stage"].S());
        Ok(await Review(api, s.Id, "financial"), "financial");
        Assert.Equal("approval", (await api.Get($"/commercialcontracts/{s.Id}")).Data!["stage"].S());
    }

    [Fact]
    public async Task Automatic_approval_whose_value_no_longer_matches_does_not_count_even_if_not_marked()
    {
        var s = await ContractFromFullStudyAsync();
        // تغيير القيمة من خارج مسار التعديل (مباشرة في قاعدة البيانات): شرط الموافقتين يقارن القيمة عند كل فحص
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>();
            var c = await db.Set<CommercialContract>().IgnoreQueryFilters().FirstAsync(x => x.Id == Guid.Parse(s.Id));
            c.ContractValue += 500m;
            await db.SaveChangesAsync();
        }
        Assert.False(await FinancialApprovedAsync(s.Api, s.Id));
        Ok(await Move(s.Api, s.Id, "review"), "submit");
        Ok(await Review(s.Api, s.Id, "legal"), "legal");
        Assert.Equal("review", (await s.Api.Get($"/commercialcontracts/{s.Id}")).Data!["stage"].S());
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
}
