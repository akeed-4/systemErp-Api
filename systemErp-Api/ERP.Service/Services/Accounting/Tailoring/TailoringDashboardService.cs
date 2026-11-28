using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using G = ERP.Core.Models.Accounting.TailoringGarmentStatuses;
using St = ERP.Core.Models.Accounting.TailoringOrderStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// لوحة الخياطة: أرقام الأوامر القائمة والقطع بحالاتها ومراحلها، والمتأخر، وبروفات اليوم، وتسليمات الشهر، والدفعات غير المستردة. فتحها
/// يشغّل فحص التنبيهات اليومي (<see cref="ITailoringAlertService.ScanAsync"/>).
/// </summary>
public class TailoringDashboardService : ITailoringDashboardService
{
    private const int ListMax = 10;
    private readonly ErpDbContext _db;
    private readonly ITailoringAlertService _alerts;

    public TailoringDashboardService(ErpDbContext db, ITailoringAlertService alerts) { _db = db; _alerts = alerts; }

    public async Task<TailoringDashboardDto> DashboardAsync(CancellationToken ct = default)
    {
        await _alerts.ScanAsync(ct);
        var today = SaudiTime.Today;
        var tomorrow = today.AddDays(1);
        var monthStart = new DateTime(today.Year, today.Month, 1);

        var orders = _db.Set<TailoringOrder>().AsNoTracking().Where(o => St.Open.Contains(o.Status));
        var pieces = await (from g in _db.Set<TailoringGarment>().AsNoTracking()
                            join o in orders on g.OrderId equals o.Id
                            where g.Status != G.Delivered && g.Status != G.Cancelled
                            group g by new { g.Status, g.CurrentStageId } into grp
                            select new { grp.Key.Status, grp.Key.CurrentStageId, Count = grp.Count() }).ToListAsync(ct);
        var stages = await _db.Set<TailoringStage>().AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.SortOrder).ToListAsync(ct);
        var deliveries = _db.Set<TailoringDelivery>().AsNoTracking()
            .Where(d => d.Status == TailoringDeliveryStatuses.Posted && d.Date >= monthStart && d.Date < tomorrow);

        var dto = new TailoringDashboardDto
        {
            OpenOrders = await orders.CountAsync(ct),
            LateOrders = await orders.CountAsync(o => o.ExpectedDeliveryDate != null && o.ExpectedDeliveryDate < today, ct),
            DueToday = await orders.CountAsync(o => o.ExpectedDeliveryDate == today, ct),
            ReadyOrders = await orders.CountAsync(o => o.Status == St.ReadyForDelivery, ct),
            ReadyPieces = pieces.Where(p => p.Status == G.Ready).Sum(p => p.Count),
            InProductionPieces = pieces.Where(p => p.Status is G.InProduction or G.AwaitingFitting or G.Modification).Sum(p => p.Count),
            QualityPieces = pieces.Where(p => p.Status == G.Quality).Sum(p => p.Count),
            OpenModifications = await _db.Set<TailoringModification>().AsNoTracking().CountAsync(m => m.Status == TailoringModificationStatuses.Open, ct),
            FittingsToday = await _db.Set<TailoringFitting>().AsNoTracking()
                .CountAsync(f => f.Status == TailoringFittingStatuses.Scheduled && f.ScheduledAt >= today && f.ScheduledAt < tomorrow, ct),
            DeliveriesThisMonth = await deliveries.CountAsync(ct),
            DeliveredThisMonth = await deliveries.SumAsync(d => (decimal?)d.TotalAmount, ct) ?? 0,
            AdvancesOutstanding = await orders.SumAsync(o => (decimal?)(o.AdvanceNet - o.AdvanceRecovered), ct) ?? 0,
        };

        dto.Stages.Add(new TailoringStageCountDto { Key = "pending", Count = pieces.Where(p => p.CurrentStageId == null && p.Status != G.Ready).Sum(p => p.Count) });
        dto.Stages.AddRange(stages.Select(s => new TailoringStageCountDto
        {
            StageId = s.Id, Key = s.Code, NameAr = s.NameAr, NameEn = s.NameEn, Count = pieces.Where(p => p.CurrentStageId == s.Id).Sum(p => p.Count),
        }));
        dto.Stages.Add(new TailoringStageCountDto { Key = "ready", Count = dto.ReadyPieces });

