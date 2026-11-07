using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// حذف العقد: في المسودة يُحذف مع سجلاته التابعة (سجل الحالات، قرارات المراجعة المُعادة، المرفقات، إعداد المشروع)،
/// وبعد المسودة يُرفض (يُلغى ولا يُحذف).
/// </summary>
[Collection("api")]
public class ContractDeleteTests : TestBase
{
    public ContractDeleteTests(ErpFactory f) : base(f) { }

    private static void Ok(Res r, string step) => Assert.True(r.Status is 200 or 201, $"{step}: {r.Status} {r.Body?.ToJsonString()}");

    private static async Task<string> SupplyAsync(Client api, Guid party, string workflow = "simple")
    {
        var r = await api.Post("/commercialcontracts", new
        {
            title = "توريد", contractType = "supply_materials", workflow, partyName = "عميل", partyId = party, contractValue = 1000, vatRate = 0,
            startDate = DateTime.UtcNow.Date, endDate = DateTime.UtcNow.Date.AddMonths(1),
            items = new[] { new { description = "صنف", unit = "PCS", quantity = 1, unitPrice = 1000, vatRate = 0 } },
            milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
        });
        Ok(r, "create supply");
        return r.Data!["id"].S();
    }

    [Fact]
    public async Task Draft_contract_is_deleted_with_its_status_log()
    {
        var api = await NewTenantAsync();
        var (party, _) = await SeedCustomerAsync(api);
        var id = await SupplyAsync(api, party);
        Ok(await api.Delete($"/commercialcontracts/{id}"), "delete draft");
        Assert.Equal(404, (await api.Get($"/commercialcontracts/{id}")).Status);
    }

    [Fact]
    public async Task Draft_returned_from_review_is_deleted_with_its_review_decisions()
    {
        var api = await NewTenantAsync();
        var (party, _) = await SeedCustomerAsync(api);
        var id = await SupplyAsync(api, party, "full");
        Ok(await api.Post($"/commercialcontracts/{id}/move", new { toStatus = "review" }), "to review");
        Ok(await api.Post($"/commercialcontracts/{id}/review", new { kind = "legal", decision = "return", note = "نواقص" }), "returned");
        Assert.Equal("draft", (await api.Get($"/commercialcontracts/{id}")).Data!["stage"].S());
        Assert.Equal(200, await UploadFileAsync(api, $"/commercialcontracts/{id}/attachments?kind=other", "note.pdf", SmallPdf));

        Ok(await api.Delete($"/commercialcontracts/{id}"), "delete returned draft");
        Assert.Equal(404, (await api.Get($"/commercialcontracts/{id}")).Status);
    }

    [Fact]
    public async Task Draft_construction_contract_is_deleted_with_its_project_setup()
    {
        var api = await NewTenantAsync();
        var created = await api.Post("/commercialcontracts", new
        {
            title = "مشروع", contractType = "construction_boq", workflow = "simple", requiresGuarantee = false, partyName = "عميل",
            startDate = DateTime.UtcNow.Date, endDate = DateTime.UtcNow.Date.AddMonths(6), vatRate = 15,
            items = new[] { new { description = "صب خرسانة", unit = "م3", quantity = 100, unitPrice = 300, vatRate = 15, lineKind = "measured" } },
            milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
        });
        Ok(created, "create boq");
        var id = created.Data!["id"].S();
        var item = created.Data["items"]![0]!["id"].S();
        var me = (await api.Get($"/commercialcontracts/{id}/project/member-candidates")).Data!.AsArray()[0]!["id"].S();
        Ok(await api.Put($"/commercialcontracts/{id}/project/phases", new
        {
            phases = new[] { new { name = "الهيكل", plannedStart = DateTime.UtcNow.Date, plannedEnd = DateTime.UtcNow.Date.AddDays(9), weightPercent = 50, contractItemIds = new[] { item } } },
        }), "phases");
        Ok(await api.Post($"/commercialcontracts/{id}/project/members", new { userId = me, role = "project_manager" }), "member");

        var center = created.Data["costCenterId"].S();
        Ok(await api.Delete($"/commercialcontracts/{id}"), "delete boq draft");
        Assert.Equal(404, (await api.Get($"/commercialcontracts/{id}/project")).Status);
        // مركز التكلفة المنشأ للمشروع تلقائياً لا يبقى يتيماً
        Assert.Equal(404, (await api.Get($"/costcenters/{center}")).Status);
    }

    [Fact]
    public async Task Contract_after_draft_cannot_be_deleted()
    {
        var api = await NewTenantAsync();
        var (party, _) = await SeedCustomerAsync(api);
        var id = await SupplyAsync(api, party);
        Ok(await api.Post($"/commercialcontracts/{id}/move", new { toStatus = "active" }), "activate");
        Assert.Equal(409, (await api.Delete($"/commercialcontracts/{id}")).Status);
        Assert.Equal(200, (await api.Get($"/commercialcontracts/{id}")).Status);
    }
}
