using ERP.Core.Models.Accounting;
using ERP.Core.Models.Shared;
using ERP.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Tests;

/// <summary>المرحلة 1 من دورة العقد: الحالات والانتقالات في قاعدة البيانات، أنواع العقود لكل منشأة، وسجل الانتقالات من الإنشاء.</summary>
[Collection("api")]
public class ContractWorkflowSchemaTests : TestBase
{
    public ContractWorkflowSchemaTests(ErpFactory f) : base(f) { }

    [Fact]
    public async Task Status_catalog_has_three_phases_with_short_names_and_transitions_are_seeded()
    {
        var api = await NewTenantAsync();
        var stages = (await api.Get("/commercialcontracts/stages")).Data!["stages"]!.AsArray();
        var ids = stages.Select(s => s!["id"].S()).ToList();
        Assert.Equal(new[] { "draft", "review", "approval", "active", "initial_acceptance", "closed", "suspended", "cancelled" }, ids);
        Assert.All(stages, s => Assert.False(string.IsNullOrEmpty(s!["shortNameAr"].S())));
        Assert.DoesNotContain("milestone_billing", ids); // الفوترة مستند داخل «ساري» لا مرحلة
        Assert.Equal("execution", stages.First(s => s!["id"].S() == "active")!["phase"].S());

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>();
        var transitions = await db.Set<ContractTransition>().AsNoTracking().ToListAsync();
        Assert.Contains(transitions, t => t.FromStatus == "review" && t.ToStatus == "approval" && t.Condition == "both_reviews");
        Assert.Contains(transitions, t => t.FromStatus == "approval" && t.ToStatus == "draft" && t.RequiresNote);
        Assert.DoesNotContain(transitions, t => t.FromStatus == "active" && t.ToStatus == "cancelled"); // الإلغاء قبل التفعيل فقط
    }

    [Fact]
    public async Task New_tenant_gets_default_contract_types_and_simplified_contract_starts_active_with_log()
    {
        var api = await NewTenantAsync();
        var created = await api.Post("/commercialcontracts", new
        {
            title = "صيانة", contractType = "monthly_maintenance", partyName = "عميل", contractValue = 1000, vatRate = 15,
            milestones = Array.Empty<object>(), clauses = Array.Empty<object>(),
        });
        Assert.Equal(201, created.Status);
        Assert.Equal("active", created.Data!["stage"].S()); // مسار مبسط: حفظ واعتماد مباشرة
        Assert.True(created.Data["isSimplified"]!.GetValue<bool>()); // الصيانة الشهرية مسار مبسط
        var id = created.Data["id"].G();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>();
        var types = await db.Set<ContractTypeDefinition>().IgnoreQueryFilters().Where(t => t.TenantId == api.TenantId).ToListAsync();
        Assert.Equal(new[] { "supply_materials", "monthly_maintenance", "construction_project", "existing_contract" },
            types.OrderBy(t => t.SortOrder).Select(t => t.Code));
        Assert.Equal(ContractBilling.AfterReceipt, types.Single(t => t.Code == "supply_materials").Billing);

        var log = await db.Set<ContractStatusLog>().IgnoreQueryFilters().Where(l => l.ContractId == id).ToListAsync();
        Assert.Single(log);
        Assert.Null(log[0].FromStatus);
        Assert.Equal("active", log[0].ToStatus);
    }
}
