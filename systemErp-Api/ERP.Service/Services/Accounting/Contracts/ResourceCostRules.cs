using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>من أدخل سطر سجل التكلفة (فارغ = النظام).</summary>
internal readonly record struct CostAuthor(string? Name, Guid? UserId)
{
    public static CostAuthor From(ICurrentUser user) => new(user.Name, user.UserId);
}

/// <summary>
/// قواعد تكلفة الموارد المشتركة بين مكتبة التكاليف ومكتبة البنود وموارد المشروع: معادلة تكلفة الساعة، وتفصيل التكلفة،
/// وسجل التكلفة بتواريخ السريان (يُضاف إليه فقط)، والتكلفة السارية في تاريخ.
/// </summary>
internal static class ResourceCostRules
{
    private const int CostDecimals = 4;

    /// <summary>اليوم الذي تُقاس عليه التكلفة السارية.</summary>
    public static DateTime Today => DateTime.UtcNow.Date;

    // ---------- المعادلة ----------

    /// <summary>
    /// تكلفة الساعة = السعر ÷ ساعات أساسه (ساعة = 1، يوم = ساعات اليوم، شهر = ساعات اليوم × أيام الشهر) + تكاليف الساعة،
    /// والإضافية = تكلفة الساعة × المعامل. المعادلة الواحدة لمورد المشروع ومورد المكتبة.
    /// </summary>
    public static (decimal Hourly, decimal Overtime) HourlyCost(string basis, decimal rate, decimal hoursPerDay, decimal daysPerMonth, decimal perHour, decimal multiplier)
    {
        var basisHours = basis switch
        {
            ProjectRateBases.Day => hoursPerDay,
            ProjectRateBases.Month => hoursPerDay * daysPerMonth,
            _ => 1m,
        };
        if (basisHours <= 0) return (0, 0);
        var hourly = Math.Round(rate / basisHours + perHour, CostDecimals);
        return (hourly, Math.Round(hourly * multiplier, CostDecimals));
    }

    /// <summary>ناتج تفصيل التكلفة: تكلفة الوحدة والساعة والإضافية.</summary>
    public readonly record struct BreakdownResult(decimal UnitCost, decimal Hourly, decimal Overtime);

    /// <summary>يتحقق من التفصيل ويحسبه؛ الأخطاء تُضاف للقائمة (والناتج صفر معها).</summary>
    public static BreakdownResult Compute(ResourceCostBreakdownDto b, decimal hoursPerUnit, List<string> errors)
    {
        var before = errors.Count;
        b.RateBasis = string.IsNullOrWhiteSpace(b.RateBasis) ? ProjectRateBases.Hour : b.RateBasis.Trim().ToLowerInvariant();
        if (!ProjectRateBases.IsValid(b.RateBasis)) errors.Add(Messages.ProjectResourceBasisInvalid);
        if (b.Rate < 0 || b.FuelPerHour < 0 || b.MaintenancePerHour < 0 || b.DepreciationPerHour < 0 || b.OperatorPerHour < 0 || b.OtherPerHour < 0)
            errors.Add(Messages.ResourceCostNegative);
        if (b.RateBasis != ProjectRateBases.Hour && b.HoursPerDay is <= 0 or > 24) errors.Add(Messages.ProjectResourceHoursPerDayInvalid);
        if (b.RateBasis == ProjectRateBases.Month && b.DaysPerMonth is <= 0 or > 31) errors.Add(Messages.ProjectResourceDaysPerMonthInvalid);
        if (b.OvertimeMultiplier is < 1 or > 5) errors.Add(Messages.ProjectResourceOvertimeInvalid);
        if (hoursPerUnit <= 0) errors.Add(Messages.ResourceHoursPerUnitPositive);
        if (errors.Count > before) return default;

        var perHour = b.FuelPerHour + b.MaintenancePerHour + b.DepreciationPerHour + b.OperatorPerHour + b.OtherPerHour;
        var (hourly, overtime) = HourlyCost(b.RateBasis, b.Rate, b.HoursPerDay, b.DaysPerMonth, perHour, b.OvertimeMultiplier);
        if (hourly <= 0) errors.Add(Messages.ResourceBreakdownCostRequired);
        return new BreakdownResult(Math.Round(hourly * hoursPerUnit, CostDecimals), hourly, overtime);
    }

    // ---------- السجل ----------

    /// <summary>عملة المنشأة: عملة سجل التكلفة في هذه المرحلة.</summary>
    public static async Task<string> CurrencyAsync(ErpDbContext db, CancellationToken ct)
        => await db.Set<Tenant>().AsNoTracking().Select(t => t.Currency).FirstOrDefaultAsync(ct) is { Length: > 0 } c ? c : "SAR";

