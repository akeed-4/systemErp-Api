using ERP.Service.Data;
using AdjSt = ERP.Core.Models.Accounting.ProjectMaterialAdjustmentStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// تعديلات الاحتياجات المعتمدة من أوامر التغيير: الاقتراح عند اعتماد الأمر، والكمية المعتمدة لكل احتياج (الأساسية + المعتمد من
/// التعديلات). المصدر الواحد لما تقرؤه حدود الطلب والاستلام عبر <see cref="ProjectMaterialService"/>.
/// </summary>
internal static class ProjectMaterialAdjustments
{
    private const int Decimals = 4;

    /// <summary>مجموع فروق التعديلات المعتمدة لكل احتياج في العقد.</summary>
    public static async Task<Dictionary<Guid, decimal>> ApprovedChangesAsync(ErpDbContext db, Guid contractId, CancellationToken ct)
        => await (from l in db.Set<ProjectMaterialAdjustmentLine>().AsNoTracking()
                  join a in db.Set<ProjectMaterialAdjustment>().AsNoTracking() on l.AdjustmentId equals a.Id
                  where a.ContractId == contractId && a.Status == AdjSt.Approved && l.RequirementId != null
                  group l by l.RequirementId!.Value into g
                  select new { g.Key, Qty = g.Sum(x => x.QuantityChange) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);

    /// <summary>
    /// يقترح تعديل الاحتياجات لأمر تغيير معتمد، إن كانت للمشروع احتياجات معتمدة: لكل بند قائم تغيّرت كميته، كل احتياج عليه بفرق =
    /// تغيّر الكمية × معدله لكل وحدة (المحفوظ، أو المعتمد ÷ كمية البند قبل الأمر). البند الجديد غير مرتبط بالمكتبة فلا تُشتق مواده:
    /// يُنشأ التعديل ليضيف المراجع مواده. لا شيء يُقترح = لا تعديل. يُستدعى داخل معاملة الاعتماد بعد حفظ البنود الجديدة.
    /// </summary>
    public static async Task ProposeForVariationAsync(ErpDbContext db, ContractVariation v, CancellationToken ct)
    {
        var approved = await db.Set<ProjectMaterialPlan>().AsNoTracking()
            .AnyAsync(p => p.ContractId == v.ContractId && p.Status == ProjectMaterialPlanStatuses.Approved, ct);
        if (!approved || await db.Set<ProjectMaterialAdjustment>().AnyAsync(a => a.VariationId == v.Id, ct)) return;

        var requirements = await db.Set<ProjectMaterialRequirement>().AsNoTracking()
            .Where(r => r.ContractId == v.ContractId && r.ResourceId != null).ToListAsync(ct);
        var changes = await ApprovedChangesAsync(db, v.ContractId, ct);
        var adjustment = new ProjectMaterialAdjustment { ContractId = v.ContractId, VariationId = v.Id, VariationNumber = v.VariationNumber };
        var order = 0;
        var newItems = false;
        foreach (var line in v.Lines.OrderBy(l => l.SortOrder))
        {
            if (line.QuantityChange == 0) continue;
            if (line.ContractItemId is not Guid itemId)
            {
                newItems |= line.CreatedItemId != null;
                continue;
            }
            // الاحتياج على سطر القياس: الفرعي بمرجعه، والرئيسي بلا فرعي
            foreach (var r in requirements.Where(r => (r.SubItemId ?? r.BoqItemId) == itemId))
            {
                var current = r.BaselineQuantity + changes.GetValueOrDefault(r.Id);
                var rate = r.QuantityPerUnit > 0 ? r.QuantityPerUnit
                    : line.QuantityBefore is decimal before && before > 0 ? current / before : 0;
                if (rate <= 0) continue;
                var delta = Math.Round(line.QuantityChange * rate, Decimals);
                // النقص لا يتجاوز المعتمد الحالي (لا كمية سالبة)
                if (current + delta < 0) delta = -current;
                if (delta == 0) continue;
                adjustment.Lines.Add(new ProjectMaterialAdjustmentLine
                {
                    RequirementId = r.Id, BoqItemId = r.BoqItemId, SubItemId = r.SubItemId, ResourceId = r.ResourceId!.Value, ProductId = r.ProductId,
                    Unit = r.Unit, ItemQuantityChange = line.QuantityChange, QuantityPerUnit = Math.Round(rate, 6),
                    ProposedQuantityChange = delta, QuantityChange = delta, SortOrder = ++order,
                });
            }
        }
        if (adjustment.Lines.Count == 0 && !newItems) return;
        if (newItems) adjustment.Notes = "أضاف أمر التغيير بنوداً جديدة: أضف موادها قبل الاعتماد.";
        db.Add(adjustment);
        await db.SaveChangesAsync(ct);
    }
}
