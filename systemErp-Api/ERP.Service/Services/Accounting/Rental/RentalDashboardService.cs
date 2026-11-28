using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using C = ERP.Core.Models.Accounting.RentalContractStatuses;
using L = ERP.Core.Models.Accounting.RentalLineStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// لوحة التأجير، قراءة فقط:
/// <list type="bullet">
/// <item>الأسطول: المعدات القابلة للتأجير؛ المؤجَّر = لها سطر «خارج» في عقد ساري، والمحجوز = سطر «معلّق» في عقد ساري أو حجز معلّق/مؤكد
/// يغطي اليوم، والصيانة = حالة المعدة صيانة أو خارج الخدمة، والمتاح الباقي. الاستغلال = المؤجَّر ÷ الأسطول.</item>
/// <item>اليوم: أسطر معلّقة يبدأ تسليمها اليوم، وأسطر خارجة ينتهي موعدها اليوم، ومهام نقل مخططة اليوم. المتأخر: أسطر خارجة انتهى موعدها قبل اليوم.</item>
/// <item>المال: الرسوم المعلّقة، والكشوف المعتمدة غير المفوترة، وفوترة الشهر (غير الملغاة بتاريخ إنشائها) وأعمدة آخر 14 يوماً.</item>
/// </list>
/// </summary>
public class RentalDashboardService : IRentalDashboardService
{
    private const int ListMax = 15;
    private readonly ErpDbContext _db;

    public RentalDashboardService(ErpDbContext db) => _db = db;

    public async Task<RentalDashboardDto> DashboardAsync(CancellationToken ct = default)
    {
        var now = SaudiTime.Now;
        var today = SaudiTime.Today;
        var tomorrow = today.AddDays(1);
        var monthStart = new DateTime(today.Year, today.Month, 1);

        var fleet = await _db.Set<Equipment>().AsNoTracking().Where(e => e.IsRentable)
            .Select(e => new { e.Id, e.Code, e.NameAr, e.Status, e.RentalCategoryId }).ToListAsync(ct);
        var lines = await (from l in _db.Set<RentalContractLine>().AsNoTracking()
                           join c in _db.Set<RentalContract>().AsNoTracking() on l.ContractId equals c.Id
                           where c.Status == C.Active && (l.Status == L.Out || l.Status == L.Pending)
                           select new LineRow { EquipmentId = l.EquipmentId, Status = l.Status, PlannedFrom = l.PlannedFrom, PlannedTo = l.PlannedTo,
                               ContractId = c.Id, ContractNumber = c.ContractNumber, CustomerId = c.CustomerId }).ToListAsync(ct);
        var bookings = _db.Set<RentalBooking>().AsNoTracking()
            .Where(b => b.Status == RentalBookingStatuses.Held || b.Status == RentalBookingStatuses.Confirmed);
        var bookedNow = await (from bl in _db.Set<RentalBookingLine>().AsNoTracking()
                               join b in bookings on bl.BookingId equals b.Id
                               where bl.EquipmentId != null && bl.From < tomorrow && bl.To >= today
                               select bl.EquipmentId!.Value).Distinct().ToListAsync(ct);

        var outIds = lines.Where(l => l.Status == L.Out).Select(l => l.EquipmentId).ToHashSet();
        var maintenance = fleet.Where(e => (e.Status == EquipmentStatuses.Maintenance || e.Status == EquipmentStatuses.OutOfService) && !outIds.Contains(e.Id))
            .Select(e => e.Id).ToHashSet();
        var reserved = lines.Where(l => l.Status == L.Pending).Select(l => l.EquipmentId).Concat(bookedNow)
            .Where(id => !outIds.Contains(id) && !maintenance.Contains(id) && fleet.Any(e => e.Id == id)).ToHashSet();
        var fleetOut = fleet.Count(e => outIds.Contains(e.Id));

        var customerIds = lines.Select(l => l.CustomerId).Distinct().ToList();
        var transport = await _db.Set<RentalTransportJob>().AsNoTracking()
            .Where(j => j.Status == RentalTransportStatuses.Planned && j.ScheduledAt >= today && j.ScheduledAt < tomorrow)
            .Join(_db.Set<RentalContract>().AsNoTracking(), j => j.ContractId, c => c.Id,
                (j, c) => new { j.JobNumber, j.Kind, j.ScheduledAt, j.ToLocation, c.Id, c.ContractNumber, c.CustomerId })
            .ToListAsync(ct);
        customerIds.AddRange(transport.Select(t => t.CustomerId));
        var customers = await _db.Set<Customer>().AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.NameAr, ct);
        var equipment = fleet.ToDictionary(e => e.Id);
        var missing = lines.Select(l => l.EquipmentId).Where(id => !equipment.ContainsKey(id)).Distinct().ToList();
        if (missing.Count > 0)
            foreach (var e in await _db.Set<Equipment>().AsNoTracking().Where(e => missing.Contains(e.Id))
                         .Select(e => new { e.Id, e.Code, e.NameAr, e.Status, e.RentalCategoryId }).ToListAsync(ct))
                equipment[e.Id] = e;

        RentalDashboardMoveDto Move(string kind, LineRow l, DateTime at) => new()
        {
            Kind = kind, ContractId = l.ContractId, ContractNumber = l.ContractNumber, CustomerName = customers.GetValueOrDefault(l.CustomerId),
            EquipmentName = equipment.TryGetValue(l.EquipmentId, out var e) ? e.NameAr : null,
            EquipmentCode = equipment.TryGetValue(l.EquipmentId, out var e2) ? e2.Code : null,
            At = at, DaysLate = Math.Max(0, (today - at.Date).Days),
        };