    /// <summary>
    /// يضيف سطر سجل بتكلفة المورد الحالية (<see cref="ConstructionResource.StandardCost"/>) ومصدره ووحدته، أو بتكلفة صريحة
    /// (<paramref name="unitCost"/>) لسعر لا يسري اليوم. الحفظ على المستدعي.
    /// </summary>
    public static ConstructionResourceCost Record(ErpDbContext db, ConstructionResource r, DateTime effectiveFrom, string? reason,
        CostAuthor author, string currency, ResourceCostBreakdownDto? breakdown = null, BreakdownResult? computed = null, decimal? unitCost = null)
    {
        var row = new ConstructionResourceCost
        {
            ResourceId = r.Id, EffectiveFrom = effectiveFrom.Date, UnitCost = unitCost ?? r.StandardCost, Currency = currency,
            Source = r.CostSource, Unit = r.Unit, HoursPerUnit = r.HoursPerUnit,
            Reason = Trim(reason, 300), CreatedBy = Trim(author.Name, 200), CreatedByUserId = author.UserId,
        };
        if (breakdown != null)
        {
            row.RateBasis = breakdown.RateBasis; row.Rate = breakdown.Rate; row.HoursPerDay = breakdown.HoursPerDay;
            row.DaysPerMonth = breakdown.DaysPerMonth; row.FuelPerHour = breakdown.FuelPerHour; row.MaintenancePerHour = breakdown.MaintenancePerHour;
            row.DepreciationPerHour = breakdown.DepreciationPerHour; row.OperatorPerHour = breakdown.OperatorPerHour;
            row.OtherPerHour = breakdown.OtherPerHour; row.OvertimeMultiplier = breakdown.OvertimeMultiplier;
            row.HourlyCost = computed?.Hourly ?? 0; row.OvertimeHourlyCost = computed?.Overtime ?? 0;
        }
        db.Add(row);
        return row;
    }

    /// <summary>أسطر سجل الموارد السارية في تاريخ: أحدث سطر تاريخه ≤ التاريخ (والأحدث إدخالاً عند التساوي).</summary>
    public static async Task<Dictionary<Guid, ConstructionResourceCost>> CostAtAsync(ErpDbContext db, IReadOnlyCollection<Guid> ids, DateTime date, CancellationToken ct)
    {
        if (ids.Count == 0) return new();
        var day = date.Date;
        var rows = await db.Set<ConstructionResourceCost>().AsNoTracking()
            .Where(c => ids.Contains(c.ResourceId) && c.EffectiveFrom <= day).ToListAsync(ct);
        return rows.GroupBy(c => c.ResourceId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.EffectiveFrom).ThenByDescending(c => c.CreatedAt).First());
    }

    /// <summary>
    /// يعيد التكلفة المخزّنة على الموارد إلى سطر سجلها الساري اليوم (بعد سعر مجدول حلّ موعده، أو سطر أُدخل في الماضي).
    /// لا يضيف سطراً: القيمة من السجل نفسه. يعيد عدد ما تغيّر (الحفظ على المستدعي).
    /// </summary>
    public static async Task<int> SyncCacheAsync(ErpDbContext db, IEnumerable<ConstructionResource> resources, CancellationToken ct)
    {
        var list = resources.ToList();
        var current = await CostAtAsync(db, list.Select(r => r.Id).ToList(), Today, ct);
        var changed = 0;
        foreach (var r in list)
        {
            if (!current.TryGetValue(r.Id, out var row)) continue;
            if (r.StandardCost == row.UnitCost && r.CostEffectiveFrom == row.EffectiveFrom) continue;
            if (r.StandardCost != row.UnitCost) { r.StandardCost = row.UnitCost; r.CostUpdatedAt = DateTime.UtcNow; changed++; }
            r.CostEffectiveFrom = row.EffectiveFrom;
            db.MarkResourceCostFromHistory(r.Id);
        }
        return changed;
    }

    /// <summary>الموارد التي حلّ موعد سعر مجدول لها ولم تتبعه تكلفتها المخزّنة بعد.</summary>
    public static async Task<int> RollForwardAsync(ErpDbContext db, CancellationToken ct)
    {
        var today = Today;
        var due = await db.Set<ConstructionResourceCost>().AsNoTracking()
            .Join(db.Set<ConstructionResource>().AsNoTracking(), c => c.ResourceId, r => r.Id, (c, r) => new { c.ResourceId, c.EffectiveFrom, r.CostEffectiveFrom })
            .Where(x => x.EffectiveFrom <= today && (x.CostEffectiveFrom == null || x.EffectiveFrom > x.CostEffectiveFrom))
            .Select(x => x.ResourceId).Distinct().ToListAsync(ct);
        if (due.Count == 0) return 0;
        var resources = await db.Set<ConstructionResource>().Where(r => due.Contains(r.Id)).ToListAsync(ct);
        return await SyncCacheAsync(db, resources, ct);
    }

    // ---------- المركّب ----------

    /// <summary>
    /// تكلفة كل مورد مركّب = Σ تكلفة المكوّن × كميته × (1 + الهدر)، والمكوّن المركّب يُحسب قبله (ترتيب الاعتماد).
    /// يعدّل الكيانات المتتبَّعة ويضيف سطر سجل لكل ما تغيّر، ويعيد عدده (الحفظ على المستدعي).
    /// </summary>
    public static async Task<int> RecomputeCompositesAsync(ErpDbContext Db, CancellationToken ct, CostAuthor author = default)
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
        var changed = new List<ConstructionResource>();
        foreach (var r in all.Where(r => r.CostSource == ResourceCostSources.Composite))
        {
            var cost = Cost(r);
            if (cost == r.StandardCost) continue;
            r.StandardCost = cost;
            r.CostUpdatedAt = now;
            r.CostEffectiveFrom = Today;
            changed.Add(r);
        }
        if (changed.Count > 0)
        {
            var currency = await CurrencyAsync(Db, ct);
            foreach (var r in changed) Record(Db, r, Today, ResourceCostReasons.Composite, author, currency);
        }
        return changed.Count;
    }

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        return v.Length > max ? v[..max] : v;
    }
}
