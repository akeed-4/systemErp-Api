using ERP.Core.Contracts.Shared;
using ERP.Core.Models.Accounting;
using ERP.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Tests;

/// <summary>
/// دالة الانتقال moveContract: الانتقال من جدول الانتقالات فقط، الصلاحية، السبب الإلزامي، الشروط برسائل تذكر ما ينقص،
/// موافقة المراجعَين، شروط التفعيل (النسخة الموقعة والضمان)، اكتمال الاستلام، الإيقاف والاستئناف، والسجل.
/// </summary>
[Collection("api")]
public class ContractWorkflowTests : TestBase
{
    public ContractWorkflowTests(ErpFactory f) : base(f) { }

    private static object ContractBody(Guid? partyId, Guid? itemId, decimal qty = 2, bool complete = true) => new
    {
        title = "عقد توريد", contractType = "supply_materials", partyName = "عميل", partyId,
        contractValue = qty * 100, vatRate = 15,
        startDate = complete ? DateTime.UtcNow.Date : (DateTime?)null, endDate = complete ? DateTime.UtcNow.Date.AddMonths(3) : (DateTime?)null,
        items = complete ? new object[] { new { itemId, description = "صنف", unit = "PCS", quantity = qty, unitPrice = 100, vatRate = 15 } } : Array.Empty<object>(),
        milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
    };

    private static Task<Res> Move(Client api, string id, string to, string? note = null)
        => api.Post($"/commercialcontracts/{id}/move", new { toStatus = to, note });

    /// <summary>عقد كامل المسار حتى «ساري»: إرسال، موافقتان، نسخة موقعة، ضمان، اعتماد.</summary>
    private async Task<string> ActiveContractAsync(Client owner, Guid party, Guid product, decimal qty = 2)
    {
        var id = (await owner.Post("/commercialcontracts", ContractBody(party, product, qty))).Data!["id"].S();
        Assert.Equal(200, (await Move(owner, id, "review")).Status);
        await owner.Post($"/commercialcontracts/{id}/review", new { kind = "legal", decision = "approve" });
        await owner.Post($"/commercialcontracts/{id}/review", new { kind = "financial", decision = "approve" });
        Assert.Equal(200, await UploadFileAsync(owner, $"/commercialcontracts/{id}/attachments?kind=signed_copy", "signed.pdf", SmallPdf));
        Assert.Equal(200, (await owner.Put($"/commercialcontracts/{id}/signing", new
        {
            guaranteeNumber = "LG-1", guaranteeBank = "بنك", guaranteeAmount = 50, guaranteeExpiry = DateTime.UtcNow.Date.AddYears(1),
        })).Status);
        var activated = await Move(owner, id, "active");
        Assert.True(activated.Status == 200, activated.Body?.ToJsonString());
        return id;
    }

    [Fact]
    public async Task Submit_lists_exactly_what_is_missing_and_starting_stage_is_ignored()
    {
        var owner = await NewTenantAsync();
        var created = await owner.Post("/commercialcontracts", new
        {
            title = "عقد", contractType = "supply_materials", partyName = "عميل", contractValue = 100, stage = "active", // المرحلة من المستخدم تُتجاهل
            items = Array.Empty<object>(), milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
        });
        Assert.Equal("draft", created.Data!["stage"].S());
        var id = created.Data["id"].S();

        var r = await Move(owner, id, "review");
        Assert.Equal(409, r.Status);
        var msg = r.Body!["message"].S();
        Assert.Contains("بنود العقد", msg);
        Assert.Contains("تاريخا البداية والنهاية", msg);
        Assert.Contains("الطرف", msg);
        Assert.DoesNotContain("عنوان العقد", msg); // العنوان موجود

        Assert.Equal(409, (await Move(owner, id, "active")).Status); // لا قفز من المسودة
    }

