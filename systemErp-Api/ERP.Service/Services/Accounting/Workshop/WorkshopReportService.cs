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

    private static (DateTime Start, DateTime End) Range(DateTime from, DateTime to)
    {
        var start = from.Date;
        var end = to.Date.AddDays(1);
        if (end <= start) throw new ValidationFailedException(Messages.WorkshopReportRangeInvalid);
        if ((end - start).TotalDays > 400) throw new ValidationFailedException(Messages.WorkshopReportRangeTooLong);
        return (start, end);
    }
}