        var deliveries = lines.Where(l => l.Status == L.Pending && l.PlannedFrom >= today && l.PlannedFrom < tomorrow).ToList();
        var returns = lines.Where(l => l.Status == L.Out && l.PlannedTo >= today && l.PlannedTo < tomorrow).ToList();
        var overdue = lines.Where(l => l.Status == L.Out && l.PlannedTo < today).OrderBy(l => l.PlannedTo).ToList();

        var todayMoves = deliveries.Select(l => Move("delivery", l, l.PlannedFrom))
            .Concat(returns.Select(l => Move("return", l, l.PlannedTo)))
            .Concat(transport.Select(t => new RentalDashboardMoveDto
            {
                Kind = "transport", ContractId = t.Id, ContractNumber = t.ContractNumber, CustomerName = customers.GetValueOrDefault(t.CustomerId),
                At = t.ScheduledAt, Reference = t.JobNumber, EquipmentName = t.ToLocation,
            }))
            .OrderBy(m => m.At).Take(ListMax * 2).ToList();

        var timesheets = await _db.Set<RentalTimesheet>().AsNoTracking()
            .Where(t => t.Status == RentalTimesheetStatuses.Submitted || t.Status == RentalTimesheetStatuses.Draft || t.Status == RentalTimesheetStatuses.Approved)
            .GroupBy(t => t.Status).Select(g => new { g.Key, Count = g.Count(), Amount = g.Sum(t => t.Amount) }).ToListAsync(ct);
        var charges = await _db.Set<RentalCharge>().AsNoTracking().Where(c => c.Status == RentalChargeStatuses.Pending)
            .GroupBy(c => 1).Select(g => new { Count = g.Count(), Amount = g.Sum(c => c.Amount) }).FirstOrDefaultAsync(ct);

        var trendStart = today.AddDays(-13);
        var billings = await _db.Set<RentalBilling>().AsNoTracking()
            .Where(b => b.Status != RentalBillingStatuses.Cancelled && b.CreatedAt >= (trendStart < monthStart ? trendStart : monthStart) && b.CreatedAt < tomorrow)
            .Select(b => new { b.CreatedAt, b.NetAmount }).ToListAsync(ct);

        var categoryIds = fleet.Where(e => e.RentalCategoryId != null).Select(e => e.RentalCategoryId!.Value).Distinct().ToList();
        var categories = await _db.Set<RentalCategory>().AsNoTracking().Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new { c.NameAr, c.NameEn }, ct);

        return new RentalDashboardDto
        {
            FleetCount = fleet.Count, OutCount = fleetOut, ReservedCount = reserved.Count, MaintenanceCount = maintenance.Count,
            AvailableCount = Math.Max(0, fleet.Count - fleetOut - reserved.Count - maintenance.Count),
            UtilizationPercent = fleet.Count == 0 ? null : decimal.Round(fleetOut * 100m / fleet.Count, 1),
            ActiveContracts = await _db.Set<RentalContract>().AsNoTracking().CountAsync(c => c.Status == C.Active, ct),
            DraftContracts = await _db.Set<RentalContract>().AsNoTracking().CountAsync(c => c.Status == C.Draft, ct),
            OpenBookings = await bookings.CountAsync(ct),
            ExpiringHolds = await bookings.CountAsync(b => b.Status == RentalBookingStatuses.Held && b.HoldUntil != null && b.HoldUntil < now.AddDays(1), ct),
            DeliveriesToday = deliveries.Count, ReturnsToday = returns.Count, OverdueReturns = overdue.Count, TransportToday = transport.Count,
            SubmittedTimesheets = timesheets.FirstOrDefault(t => t.Key == RentalTimesheetStatuses.Submitted)?.Count ?? 0,
            DraftTimesheets = timesheets.FirstOrDefault(t => t.Key == RentalTimesheetStatuses.Draft)?.Count ?? 0,
            UnbilledApprovedAmount = timesheets.FirstOrDefault(t => t.Key == RentalTimesheetStatuses.Approved)?.Amount ?? 0,
            PendingCharges = charges?.Count ?? 0, PendingChargesAmount = charges?.Amount ?? 0,
            BilledThisMonth = billings.Where(b => b.CreatedAt >= monthStart).Sum(b => b.NetAmount),
            BillingsThisMonth = billings.Count(b => b.CreatedAt >= monthStart),
            Today = todayMoves,
            Overdue = overdue.Take(ListMax).Select(l => Move("return", l, l.PlannedTo)).ToList(),
            Categories = fleet.GroupBy(e => e.RentalCategoryId).Select(g => new RentalCategoryUseDto
            {
                CategoryId = g.Key, NameAr = g.Key is Guid k ? categories.GetValueOrDefault(k)?.NameAr : null,
                NameEn = g.Key is Guid k2 ? categories.GetValueOrDefault(k2)?.NameEn : null,
                Fleet = g.Count(), Out = g.Count(e => outIds.Contains(e.Id)),
            }).OrderByDescending(c => c.Fleet).ToList(),
            Trend = Enumerable.Range(0, 14).Select(i => trendStart.AddDays(i)).Select(d => new RentalDayAmountDto
            {
                Date = d, Count = billings.Count(b => b.CreatedAt.Date == d), Amount = billings.Where(b => b.CreatedAt.Date == d).Sum(b => b.NetAmount),
            }).ToList(),
        };
    }

    private sealed class LineRow
    {
        public Guid EquipmentId { get; init; }
        public string Status { get; init; } = string.Empty;
        public DateTime PlannedFrom { get; init; }
        public DateTime PlannedTo { get; init; }
        public Guid ContractId { get; init; }
        public string ContractNumber { get; init; } = string.Empty;
        public Guid CustomerId { get; init; }
    }
}
