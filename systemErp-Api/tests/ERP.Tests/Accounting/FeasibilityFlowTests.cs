using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// دورة السجل الواحد: دراسة جدوى (رقم FS، بلا رقم عقد ولا مركز تكلفة، خارج قوائم العقود والمشاريع، ولا تخرج من المسودة) ←
/// خطة تنفيذ معتمدة ← «إنشاء العقد». العقد المباشر يعمل كما كان.
/// </summary>
[Collection("api")]
public partial class FeasibilityFlowTests : TestBase
{
    public FeasibilityFlowTests(ErpFactory f) : base(f) { }

    private static readonly DateTime Today = DateTime.UtcNow.Date;

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");

    private static object StudyBody(Guid party, object[] items, bool study = true, string? partyType = null, string? tenderStatus = null) => new
    {
        title = "دراسة برج", projectName = "برج الريان", contractType = "construction_boq", workflow = "simple", requiresGuarantee = false,
        partyName = "عميل", partyId = party, partyType, startDate = Today, endDate = Today.AddMonths(6), vatRate = 15, items,
        milestones = Array.Empty<object>(), clauses = Array.Empty<object>(), isFeasibilityStudy = study, tenderStatus,
    };

    private static readonly object[] SimpleItems =
    {
        new { description = "حفر", unit = "م3", quantity = 100, unitPrice = 50, vatRate = 15, lineKind = "measured", estimatedUnitCost = 30, sectionName = "أعمال ترابية", sortOrder = 1 },
    };

    [Fact]
    public async Task Study_gets_its_own_number_and_stays_out_of_contracts_and_projects_until_contracted()
    {
        var api = await NewConstructionTenantAsync();
        var (party, _) = await SeedCustomerAsync(api);

        var created = await api.Post("/commercialcontracts", StudyBody(party, SimpleItems));
        Ok(created, "study");
        var study = created.Data!;
        var id = study["id"].S();
        Assert.Equal("study", study["feasibilityStatus"].S());
        Assert.StartsWith($"FS-{Today.Year}-", study["feasibilityNumber"].S());
        Assert.Equal(study["feasibilityNumber"].S(), study["contractNumber"].S()); // لا رقم عقد بعد
        Assert.Null(study["costCenterId"]?.GetValue<string?>()); // لا مركز تكلفة قبل العقد

        // ملف المشروع موجود (ساعات العمل والميزانية للخطة)
        Ok(await api.Get($"/commercialcontracts/{id}/project"), "project profile");

        // عقد مباشر للمقارنة: يعمل كما كان
        var direct = await api.Post("/commercialcontracts", StudyBody(party, SimpleItems, study: false));
        Ok(direct, "direct");
        Assert.Null(direct.Data!["feasibilityStatus"]?.GetValue<string?>());
        Assert.StartsWith("CON-", direct.Data["contractNumber"].S());

        var contracts = (await api.Get("/commercialcontracts?pageSize=100")).Data!["items"]!.AsArray().Select(c => c!["id"].S()).ToList();
        Assert.DoesNotContain(id, contracts);
        Assert.Contains(direct.Data["id"].S(), contracts);
        var projects = (await api.Get("/construction-projects")).Data!.AsArray().Select(p => p!["contractId"].S()).ToList();
        Assert.DoesNotContain(id, projects);
        Assert.Contains(direct.Data["id"].S(), projects);

        var studies = (await api.Get("/feasibility-studies")).Data!.AsArray();
        var row = Assert.Single(studies)!;
        Assert.Equal(id, row["id"].S());
        Assert.Equal(5000m, row["contractValue"].D());
        Assert.Equal(3000m, row["estimatedCost"].D());
        Assert.Equal(2000m, row["margin"].D());
        Assert.Equal(40m, row["marginPercent"].D());
        Assert.Null(row["contractNumber"]?.GetValue<string?>());

        // لا تخرج من المسودة بدورة العقد قبل «إنشاء العقد»
        var move = await api.Post($"/commercialcontracts/{id}/move", new { toStatus = "active" });
        Assert.NotEqual(200, move.Status);
        Assert.Equal("draft", (await api.Get($"/commercialcontracts/{id}")).Data!["stage"].S());
        // والتعديل في المسودة يبقيها دراسة برقمها
        var saved = await api.Put($"/commercialcontracts/{id}", StudyBody(party, SimpleItems));
        Ok(saved, "edit study");
        Assert.Equal("study", saved.Data!["feasibilityStatus"].S());
        Assert.Equal(study["feasibilityNumber"].S(), saved.Data["contractNumber"].S());
    }

    [Fact]
    public async Task Study_is_only_for_customer_construction_projects()
    {
        var api = await NewConstructionTenantAsync();
        var (party, _) = await SeedCustomerAsync(api);
        var (supplier, _) = await SeedSupplierAsync(api);
        Assert.Equal(400, (await api.Post("/commercialcontracts", StudyBody(supplier, SimpleItems, partyType: "supplier"))).Status);
        Assert.Equal(400, (await api.Post("/commercialcontracts", StudyBody(party, SimpleItems, tenderStatus: "new"))).Status);
        Assert.Empty((await api.Get("/feasibility-studies")).Data!.AsArray());
    }
}