    [Fact]
    public async Task Activation_requires_signed_copy_and_guarantee_and_logs_every_transition()
    {
        var owner = await NewTenantAsync();
        var (party, _) = await SeedCustomerAsync(owner);
        var product = await SeedProductAsync(owner);
        var id = (await owner.Post("/commercialcontracts", ContractBody(party, product))).Data!["id"].S();
        Assert.Equal(200, (await Move(owner, id, "review")).Status);
        await owner.Post($"/commercialcontracts/{id}/review", new { kind = "legal", decision = "approve" });
        var afterReviews = await owner.Post($"/commercialcontracts/{id}/review", new { kind = "financial", decision = "approve" });
        Assert.Equal("approval", afterReviews.Data!["status"].S());

        var blocked = await Move(owner, id, "active");
        Assert.Equal(409, blocked.Status);
        Assert.Contains("النسخة الموقعة", blocked.Body!["message"].S());
        Assert.Contains("الضمان البنكي", blocked.Body!["message"].S());

        Assert.Equal(200, await UploadFileAsync(owner, $"/commercialcontracts/{id}/attachments?kind=signed_copy", "signed.pdf", SmallPdf));
        await owner.Put($"/commercialcontracts/{id}/signing", new { guaranteeNumber = "LG-1", guaranteeAmount = 50, guaranteeExpiry = DateTime.UtcNow.Date.AddDays(-1) });
        Assert.Contains("منتهٍ", (await Move(owner, id, "active")).Body!["message"].S());
        await owner.Put($"/commercialcontracts/{id}/signing", new { guaranteeNumber = "LG-1", guaranteeAmount = 50, guaranteeExpiry = DateTime.UtcNow.Date.AddYears(1) });

        var active = await Move(owner, id, "active");
        Assert.Equal(200, active.Status);
        Assert.Equal("active", active.Data!["status"].S());
        Assert.Equal(409, (await owner.Put($"/commercialcontracts/{id}/signing", new { guaranteeNumber = "X" })).Status); // بعد التفعيل مقفل

        var log = active.Data["log"]!.AsArray();
        Assert.Equal(new[] { "draft", "review", "approval", "active" }, log.Select(l => l!["toStatus"].S()));
        Assert.Equal("approval", log.Last()!["fromStatus"].S());
    }

    [Fact]
    public async Task Reason_is_mandatory_for_suspend_resume_and_cancel_and_cancel_is_only_before_activation()
    {
        var owner = await NewTenantAsync();
        var (party, _) = await SeedCustomerAsync(owner);
        var product = await SeedProductAsync(owner);

        var draft = (await owner.Post("/commercialcontracts", ContractBody(party, product))).Data!["id"].S();
        Assert.Equal(400, (await Move(owner, draft, "cancelled")).Status);
        Assert.Equal("cancelled", (await Move(owner, draft, "cancelled", "ألغي الطلب")).Data!["status"].S());

        var id = await ActiveContractAsync(owner, party, product);
        Assert.Equal(409, (await Move(owner, id, "cancelled", "سبب")).Status); // الإلغاء قبل التفعيل فقط
        Assert.Equal(400, (await Move(owner, id, "suspended")).Status);
        Assert.Equal("suspended", (await Move(owner, id, "suspended", "توقف المورد")).Data!["status"].S());
        Assert.Equal(400, (await Move(owner, id, "active")).Status);
        var resumed = await Move(owner, id, "active", "استؤنف التوريد");
        Assert.Equal("active", resumed.Data!["status"].S());
        Assert.Equal("استؤنف التوريد", resumed.Data["log"]!.AsArray().Last()!["note"].S());
    }

