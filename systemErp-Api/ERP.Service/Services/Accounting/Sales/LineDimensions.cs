using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// موقع ومركز تكلفة كل سطر في المستندات التي تُحوَّل إلى فاتورة (عرض السعر، الأمر، بيان التسليم): المركز من المنشأة وفعّال،
/// والموقع من المنشأة وفعّال ومسموح للمستخدم (كموقع المستند). الفارغ = موقع المستند / بلا مركز. صلاحية تعيين المركز تُفحص في الفاتورة
/// عند التحويل (المستند المصدر لا يُصدر قيداً).
/// </summary>
internal static class LineDimensions
{
    public static async Task EnsureAsync(ErpDbContext db, IEnumerable<(Guid? WarehouseId, Guid? CostCenterId)> lines, CancellationToken ct)
    {
        var list = lines.ToList();
        await CostCenterRules.EnsureUsableAsync(db, list.Select(l => l.CostCenterId), ct);
        var sites = list.Where(l => l.WarehouseId.HasValue).Select(l => l.WarehouseId!.Value).Distinct().ToList();
        if (sites.Count == 0) return;
        var found = await db.Set<Warehouse>().AsNoTracking().Where(w => sites.Contains(w.Id))
            .Select(w => new { w.Id, w.Status }).ToDictionaryAsync(w => w.Id, ct);
        foreach (var id in sites)
        {
            if (!found.TryGetValue(id, out var site)) throw new ValidationFailedException(Messages.WarehouseNotFound);
            if (site.Status != "active") throw new ValidationFailedException(Messages.WarehouseInactive);
            if (!db.IsWarehouseAllowed(id)) throw new ForbiddenException(Messages.WarehouseNotAllowed);
        }
    }
}
