using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// قواعد حجز المخزون للمشاريع المشتركة بين الحجز وتحويله وصرف المشروع (مصدر واحد للأرقام):
/// المتاح = الرصيد الفعلي في المستودع − المتبقي من الحجوزات النشطة (للمشاريع الأخرى عند صرف مشروع أو حجزه).
/// كل عملية تغيّر الالتزام تقفل الصنف في المستودع بقفل تطبيقي على المعاملة (<c>sp_getapplock</c>) قبل القراءة،
/// فحجزان أو حجز وصرف متزامنان على الرصيد نفسه يتسلسلان ولا يتجاوزان المتاح.
/// </summary>
public static class StockReservations
{
    private const decimal Tolerance = 0.0001m;

    public static decimal Remaining(StockReservation r) => r.Quantity - r.IssuedQuantity - r.ReleasedQuantity;

    /// <summary>يقفل الصنف في المستودع حتى نهاية المعاملة الحالية (إلزامية).</summary>
    public static async Task LockAsync(ErpDbContext db, Guid productId, Guid warehouseId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction == null)
            throw new InvalidOperationException("Stock reservation lock requires an active transaction.");
        var resource = $"stock:{db.CurrentTenantId:N}:{warehouseId:N}:{productId:N}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DECLARE @r int; EXEC @r = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000; IF @r < 0 THROW 51000, 'stock lock timeout', 1;",
            ct);
    }

    /// <summary>المتبقي من الحجوزات النشطة للصنف في المستودع، عدا مشروع بعينه (أو كلها).</summary>
    public static async Task<decimal> ReservedAsync(ErpDbContext db, Guid productId, Guid warehouseId, Guid? exceptContractId, CancellationToken ct)
        => await db.Set<StockReservation>().AsNoTracking()
            .Where(r => r.ProductId == productId && r.WarehouseId == warehouseId && r.Status == StockReservationStatuses.Active
                && (exceptContractId == null || r.ContractId != exceptContractId))
            .SumAsync(r => (decimal?)(r.Quantity - r.IssuedQuantity - r.ReleasedQuantity), ct) ?? 0m;

    /// <summary>
    /// خروج غير مخصص لمشروع (بيع أو تحويل عام بين المستودعات): يقفل الصنف في المستودع (إن كانت هناك معاملة) ويرفض ما يتجاوز
    /// الرصيد الفعلي ناقص كل الحجوزات النشطة فيه، فلا يستهلك البيع أو التحويل كميات مخصصة للمشاريع. بلا حجوزات: لا أثر.
    /// يُستدعى قبل إنقاص رصيد المستودع.
    /// </summary>
    public static async Task EnsureUnreservedAsync(ErpDbContext db, Guid productId, string name, Guid warehouseId, decimal qty, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction != null) await LockAsync(db, productId, warehouseId, ct);
        var reserved = await ReservedAsync(db, productId, warehouseId, null, ct);
        if (reserved <= 0) return;
        var onHand = await WarehouseStocks.QuantityAsync(db, productId, warehouseId, ct);
        var free = Math.Max(0, onHand - reserved);
        if (qty > free + Tolerance)
            throw new ConflictException(string.Format(Messages.StockReservedForProjects, name, qty.ToString("0.####"), free.ToString("0.####"),
                Math.Max(0, onHand).ToString("0.####"), reserved.ToString("0.####")));
    }

    /// <summary>
    /// قبل ترحيل تسليم مشروع: يقفل كل صنف، ويرفض ما يتجاوز الرصيد الفعلي ناقص المحجوز لمشاريع أخرى (المادة لم تُستلم في المستودع،
    /// أو محجوزة لغيره)، ثم يستهلك حجوزات المشروع نفسه في المستودع نفسه بالأقدم أولاً ويسجّل الاستهلاك لكل سطر لعكسه بالإلغاء.
    /// </summary>
    public static async Task ConsumeForIssueAsync(ErpDbContext db, ProjectMaterialIssue issue, Guid warehouseId, CancellationToken ct)
    {
        foreach (var g in issue.Lines.Where(l => l.Quantity > 0).GroupBy(l => l.ProductId).OrderBy(g => g.Key))
        {
            await LockAsync(db, g.Key, warehouseId, ct);
            var qty = g.Sum(l => l.Quantity);
            var onHand = await WarehouseStocks.QuantityAsync(db, g.Key, warehouseId, ct);
            var reservedForOthers = await ReservedAsync(db, g.Key, warehouseId, issue.ContractId, ct);
            var free = onHand - reservedForOthers;
            var name = g.First().ItemName;
            if (qty > onHand + Tolerance)
                throw new ValidationFailedException(string.Format(Messages.ProjectIssueExceedsWarehouseStock, name, qty, Math.Max(0, onHand)));
            if (qty > free + Tolerance)
                throw new ValidationFailedException(string.Format(Messages.ProjectIssueExceedsProjectAvailable, name, qty, Math.Max(0, free), onHand, reservedForOthers));

            var own = await db.Set<StockReservation>()
                .Where(r => r.ContractId == issue.ContractId && r.ProductId == g.Key && r.WarehouseId == warehouseId && r.Status == StockReservationStatuses.Active)
                .OrderBy(r => r.CreatedAt).ToListAsync(ct);
            foreach (var line in g.OrderBy(l => l.SortOrder))
            {
                var need = line.Quantity;
                foreach (var r in own)
                {
                    if (need <= 0) break;
                    var take = Math.Min(need, Remaining(r));
                    if (take <= 0) continue;
                    r.IssuedQuantity += take;
                    if (Remaining(r) <= Tolerance) r.Status = StockReservationStatuses.Closed;
                    db.Add(new StockReservationConsumption { ReservationId = r.Id, IssueId = issue.Id, IssueLineId = line.Id, Quantity = take });
                    need -= take;
                }
            }
        }
    }

    /// <summary>إلغاء تسليم مرحّل: يعيد ما استهلكه من الحجوزات (تعود الكمية للمستودع فيعود حجزها).</summary>
    public static async Task ReverseIssueAsync(ErpDbContext db, Guid issueId, CancellationToken ct)
    {
        var rows = await db.Set<StockReservationConsumption>().Where(c => c.IssueId == issueId && !c.IsReversed).ToListAsync(ct);
        if (rows.Count == 0) return;
        var ids = rows.Select(c => c.ReservationId).Distinct().ToList();
        var reservations = await db.Set<StockReservation>().Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        foreach (var c in rows)
        {
            var r = reservations[c.ReservationId];
            r.IssuedQuantity = Math.Max(0, r.IssuedQuantity - c.Quantity);
            if (Remaining(r) > Tolerance) r.Status = StockReservationStatuses.Active;
            c.IsReversed = true;
        }
    }
}