    [Fact]
    public async Task Initial_acceptance_requires_all_quantities_received_net_of_returns()
    {
        var owner = await NewTenantAsync();
        var (party, _) = await SeedCustomerAsync(owner);
        var product = await SeedProductAsync(owner);
        var id = await ActiveContractAsync(owner, party, product, qty: 4);

        var blocked = await Move(owner, id, "initial_acceptance");
        Assert.Equal(409, blocked.Status);
        Assert.Contains("صنف: 4", blocked.Body!["message"].S());

        var note = await owner.Post("/deliverynotes", new
        {
            type = "sales_delivery", partyName = "عميل", partyId = party, contractId = id, date = DateTime.UtcNow,
            items = new[] { new { itemId = product, itemName = "صنف", unit = "PCS", contractQty = 4, deliveredQty = 4, unitPrice = 100, vatRate = 15 } },
        });
        Assert.Equal(200, note.Status);
        var ret = await owner.Post("/deliverynotes/returns", new
        {
            type = "sales_delivery_return", deliveryNoteId = note.Data!["id"].S(), returnReason = "تالف",
            items = new[] { new { itemId = product, quantity = 1 } },
        });
        Assert.Equal(200, ret.Status);
        Assert.Contains("صنف: 1", (await Move(owner, id, "initial_acceptance")).Body!["message"].S()); // صافي بعد المرتجع

        await owner.Post("/deliverynotes", new
        {
            type = "sales_delivery", partyName = "عميل", partyId = party, contractId = id, date = DateTime.UtcNow,
            items = new[] { new { itemId = product, itemName = "صنف", unit = "PCS", contractQty = 4, deliveredQty = 1, unitPrice = 100, vatRate = 15 } },
        });
        Assert.Equal("initial_acceptance", (await Move(owner, id, "initial_acceptance")).Data!["status"].S());
    }

    [Fact]
    public async Task Buttons_show_only_transitions_the_user_may_perform()
    {
        var owner = await NewTenantAsync();
        var (party, _) = await SeedCustomerAsync(owner);
        var role = (await owner.Post("/roles", new { nameAr = "مُعد عقود" })).Data!["code"].S();
        await owner.Post("/permissions", new
        {
            roleId = role,
            permissions = new[] { new { screenId = "commercial-contracts", screenNameAr = "", screenNameEn = "", actions = new[] { "view", "create", "edit", "submit" } } },
        });
        var roleId = (await owner.Get("/roles")).Data!.AsArray().First(r => r!["code"].S() == role)!["id"].G();
        var email = $"p{Guid.NewGuid():N}@test.com";
        await owner.Post("/users", new { name = "مُعد", email, phone = "1", password = "Passw0rd!", role = "sales_rep", customRoleId = roleId });
        var preparer = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");

        var id = (await preparer.Post("/commercialcontracts", ContractBody(party, null, complete: false))).Data!["id"].S();
        var wf = (await preparer.Get($"/commercialcontracts/{id}/workflow")).Data!;
        var buttons = wf["transitions"]!.AsArray();
        var submit = Assert.Single(buttons); // لا «إلغاء» (يتطلب صلاحية الاعتماد)
        Assert.Equal("review", submit!["toStatus"].S());
        Assert.NotEmpty(submit["blockers"]!.AsArray()); // البيانات ناقصة

        // مُعد العقد ينشئ العقد المبسط مسودة، ولا يفعّله (التفعيل يتطلب صلاحية الاعتماد) ولا يرى زراً لا يملكه
        var simple = await preparer.Post("/commercialcontracts", new
        {
            title = "صيانة", contractType = "monthly_maintenance", partyName = "عميل", partyId = party, contractValue = 100,
            startDate = DateTime.UtcNow.Date, endDate = DateTime.UtcNow.Date.AddMonths(6),
            items = Array.Empty<object>(), milestones = new[] { new { title = "دفعة", percentage = 100 } }, clauses = Array.Empty<object>(),
        });
        Assert.Equal(201, simple.Status);
        Assert.Equal("draft", simple.Data!["stage"].S());
        var simpleId = simple.Data["id"].S();
        Assert.Empty((await preparer.Get($"/commercialcontracts/{simpleId}/workflow")).Data!["transitions"]!.AsArray());
        Assert.Equal(403, (await Move(preparer, simpleId, "active")).Status);
    }

