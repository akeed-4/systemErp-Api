using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>قواعد تكلفة الموارد المشتركة بين الموارد ومكتبة البنود.</summary>
internal static class ResourceCostRules
{
    private const int CostDecimals = 4;

    /// <summary>
    /// تكلفة كل مورد مركّب = Σ تكلفة المكوّن × كميته × (1 + الهدر)، والمكوّن المركّب يُحسب قبله (ترتيب الاعتماد).
    /// يعدّل الكيانات المتتبَّعة ويعيد عدد ما تغيّر (الحفظ على المستدعي).
    /// </summary>
    public static async Task<int> RecomputeCompositesAsync(ErpDbContext Db, CancellationToken ct)
    {
        var all = await Db.Set<ConstructionResource>().Include(r => r.Components).ToListAsync(ct);
        var byId = all.ToDictionary(r => r.Id);
        var memo = new Dictionary<Guid, decimal>();
        var path = new HashSet<Guid>();

        decimal Cost(ConstructionResource r)
        {
            if (r.CostSource != ResourceCostSources.Composite) return r.StandardCost;
            if (memo.TryGetValue(r.Id, out var known)) return known;
            if (!path.Add(r.Id)) return 0; // حماية من دورة قديمة في البيانات
            var total = r.Components.Sum(c => byId.TryGetValue(c.ComponentResourceId, out var child)
                ? Cost(child) * c.Quantity * (1 + c.WastePercent / 100m) : 0);
            path.Remove(r.Id);
            return memo[r.Id] = Math.Round(total, CostDecimals);
        }

        var now = DateTime.UtcNow;
        var changed = 0;
        foreach (var r in all.Where(r => r.CostSource == ResourceCostSources.Composite))
        {
            var cost = Cost(r);
            if (cost == r.StandardCost) continue;
            r.StandardCost = cost;
            r.CostUpdatedAt = now;
            changed++;
        }
        return changed;
    }
}
