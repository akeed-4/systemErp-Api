using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using A = ERP.Core.Models.Accounting.LineApprovalStatuses;
using St = ERP.Core.Models.Accounting.WorkOrderStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// تقارير الورشة (W5)، قراءة فقط من مستندات الوحدة وفواتيرها:
/// <list type="bullet">
/// <item>اللوحة: الأوامر القائمة بحالاتها، والمتأخر عن موعده، والعاجل، ومتوسط العمر، وأسباب التوقف، والسجلات المفتوحة الآن.</item>
/// <item>ربحية الأوامر المفوترة في الفترة (بتاريخ الفوترة): الإيراد قبل الضريبة من فاتورتها، والتكلفة الفعلية: ساعات العمل المغلقة، وصافي
/// القطع المصروفة، والخدمات الخارجية المنجزة.</item>
/// <item>الفنيون (بسجلات الفترة): دقائق العمل والتوقف وتكلفتها، والساعات المعيارية والإيراد لأسطر العمالة المفوترة موزّعة بنسبة دقائق كل فني
/// من دقائق السطر.</item>
/// <item>القطع: صافي المصروف وتكلفته في الفترة، والمفوتر منه (بما صُرف ضمن كمية السطر) لفواتير الفترة.</item>
/// </list>
/// </summary>
public class WorkshopReportService : IWorkshopReportService
{
    private static readonly string[] Billed = { St.Invoiced, St.Delivered, St.Closed };

    private readonly ErpDbContext _db;
    private readonly IWorkOrderService _orders;
    private readonly IWorkOrderExecutionService _execution;

    public WorkshopReportService(ErpDbContext db, IWorkOrderService orders, IWorkOrderExecutionService execution)
    {
        _db = db; _orders = orders; _execution = execution;
    }

    public async Task<WorkshopDashboardDto> DashboardAsync(CancellationToken ct = default)
    {
        var open = await _orders.ListAsync("open", null, null, null, ct);
        var now = SaudiTime.Now;
        var pauses = open.Where(o => o.Status == St.Paused).GroupBy(o => o.PauseReasonName ?? "—").ToDictionary(g => g.Key, g => g.Count());
        return new WorkshopDashboardDto
        {
            OpenCount = open.Count, OverdueCount = open.Count(o => o.IsOverdue), UrgentCount = open.Count(o => o.Priority == WorkOrderPriorities.Urgent),
            AverageAgeDays = open.Count == 0 ? 0 : decimal.Round((decimal)open.Average(o => (now - o.ReceivedAt).TotalDays), 1),
            ByStatus = open.GroupBy(o => o.Status).ToDictionary(g => g.Key, g => g.Count()),
            PausedByReason = pauses,
            Overdue = open.Where(o => o.IsOverdue).OrderBy(o => o.PromisedAt).Take(50).ToList(),
            ActiveLogs = await _execution.OpenLogsAsync(null, ct),
        };
    }

    public async Task<WorkshopProfitabilityDto> ProfitabilityAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var (start, end) = Range(from, to);
        var orders = await _db.Set<WorkOrder>().AsNoTracking()
            .Where(o => Billed.Contains(o.Status) && o.InvoicedAt >= start && o.InvoicedAt < end).OrderBy(o => o.InvoicedAt).ToListAsync(ct);
        var ids = orders.Select(o => o.Id).ToList();
        var invoiceIds = orders.Where(o => o.InvoiceId != null).Select(o => o.InvoiceId!.Value).ToList();
        var revenue = await _db.Set<InvoiceBase>().AsNoTracking().Where(i => invoiceIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.GrandTotal - i.VatTotal, ct);
        var labor = await _db.Set<WorkOrderTimeLog>().AsNoTracking()
            .Where(l => ids.Contains(l.WorkOrderId) && l.Kind == TimeLogKinds.Work && l.EndedAt != null)
            .GroupBy(l => l.WorkOrderId).Select(g => new { g.Key, Cost = g.Sum(l => l.Cost) }).ToDictionaryAsync(x => x.Key, x => x.Cost, ct);
        var parts = await _db.Set<WorkOrderPartIssue>().AsNoTracking().Where(i => ids.Contains(i.WorkOrderId))
            .GroupBy(i => i.WorkOrderId).Select(g => new { g.Key, Cost = g.Sum(i => i.Kind == PartIssueKinds.Issue ? i.TotalCost : -i.TotalCost) })
            .ToDictionaryAsync(x => x.Key, x => x.Cost, ct);
        var external = await _db.Set<WorkOrderExternalService>().AsNoTracking()
            .Where(x => ids.Contains(x.WorkOrderId) && x.Status == ExternalServiceStatuses.Completed)
            .GroupBy(x => x.WorkOrderId).Select(g => new { g.Key, Cost = g.Sum(x => x.ActualCost ?? 0) }).ToDictionaryAsync(x => x.Key, x => x.Cost, ct);