    [Fact]
    public async Task Simplified_contract_starts_draft_and_activates_only_through_its_own_transition()
    {
        var owner = await NewTenantAsync();
        var (party, _) = await SeedCustomerAsync(owner);
        var created = await owner.Post("/commercialcontracts", new
        {
            title = "صيانة", contractType = "monthly_maintenance", partyName = "عميل", partyId = party, contractValue = 1000, vatRate = 15,
            stage = "active", status = 3, // الحالة من المستخدم تُتجاهل
            items = Array.Empty<object>(), milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
        });
        Assert.Equal("draft", created.Data!["stage"].S());
        var id = created.Data["id"].S();

        var wf = (await owner.Get($"/commercialcontracts/{id}/workflow")).Data!;
        var toStatuses = wf["transitions"]!.AsArray().Select(t => t!["toStatus"].S()).ToList();
        Assert.Contains("active", toStatuses);
        Assert.DoesNotContain("review", toStatuses); // المراجعة للمسار الكامل
        Assert.Equal(409, (await Move(owner, id, "review")).Status); // ولا عبر API

        var blocked = await Move(owner, id, "active");
        Assert.Equal(409, blocked.Status);
        Assert.Contains("تاريخا البداية والنهاية", blocked.Body!["message"].S());
        Assert.Contains("بنود العقد أو مستخلصاته", blocked.Body!["message"].S());

        var body = new
        {
            title = "صيانة", contractType = "monthly_maintenance", partyName = "عميل", partyId = party, contractValue = 1000, vatRate = 15,
            startDate = DateTime.UtcNow.Date, endDate = DateTime.UtcNow.Date.AddMonths(12), stage = "active", // التعديل لا يغيّر الحالة
            items = Array.Empty<object>(), milestones = new[] { new { title = "دفعة", percentage = 100 } }, clauses = Array.Empty<object>(),
        };
        var edited = await owner.Put($"/commercialcontracts/{id}", body);
        Assert.Equal(200, edited.Status);
        Assert.Equal("draft", edited.Data!["stage"].S());

        var active = await Move(owner, id, "active");
        Assert.Equal(200, active.Status);
        Assert.Equal(new[] { "draft", "active" }, active.Data!["log"]!.AsArray().Select(l => l!["toStatus"].S()));
        Assert.NotNull((await owner.Get($"/commercialcontracts/{id}")).Data!["signedDate"]);
    }

    [Fact]
    public async Task Full_contract_cannot_use_the_simplified_shortcut_and_status_cannot_be_saved_outside_move()
    {
        var owner = await NewTenantAsync();
        var (party, _) = await SeedCustomerAsync(owner);
        var product = await SeedProductAsync(owner);
        var id = (await owner.Post("/commercialcontracts", ContractBody(party, product))).Data!["id"].S();
        Assert.Equal(409, (await Move(owner, id, "active")).Status); // مسودة ← ساري للمسار المبسط فقط
        Assert.Equal(409, (await owner.Post($"/commercialcontracts/{id}/AdvanceStage", new { stage = "closed" })).Status);

        // تحت الخدمات: الحفظ نفسه يرفض تغيير الحالة بلا سطر سجل لانتقال معرّف
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>();
        var c = await db.Set<CommercialContract>().IgnoreQueryFilters().FirstAsync(x => x.Id == Guid.Parse(id));
        c.Stage = "active";
        await Assert.ThrowsAsync<ConflictException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var log = await db.Set<ContractStatusLog>().IgnoreQueryFilters().FirstAsync(l => l.ContractId == Guid.Parse(id));
        log.ToStatus = "active";
        await Assert.ThrowsAsync<ConflictException>(() => db.SaveChangesAsync()); // السجل لا يُعدَّل
        db.ChangeTracker.Clear();

        Assert.Equal("draft", (await owner.Get($"/commercialcontracts/{id}")).Data!["stage"].S());
    }
}