        dto.Late = (await orders.Where(o => o.ExpectedDeliveryDate != null && o.ExpectedDeliveryDate <= today)
                .OrderBy(o => o.ExpectedDeliveryDate).ThenByDescending(o => o.Priority == TailoringPriorities.Urgent).Take(ListMax)
                .Select(o => new { o.Id, o.OrderNumber, o.CustomerName, o.CustomerPhone, Due = o.ExpectedDeliveryDate!.Value, o.Status, o.Priority }).ToListAsync(ct))
            .Select(o => new TailoringLateOrderDto
            {
                OrderId = o.Id, OrderNumber = o.OrderNumber, CustomerName = o.CustomerName, CustomerPhone = o.CustomerPhone, DueDate = o.Due,
                DaysLate = (today - o.Due.Date).Days, Status = o.Status, Priority = o.Priority,
            }).ToList();

        dto.Fittings = await (from f in _db.Set<TailoringFitting>().AsNoTracking()
                              join o in _db.Set<TailoringOrder>().AsNoTracking() on f.OrderId equals o.Id
                              join g in _db.Set<TailoringGarment>().AsNoTracking() on f.GarmentId equals g.Id
                              where f.Status == TailoringFittingStatuses.Scheduled && f.ScheduledAt >= today && f.ScheduledAt < tomorrow
                              orderby f.ScheduledAt
                              select new TailoringFittingDto
                              {
                                  Id = f.Id, OrderId = o.Id, OrderNumber = o.OrderNumber, CustomerName = o.CustomerName, CustomerPhone = o.CustomerPhone,
                                  GarmentId = g.Id, GarmentCode = g.Code, ScheduledAt = f.ScheduledAt, Status = f.Status, Result = f.Result, Notes = f.Notes,
                              }).Take(ListMax).ToListAsync(ct);
        dto.NewOrdersThisMonth = await _db.Set<TailoringOrder>().AsNoTracking()
            .CountAsync(o => o.OrderDate >= monthStart && o.OrderDate < tomorrow && o.Status != St.Draft && o.Status != St.Cancelled, ct);

        var assigned = await (from g in _db.Set<TailoringGarment>().AsNoTracking()
                              join o in _db.Set<TailoringOrder>().AsNoTracking() on g.OrderId equals o.Id
                              where g.AssignedEmployeeId != null && g.Status != G.Cancelled
                                    && ((St.Open.Contains(o.Status) && g.Status != G.Delivered && g.Status != G.Ready) || (g.ReadyAt >= monthStart && g.ReadyAt < tomorrow))
                              select new { EmployeeId = g.AssignedEmployeeId!.Value, g.Status, g.ReadyAt, o.ExpectedDeliveryDate, Open = St.Open.Contains(o.Status) })
            .ToListAsync(ct);
        var tailorIds = assigned.Select(a => a.EmployeeId).Distinct().ToList();
        var tailorNames = await _db.Set<Employee>().AsNoTracking().Where(e => tailorIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.NameAr, ct);
        dto.Tailors = assigned.GroupBy(a => a.EmployeeId).Select(g =>
        {
            var active = g.Where(a => a.Open && a.Status != G.Ready && a.Status != G.Delivered).ToList();
            return new TailoringTailorLoadDto
            {
                EmployeeId = g.Key, Name = tailorNames.GetValueOrDefault(g.Key) ?? "—", ActivePieces = active.Count,
                LatePieces = active.Count(a => a.ExpectedDeliveryDate != null && a.ExpectedDeliveryDate < today),
                ReadyThisMonth = g.Count(a => a.ReadyAt >= monthStart && a.ReadyAt < tomorrow),
            };
        }).OrderByDescending(t => t.ActivePieces).ThenBy(t => t.Name).ToList();

        var trendStart = today.AddDays(-13);
        var trend = await _db.Set<TailoringDelivery>().AsNoTracking()
            .Where(d => d.Status == TailoringDeliveryStatuses.Posted && d.Date >= trendStart && d.Date < tomorrow)
            .Select(d => new { d.Date, d.TotalAmount }).ToListAsync(ct);
        dto.Trend = Enumerable.Range(0, 14).Select(i => trendStart.AddDays(i)).Select(d => new TailoringDayTrendDto
        {
            Date = d, Deliveries = trend.Count(x => x.Date.Date == d), Amount = trend.Where(x => x.Date.Date == d).Sum(x => x.TotalAmount),
        }).ToList();

        return dto;
    }
}