        var rows = orders.Select(o =>
        {
            var rev = o.InvoiceId is Guid inv ? revenue.GetValueOrDefault(inv) : 0;
            decimal l = labor.GetValueOrDefault(o.Id), p = parts.GetValueOrDefault(o.Id), x = external.GetValueOrDefault(o.Id);
            var cost = l + p + x;
            return new WorkOrderProfitRowDto
            {
                WorkOrderId = o.Id, OrderNumber = o.OrderNumber, InvoiceNumber = o.InvoiceNumber, InvoicedAt = o.InvoicedAt, CustomerName = o.CustomerName,
                PlateNumber = o.PlateSnapshot, IsWarrantyJob = o.IsWarrantyJob, Revenue = rev, LaborCost = l, PartsCost = p, ExternalCost = x, TotalCost = cost,
                Profit = rev - cost, MarginPercent = rev > 0 ? decimal.Round((rev - cost) / rev * 100m, 1) : null,
            };
        }).ToList();
        return new WorkshopProfitabilityDto
        {
            Rows = rows,
            Totals = new WorkshopReportTotalsDto { Orders = rows.Count, Revenue = rows.Sum(r => r.Revenue), Cost = rows.Sum(r => r.TotalCost), Profit = rows.Sum(r => r.Profit) },
        };
    }

    public async Task<List<TechnicianPerformanceRowDto>> TechniciansAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var (start, end) = Range(from, to);
        var logs = await _db.Set<WorkOrderTimeLog>().AsNoTracking().Where(l => l.EndedAt != null && l.StartedAt >= start && l.StartedAt < end).ToListAsync(ct);
        var lineIds = logs.Where(l => l.LineId != null).Select(l => l.LineId!.Value).Distinct().ToList();
        // أسطر عمالة مفوترة على العميل، وكل دقائقها (لنسبة كل فني)
        var lines = await (from l in _db.Set<WorkOrderLine>().AsNoTracking()
                           join o in _db.Set<WorkOrder>().AsNoTracking() on l.WorkOrderId equals o.Id
                           where lineIds.Contains(l.Id) && l.Kind == WorkOrderLineKinds.Labor && l.ApprovalStatus == A.Approved
                                 && l.BillingType == LineBillingTypes.Customer && o.InvoiceId != null && Billed.Contains(o.Status)
                           select l).ToListAsync(ct);
        var serviceIds = lines.Where(l => l.ServiceId != null).Select(l => l.ServiceId!.Value).Distinct().ToList();
        var hours = await _db.Set<WorkshopServiceDefinition>().AsNoTracking().Where(s => serviceIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.StandardHours, ct);
        var billedIds = lines.Select(l => l.Id).ToList();
        var lineMinutes = await _db.Set<WorkOrderTimeLog>().AsNoTracking().Where(l => l.LineId != null && billedIds.Contains(l.LineId.Value) && l.EndedAt != null)
            .GroupBy(l => l.LineId!.Value).Select(g => new { g.Key, Minutes = g.Sum(l => l.Minutes) }).ToDictionaryAsync(x => x.Key, x => x.Minutes, ct);
        var empIds = logs.Select(l => l.EmployeeId).Distinct().ToList();
        var names = await _db.Set<Employee>().AsNoTracking().Where(e => empIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.NameAr, ct);
        var pauseIds = logs.Where(l => l.PauseReasonId != null).Select(l => l.PauseReasonId!.Value).Distinct().ToList();
        var pauses = await _db.Set<WorkshopPauseReason>().AsNoTracking().Where(p => pauseIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.NameAr, ct);

        return logs.GroupBy(l => l.EmployeeId).Select(g =>
        {
            decimal billedHours = 0, revenue = 0;
            foreach (var share in g.Where(l => l.Kind == TimeLogKinds.Work && l.LineId != null).GroupBy(l => l.LineId!.Value))
            {
                var line = lines.FirstOrDefault(x => x.Id == share.Key);
                var total = lineMinutes.GetValueOrDefault(share.Key);
                if (line == null || total <= 0) continue;
                var ratio = share.Sum(l => l.Minutes) / total;
                var standard = (line.ServiceId is Guid s ? hours.GetValueOrDefault(s) : 0) * line.Quantity;
                billedHours += standard * ratio;
                revenue += WorkOrderRules.Amounts(line.Quantity, line.UnitPrice, line.DiscountAmount, line.VatRate).Net * ratio;
            }
            var work = g.Where(l => l.Kind == TimeLogKinds.Work).Sum(l => l.Minutes);
            return new TechnicianPerformanceRowDto
            {
                EmployeeId = g.Key, EmployeeName = names.GetValueOrDefault(g.Key), WorkMinutes = work,
                IdleMinutes = g.Where(l => l.Kind == TimeLogKinds.Idle).Sum(l => l.Minutes), BilledHours = decimal.Round(billedHours, 2),
                EfficiencyPercent = work > 0 ? decimal.Round(billedHours / (work / 60m) * 100m, 1) : null,
                LaborRevenue = decimal.Round(revenue, 2), LaborCost = g.Where(l => l.Kind == TimeLogKinds.Work).Sum(l => l.Cost),
                IdleReasons = g.Where(l => l.Kind == TimeLogKinds.Idle).GroupBy(l => l.PauseReasonId)
                    .Select(r => new IdleReasonDto { Reason = r.Key is Guid p ? pauses.GetValueOrDefault(p) : null, Minutes = r.Sum(l => l.Minutes) })
                    .OrderByDescending(r => r.Minutes).ToList(),
            };
        }).OrderByDescending(r => r.WorkMinutes).ToList();
    }

    public async Task<List<PartProfitRowDto>> PartsAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var (start, end) = Range(from, to);
        var issues = await _db.Set<WorkOrderPartIssue>().AsNoTracking().Where(i => i.Date >= start && i.Date < end).ToListAsync(ct);
        // المفوتر: أسطر قطع موافق عليها على العميل في أوامر فوترت في الفترة، بما صُرف ضمن كمية السطر
        var billedLines = await (from l in _db.Set<WorkOrderLine>().AsNoTracking()
                                 join o in _db.Set<WorkOrder>().AsNoTracking() on l.WorkOrderId equals o.Id
                                 where l.Kind == WorkOrderLineKinds.Part && l.ApprovalStatus == A.Approved && l.BillingType == LineBillingTypes.Customer
                                       && o.InvoiceId != null && Billed.Contains(o.Status) && o.InvoicedAt >= start && o.InvoicedAt < end
                                 select l).ToListAsync(ct);
        var billedIds = billedLines.Select(l => l.Id).ToList();
        var issuedPerLine = await _db.Set<WorkOrderPartIssue>().AsNoTracking().Where(i => billedIds.Contains(i.LineId))
            .GroupBy(i => i.LineId).Select(g => new { g.Key, Qty = g.Sum(i => i.Kind == PartIssueKinds.Issue ? i.Quantity : -i.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        var billed = billedLines.Select(l =>
        {
            var qty = Math.Min(l.Quantity, Math.Max(0, issuedPerLine.GetValueOrDefault(l.Id)));
            var discount = l.Quantity == 0 ? 0 : DocumentPricing.Round(l.DiscountAmount * qty / l.Quantity);
            return new { l.ProductId, Qty = qty, Revenue = qty <= 0 ? 0 : WorkOrderRules.Amounts(qty, l.UnitPrice, discount, l.VatRate).Net };
        }).ToList();
        var productIds = issues.Select(i => i.ProductId).Concat(billed.Select(b => b.ProductId)).Distinct().ToList();
        var products = await _db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => new { p.Sku, p.NameAr }, ct);
        return productIds.Select(pid =>
        {
            var mine = issues.Where(i => i.ProductId == pid).ToList();
            var cost = mine.Sum(i => i.Kind == PartIssueKinds.Issue ? i.TotalCost : -i.TotalCost);
            var revenue = billed.Where(b => b.ProductId == pid).Sum(b => b.Revenue);
            return new PartProfitRowDto
            {
                ProductId = pid, Sku = products.GetValueOrDefault(pid)?.Sku, Name = products.GetValueOrDefault(pid)?.NameAr,
                IssuedQuantity = mine.Sum(i => i.Kind == PartIssueKinds.Issue ? i.Quantity : -i.Quantity), IssuedCost = cost,
                BilledQuantity = billed.Where(b => b.ProductId == pid).Sum(b => b.Qty), Revenue = revenue, Profit = revenue - cost,
            };
        }).OrderByDescending(r => r.Revenue).ToList();
    }

    /// <summary>
    /// فريق الورشة: الفنيون المسجلون ومن أُسند إليهم أمر قائم أو سجّل في الفترة (النشطون فقط إلا من له سجل). الحالة الآن من السجل المفتوح،
    /// ودقائق اليوم تشمل المنقضي منه، وأداء الفترة من السجلات المغلقة بأيامها وأسباب توقفها، والساعات المعيارية والكفاءة من تقرير الفنيين، وأوامره
    /// القائمة المسندة إليه أو التي يعمل عليها الآن. بلا تكلفة أو إيراد.
    /// </summary>
    public async Task<WorkshopTeamDto> TeamAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var (start, end) = Range(from, to);
        var now = SaudiTime.Now;
        var today = SaudiTime.Today;
        var tomorrow = today.AddDays(1);

        var openLogs = await _execution.OpenLogsAsync(null, ct);
        var logs = await _db.Set<WorkOrderTimeLog>().AsNoTracking()
            .Where(l => l.EndedAt != null && ((l.StartedAt >= start && l.StartedAt < end) || (l.StartedAt >= today && l.StartedAt < tomorrow)))
            .Select(l => new { l.EmployeeId, l.WorkOrderId, l.Kind, l.PauseReasonId, l.StartedAt, l.Minutes }).ToListAsync(ct);
        var orders = await _db.Set<WorkOrder>().AsNoTracking().Where(o => St.Editable.Contains(o.Status))
            .Where(o => o.AssignedTechnicianId != null || openLogs.Select(l => l.WorkOrderId).Contains(o.Id)).ToListAsync(ct);
        var registered = await _db.Set<EmployeeStandardCostRate>().AsNoTracking().Where(x => x.Purpose == EmployeeCostPurposes.Technician)
            .Select(x => x.EmployeeId).Distinct().ToListAsync(ct);

        var logged = logs.Select(l => l.EmployeeId).Concat(openLogs.Select(l => l.EmployeeId)).ToHashSet();
        var ids = registered.Concat(orders.Where(o => o.AssignedTechnicianId != null).Select(o => o.AssignedTechnicianId!.Value)).Concat(logged).Distinct().ToList();
        var employees = await _db.Set<Employee>().AsNoTracking().Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, e.Code, e.NameAr, e.NameEn, e.JobTitle, e.Phone, e.UserId, e.Status }).ToListAsync(ct);
        employees = employees.Where(e => e.Status == EmployeeStatuses.Active || logged.Contains(e.Id)).OrderBy(e => e.NameAr).ToList();

        var performance = (await TechniciansAsync(from, to, ct)).ToDictionary(p => p.EmployeeId);
        var vehicleIds = orders.Select(o => o.VehicleId).Distinct().ToList();
        var plates = await _db.Set<ServiceVehicle>().AsNoTracking().Where(v => vehicleIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.PlateNumber, ct);
        var days = Enumerable.Range(0, (int)(end - start).TotalDays).Select(i => start.AddDays(i)).ToList();

        static decimal Overlap(DateTime? s, DateTime? e, DateTime from, DateTime to)
            => s is DateTime a && e is DateTime b && b > from && a < to ? (decimal)((b < to ? b : to) - (a > from ? a : from)).TotalMinutes : 0;

        return new WorkshopTeamDto
        {
            From = start, To = end.AddDays(-1),
            Members = employees.Select(e =>
            {
                var open = openLogs.FirstOrDefault(l => l.EmployeeId == e.Id);
                var running = open == null ? 0 : Math.Max(0, (decimal)(now - (open.StartedAt > today ? open.StartedAt : today)).TotalMinutes);
                var mineToday = logs.Where(l => l.EmployeeId == e.Id && l.StartedAt >= today && l.StartedAt < tomorrow).ToList();
                var mine = logs.Where(l => l.EmployeeId == e.Id && l.StartedAt >= start && l.StartedAt < end).ToList();
                var perf = performance.GetValueOrDefault(e.Id);
                var jobs = orders.Where(o => o.AssignedTechnicianId == e.Id || (open != null && o.Id == open.WorkOrderId)).ToList();
                return new WorkshopTeamMemberDto
                {
                    EmployeeId = e.Id, Code = e.Code, Name = e.NameAr, NameEn = e.NameEn, JobTitle = e.JobTitle, Phone = e.Phone, HasUser = e.UserId != null,
                    OpenLog = open,
                    TodayWorkMinutes = decimal.Round(mineToday.Where(l => l.Kind == TimeLogKinds.Work).Sum(l => l.Minutes) + (open?.Kind == TimeLogKinds.Work ? running : 0), 0),
                    TodayIdleMinutes = decimal.Round(mineToday.Where(l => l.Kind == TimeLogKinds.Idle).Sum(l => l.Minutes) + (open?.Kind == TimeLogKinds.Idle ? running : 0), 0),
                    TodayPlannedMinutes = (int)jobs.Where(o => o.AssignedTechnicianId == e.Id).Sum(o => Overlap(o.PlannedStartAt, o.PlannedEndAt, today, tomorrow)),
                    WorkMinutes = mine.Where(l => l.Kind == TimeLogKinds.Work).Sum(l => l.Minutes),
                    IdleMinutes = mine.Where(l => l.Kind == TimeLogKinds.Idle).Sum(l => l.Minutes),
                    OrdersWorked = mine.Where(l => l.Kind == TimeLogKinds.Work).Select(l => l.WorkOrderId).Distinct().Count(),
                    BilledHours = perf?.BilledHours ?? 0, EfficiencyPercent = perf?.EfficiencyPercent,
                    IdleReasons = perf?.IdleReasons ?? new(),
                    Days = days.Select(d => new WorkshopTeamDayDto
                    {
                        Date = d,
                        WorkMinutes = mine.Where(l => l.Kind == TimeLogKinds.Work && l.StartedAt >= d && l.StartedAt < d.AddDays(1)).Sum(l => l.Minutes),
                        IdleMinutes = mine.Where(l => l.Kind == TimeLogKinds.Idle && l.StartedAt >= d && l.StartedAt < d.AddDays(1)).Sum(l => l.Minutes),
                    }).ToList(),
                    Jobs = jobs.OrderByDescending(o => o.Priority == WorkOrderPriorities.Urgent).ThenBy(o => o.PlannedStartAt ?? o.PromisedAt ?? DateTime.MaxValue)
                        .Select(o => new WorkshopTeamJobDto
                        {
                            WorkOrderId = o.Id, OrderNumber = o.OrderNumber, Status = o.Status, Priority = o.Priority, CustomerName = o.CustomerName,
                            PlateNumber = o.PlateSnapshot ?? plates.GetValueOrDefault(o.VehicleId), PlannedStartAt = o.PlannedStartAt, PlannedEndAt = o.PlannedEndAt,
                            PromisedAt = o.PromisedAt,
                            IsOverdue = (o.PromisedAt is DateTime due && due < now && o.Status != St.Ready) || (o.PlannedEndAt is DateTime pe && pe < now),
                        }).ToList(),
                };
            }).ToList(),
        };
    }

    private static (DateTime Start, DateTime End) Range(DateTime from, DateTime to)
    {
        var start = from.Date;
        var end = to.Date.AddDays(1);
        if (end <= start) throw new ValidationFailedException(Messages.WorkshopReportRangeInvalid);
        if ((end - start).TotalDays > 400) throw new ValidationFailedException(Messages.WorkshopReportRangeTooLong);
        return (start, end);
    }
}
