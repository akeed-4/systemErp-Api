using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// تحليل أداء مشاريع المقاولات للإدارة (للقراءة فقط). المصادر واحدة لكل رقم فلا يُحسب شيء مرتين:
/// <list type="bullet">
/// <item>المشاريع وملخصها من <see cref="IConstructionProjectService.ListAsync"/> (نطاق الوصول إلى العقود يسري عليها، وكل استعلام بعدها مقيّد بمعرّفاتها).</item>
/// <item>الإنتاجية والإنجاز الفعلي وساعات العمالة والمعدات من التقارير اليومية <b>المعتمدة</b> وحدها (المستهدف والمنفَّذ بقيمة البند كما في التقرير).</item>
/// <item>المال كله من <see cref="IProjectCostService.ReportAsync"/> (القيود على مركز المشروع بعد استبعاد المحمّل مرتين)، وتكلفة الفترة من أسطره نفسها.</item>
/// <item>نقص العمالة والمعدات من <see cref="IProjectResourceCoverageService"/>، والحضور من سجل الحضور اليومي لمن يملك شاشة حضور الفريق.</item>
/// </list>
/// </summary>
public class ProjectPerformanceService : IProjectPerformanceService
{
    // حدود التنبيه
    private const decimal LowProductivity = 80m;
    private const decimal CriticalProductivity = 60m;
    private const decimal SpiWarning = 0.9m;
    private const decimal SpiCritical = 0.75m;
    private const decimal LowUtilization = 60m;
    private const decimal LowAttendance = 85m;
    private const int MissingReportDays = 3;
    /// <summary>المرحلة متأخرة: فعليها أقل من مخططها بأكثر من هذه النقاط (هامش ملخص المشروع نفسه).</summary>
    private const decimal PhaseBehindMargin = 10m;
    private const int MaxRangeDays = 366;
    private const int TopItems = 15;

    private static readonly string[] LaborKinds = { ProjectResourceKinds.Labor, ProjectResourceKinds.Engineer };

    private readonly ErpDbContext _db;
    private readonly IConstructionProjectService _projects;
    private readonly IProjectCostService _costs;
    private readonly IProjectResourceCoverageService _coverage;
    private readonly IPermissionService _permissions;

    public ProjectPerformanceService(ErpDbContext db, IConstructionProjectService projects, IProjectCostService costs,
        IProjectResourceCoverageService coverage, IPermissionService permissions)
    {
        _db = db; _projects = projects; _costs = costs; _coverage = coverage; _permissions = permissions;
    }

    public async Task<ProjectPerformanceDto> AnalyzeAsync(DateTime? from, DateTime? to, Guid? contractId, Guid? phaseId, CancellationToken ct = default)
    {
        var today = SaudiTime.Today;
        var end = (to?.Date ?? today) > today ? today : (to?.Date ?? today);
        var start = from?.Date ?? end.AddDays(-29);
        if (start > end || (end - start).Days + 1 > MaxRangeDays)
            throw new ValidationFailedException(Messages.ProjectPerformanceRangeInvalid);
        if (phaseId != null && contractId == null) throw new ValidationFailedException(Messages.ProjectPerformanceRangeInvalid);

        var all = await _projects.ListAsync(ct);
        var summaries = contractId is Guid only ? all.Where(p => p.ContractId == only).ToList() : all;
        if (contractId != null && summaries.Count == 0) throw new NotFoundException(Messages.ContractNotFound);

        var attendanceVisible = await _permissions.HasPermissionAsync(PermissionCatalog.ProjectAttendanceScreenId, ScreenAction.View, ct);
        var result = new ProjectPerformanceDto
        {
            From = start, To = end, Today = today, Bucket = BucketOf(start, end), AttendanceVisible = attendanceVisible,
            Thresholds = new ProjectPerformanceThresholdsDto
            {
                LowProductivityPercent = LowProductivity, CriticalProductivityPercent = CriticalProductivity, SpiWarning = SpiWarning, SpiCritical = SpiCritical,
                CpiWarning = SpiWarning, LowUtilizationPercent = LowUtilization, LowAttendancePercent = LowAttendance, MissingReportDays = MissingReportDays,
            },
        };
        if (summaries.Count == 0) return result;
        var ids = summaries.Select(s => s.ContractId).ToList();

        var data = await LoadAsync(ids, start, end, today, attendanceVisible, ct);
        if (phaseId is Guid ph && !data.Phases.Any(p => p.Id == ph)) throw new NotFoundException(Messages.ContractNotFound);
        var buckets = Buckets(start, end, result.Bucket);

        foreach (var s in summaries)
        {
            // المال والتغطية من خدماتهما (تسلسلياً على السياق نفسه)
            var cost = await _costs.ReportAsync(s.ContractId, ct);
            var running = IsRunning(s.Stage);
            var coverage = running ? await _coverage.GetAsync(s.ContractId, ct) : null;
            result.Projects.Add(Build(s, data, cost, coverage, phaseId, start, end, today, buckets, running));
        }
        return result;
    }

    // ---------- تحميل البيانات المحفوظة ----------

    private sealed record ItemInfo(Guid Id, Guid ContractId, string? LineCode, string Description, string Unit, decimal Quantity, decimal UnitPrice);
    private sealed record HourAgg(Guid ContractId, DateTime Date, Guid ItemId, decimal Target, decimal Actual, decimal PlannedTarget = 0, decimal PlannedActual = 0);
    private sealed record HourRaw(Guid ContractId, Guid ReportId, DateTime Date, int SlotStart, int SlotEnd, Guid ItemId, int Downtime, string? Reason);
    private sealed record Usage(Guid ContractId, DateTime Date, Guid ResourceId, string Name, string Kind, Guid? ItemId, decimal Hours, decimal Overtime, decimal Idle, decimal Standby);
    private sealed record ReportInfo(Guid ContractId, DateTime Date, string Status);
    private sealed record Attendance(Guid ContractId, string Status, int LateMinutes, int? WorkedMinutes, string? GpsStatus);
    private sealed record Milestone(Guid ContractId, int Number, string Title, DateTime DueDate, decimal Amount);

    private sealed class Data
    {
        public List<ItemInfo> Items { get; init; } = new();
        public List<ProjectPhase> Phases { get; init; } = new();
        public List<HourAgg> Cumulative { get; init; } = new();
        public List<HourRaw> HoursInRange { get; init; } = new();
        public List<Usage> Usages { get; init; } = new();
        public List<ReportInfo> Reports { get; init; } = new();
        public Dictionary<Guid, DateTime> LastApproved { get; init; } = new();
        public Dictionary<Guid, decimal> DailyHours { get; init; } = new();
        public List<(Guid ContractId, string Kind)> ActiveResources { get; init; } = new();
        public List<Attendance>? Attendance { get; init; }
        public List<Milestone> Milestones { get; init; } = new();
    }

    private async Task<Data> LoadAsync(List<Guid> ids, DateTime rangeFrom, DateTime rangeTo, DateTime today, bool attendance, CancellationToken ct)
    {
        var approved = SiteDailyReportStatuses.Approved;
        var items = (await _db.Set<CommercialContractItem>().AsNoTracking()
                .Where(i => ids.Contains(i.ContractId) && !i.IsGroup)
                .Select(i => new { i.Id, i.ContractId, i.LineCode, i.Description, i.Unit, i.Quantity, i.RevisedQuantity, i.UnitPrice }).ToListAsync(ct))
            .Select(i => new ItemInfo(i.Id, i.ContractId, i.LineCode, i.Description, i.Unit, i.RevisedQuantity != 0 ? i.RevisedQuantity : i.Quantity, i.UnitPrice))
            .ToList();
        var phases = await _db.Set<ProjectPhase>().AsNoTracking().Include(p => p.Items)
            .Where(p => ids.Contains(p.ContractId)).OrderBy(p => p.SortOrder).ToListAsync(ct);

        // المنفَّذ التراكمي حتى نهاية الفترة (للإنجاز الفعلي واتجاهه): مجمّع بالمشروع واليوم والبند
        var cumulative = (await (from h in _db.Set<SiteHourEntry>().AsNoTracking()
                                 join r in _db.Set<SiteDailyReport>().AsNoTracking() on h.ReportId equals r.Id
                                 where ids.Contains(r.ContractId) && r.Status == approved && r.ReportDate <= rangeTo
                                 group h by new { r.ContractId, r.ReportDate, h.ContractItemId } into g
                                 select new { g.Key.ContractId, g.Key.ReportDate, g.Key.ContractItemId, Target = g.Sum(x => x.TargetQuantity), Actual = g.Sum(x => x.ActualQuantity),
                                     PlannedTarget = g.Sum(x => x.PlannedTargetQuantity ?? 0), PlannedActual = g.Sum(x => x.PlannedTargetQuantity != null ? x.ActualQuantity : 0) })
                .ToListAsync(ct))
            .Select(x => new HourAgg(x.ContractId, x.ReportDate.Date, x.ContractItemId, x.Target, x.Actual, x.PlannedTarget, x.PlannedActual)).ToList();
        var raw = (await (from h in _db.Set<SiteHourEntry>().AsNoTracking()
                          join r in _db.Set<SiteDailyReport>().AsNoTracking() on h.ReportId equals r.Id
                          where ids.Contains(r.ContractId) && r.Status == approved && r.ReportDate >= rangeFrom && r.ReportDate <= rangeTo
                          select new { r.ContractId, h.ReportId, r.ReportDate, h.SlotStart, h.SlotEnd, h.ContractItemId, h.DowntimeMinutes, h.DowntimeReason })
                .ToListAsync(ct))
            .Select(x => new HourRaw(x.ContractId, x.ReportId, x.ReportDate.Date, x.SlotStart, x.SlotEnd, x.ContractItemId, x.DowntimeMinutes, x.DowntimeReason)).ToList();
        var usages = (await (from u in _db.Set<SiteResourceUsage>().AsNoTracking()
                             join r in _db.Set<SiteDailyReport>().AsNoTracking() on u.ReportId equals r.Id
                             join pr in _db.Set<ProjectResource>().AsNoTracking() on u.ProjectResourceId equals pr.Id
                             where ids.Contains(r.ContractId) && r.Status == approved && r.ReportDate >= rangeFrom && r.ReportDate <= rangeTo
                             select new { r.ContractId, r.ReportDate, u.ProjectResourceId, pr.Name, pr.Kind, u.ContractItemId, u.Hours, u.OvertimeHours, u.IdleHours, u.StandbyHours })
                .ToListAsync(ct))
            .Select(x => new Usage(x.ContractId, x.ReportDate.Date, x.ProjectResourceId, x.Name, x.Kind, x.ContractItemId, x.Hours, x.OvertimeHours, x.IdleHours, x.StandbyHours)).ToList();
        var reports = (await _db.Set<SiteDailyReport>().AsNoTracking()
                .Where(r => ids.Contains(r.ContractId) && r.ReportDate >= rangeFrom && r.ReportDate <= rangeTo)
                .Select(r => new { r.ContractId, r.ReportDate, r.Status }).ToListAsync(ct))
            .Select(r => new ReportInfo(r.ContractId, r.ReportDate.Date, r.Status)).ToList();
        var lastApproved = await _db.Set<SiteDailyReport>().AsNoTracking()
            .Where(r => ids.Contains(r.ContractId) && r.Status == approved && r.ReportDate <= today)
            .GroupBy(r => r.ContractId).Select(g => new { g.Key, Last = g.Max(r => r.ReportDate) }).ToDictionaryAsync(x => x.Key, x => x.Last.Date, ct);
        var dailyHours = (await _db.Set<ConstructionProjectProfile>().AsNoTracking().Where(p => ids.Contains(p.ContractId)).ToListAsync(ct))
            .ToDictionary(p => p.ContractId, p => ConstructionProjectService.HourSlots(p.WorkdayStartMinutes, p.WorkdayEndMinutes, p.BreakStartMinutes, p.BreakEndMinutes)
                .Sum(x => x.End - x.Start) / 60m);
        var resources = await _db.Set<ProjectResource>().AsNoTracking().Where(r => ids.Contains(r.ContractId) && r.IsActive)
            .Select(r => new { r.ContractId, r.Kind, r.EmployeeId }).ToListAsync(ct);

        List<Attendance>? records = null;
        if (attendance)
        {
            // الموظف المسند لمشروع واحد فقط في المجموعة يُنسب إليه سجله غير الميداني؛ المسند لأكثر من مشروع لا يُنسب (لا يُحتسب مرتين)
            var owner = resources.Where(r => r.EmployeeId != null && LaborKinds.Contains(r.Kind))
                .GroupBy(r => r.EmployeeId!.Value)
                .Where(g => g.Select(x => x.ContractId).Distinct().Count() == 1)
                .ToDictionary(g => g.Key, g => g.First().ContractId);
            var employeeIds = owner.Keys.ToList();
            records = (await _db.Set<AttendanceRecord>().AsNoTracking()
                    .Where(a => a.Date >= rangeFrom && a.Date <= rangeTo
                        && ((a.ContractId != null && ids.Contains(a.ContractId.Value)) || (a.ContractId == null && employeeIds.Contains(a.EmployeeId))))
                    .Select(a => new { a.EmployeeId, a.ContractId, a.Status, a.LateMinutes, a.WorkedMinutes, a.GpsStatus }).ToListAsync(ct))
                .Select(a => new Attendance(a.ContractId ?? owner[a.EmployeeId], a.Status, a.LateMinutes, a.WorkedMinutes, a.GpsStatus)).ToList();
        }

        var milestones = (await _db.Set<ContractMilestone>().AsNoTracking()
                .Where(m => ids.Contains(m.ContractId) && m.CompletionDate == null && m.DueDate < today
                    && (m.Status == ContractMilestoneStatus.Pending || m.Status == ContractMilestoneStatus.Due))
                .Select(m => new { m.ContractId, m.MilestoneNumber, m.Title, m.DueDate, m.TotalWithVat }).ToListAsync(ct))
            .Select(m => new Milestone(m.ContractId, m.MilestoneNumber, m.Title, m.DueDate.Date, m.TotalWithVat)).ToList();

        return new Data
        {
            Items = items, Phases = phases, Cumulative = cumulative, HoursInRange = raw, Usages = usages, Reports = reports, LastApproved = lastApproved,
            DailyHours = dailyHours, ActiveResources = resources.Select(r => (r.ContractId, r.Kind)).ToList(), Attendance = records, Milestones = milestones,
        };
    }

    // ---------- تحليل مشروع ----------

    private static ProjectPerformanceRowDto Build(ConstructionProjectSummaryDto s, Data d, ProjectCostReportDto cost, ProjectResourceCoverageDto? coverage,
        Guid? phaseId, DateTime from, DateTime to, DateTime today, List<(DateTime Start, DateTime End)> buckets, bool running)
    {
        var id = s.ContractId;
        var items = d.Items.Where(i => i.ContractId == id).ToDictionary(i => i.Id);
        var allPhases = d.Phases.Where(p => p.ContractId == id).ToList();
        var phases = phaseId is Guid ph ? allPhases.Where(p => p.Id == ph).ToList() : allPhases;
        var phaseOfItem = allPhases.SelectMany(p => p.Items.Select(i => (i.ContractItemId, p.Name))).GroupBy(x => x.ContractItemId).ToDictionary(g => g.Key, g => g.First().Name);
        var scope = phaseId != null ? phases.SelectMany(p => p.Items.Select(i => i.ContractItemId)).ToHashSet() : null;
        bool InScope(Guid item) => scope == null || scope.Contains(item);
        var window = phaseId != null && phases.Count == 1 ? (phases[0].PlannedStart.Date, phases[0].PlannedEnd.Date) : ((DateTime, DateTime)?)null;

        var cumulative = d.Cumulative.Where(h => h.ContractId == id && InScope(h.ItemId)).ToList();
        var hours = d.HoursInRange.Where(h => h.ContractId == id && InScope(h.ItemId)).ToList();
        // المورد على بند المرحلة، أو بلا بند في أيام المرحلة
        var usages = d.Usages.Where(u => u.ContractId == id && (scope == null
            || (u.ItemId is Guid ui ? scope.Contains(ui) : window is { } w && u.Date >= w.Item1 && u.Date <= w.Item2))).ToList();
        decimal Price(Guid item) => items.TryGetValue(item, out var i) ? i.UnitPrice : 0m;

        var row = new ProjectPerformanceRowDto { Summary = s };

        // ---- الإنتاجية ----
        var reports = d.Reports.Where(r => r.ContractId == id).ToList();
        var p = row.Productivity;
        p.ReportsApproved = reports.Count(r => r.Status == SiteDailyReportStatuses.Approved);
        p.ReportsPending = reports.Count(r => r.Status == SiteDailyReportStatuses.Submitted);
        p.ReportsDraft = reports.Count(r => r.Status == SiteDailyReportStatuses.Draft);
        var effStart = s.StartDate?.Date is DateTime cs && cs > from ? cs : from;
        var effEnd = s.EndDate?.Date is DateTime ce && ce < to ? ce : to;
        p.PeriodDays = effEnd >= effStart ? (effEnd - effStart).Days + 1 : 0;
        p.ReportedDays = reports.Where(r => r.Status == SiteDailyReportStatuses.Approved && r.Date >= effStart && r.Date <= effEnd).Select(r => r.Date).Distinct().Count();
        p.LastApprovedReport = d.LastApproved.TryGetValue(id, out var last) ? last : null;
        var started = s.StartDate?.Date is DateTime st0 ? st0 <= today : p.LastApprovedReport != null;
        if (started) p.DaysSinceLastReport = p.LastApprovedReport is DateTime lr ? (today - lr).Days : s.StartDate is DateTime sd ? (today - sd.Date).Days : (int?)null;

        var inRange = cumulative.Where(h => h.Date >= from).ToList();
        p.TargetValue = Money(inRange.Sum(h => h.Target * Price(h.ItemId)));
        p.ActualValue = Money(inRange.Sum(h => h.Actual * Price(h.ItemId)));
        p.ProductivityPercent = Pct(p.ActualValue, p.TargetValue);
        // ساعات العمل: فترات التقرير بلا تكرار؛ والتوقف أطول توقف في الفترة (كما في ملخص التقرير)
        var slots = hours.GroupBy(h => (h.ReportId, h.SlotStart, h.SlotEnd)).Select(g =>
        {
            var worst = g.OrderByDescending(x => x.Downtime).First();
            return (g.Key.ReportId, g.First().Date, Minutes: g.Key.SlotEnd - g.Key.SlotStart, worst.Downtime, worst.Reason);
        }).ToList();
        p.WorkingHours = Hrs(slots.Sum(x => x.Minutes));
        p.DowntimeHours = Hrs(slots.Sum(x => x.Downtime));
        p.Downtime = slots.Where(x => x.Downtime > 0).GroupBy(x => x.Reason ?? SiteDowntimeReasons.Other)
            .Select(g => new ProjectDowntimeDto { Reason = g.Key, Hours = Hrs(g.Sum(x => x.Downtime)) }).OrderByDescending(x => x.Hours).ToList();
        var executed = cumulative.GroupBy(h => h.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Actual));
        p.Items = inRange.GroupBy(h => h.ItemId).Where(g => items.ContainsKey(g.Key)).Select(g =>
        {
            var info = items[g.Key];
            var target = g.Sum(x => x.Target);
            var actual = g.Sum(x => x.Actual);
            var done = executed.GetValueOrDefault(g.Key);
            return new ProjectItemProductivityDto
            {
                ContractItemId = g.Key, LineCode = info.LineCode, Description = info.Description, Unit = info.Unit, PhaseName = phaseOfItem.GetValueOrDefault(g.Key),
                TargetQuantity = target, ActualQuantity = actual, ProductivityPercent = Pct(actual, target),
                PlannedTargetQuantity = g.Sum(x => x.PlannedTarget), PlannedProductivityPercent = Pct(g.Sum(x => x.PlannedActual), g.Sum(x => x.PlannedTarget)),
                ContractQuantity = info.Quantity, ExecutedToDate = done, RemainingQuantity = Math.Max(0, info.Quantity - done),
            };
        }).OrderBy(x => x.ProductivityPercent ?? decimal.MaxValue).Take(TopItems).ToList();

        // ---- العمالة والمعدات ----
        var labor = usages.Where(u => LaborKinds.Contains(u.Kind)).ToList();
        var l = row.Labor;
        l.Hours = labor.Sum(u => u.Hours); l.OvertimeHours = labor.Sum(u => u.Overtime); l.IdleHours = labor.Sum(u => u.Idle);
        l.ManDays = labor.Select(u => (u.Date, u.ResourceId)).Distinct().Count();
        l.UtilizationPercent = Pct(l.Hours + l.OvertimeHours, l.Hours + l.OvertimeHours + l.IdleHours);
        l.AssignedWorkers = d.ActiveResources.Count(r => r.ContractId == id && LaborKinds.Contains(r.Kind));
        if (d.Attendance != null)
        {
            var att = d.Attendance.Where(a => a.ContractId == id).ToList();
            var a = new ProjectAttendanceSummaryDto
            {
                PresentDays = att.Count(x => x.Status == AttendanceStatuses.Present),
                AbsentDays = att.Count(x => x.Status == AttendanceStatuses.Absent),
                LeaveDays = att.Count(x => x.Status == AttendanceStatuses.Leave),
                LateMinutes = att.Sum(x => x.LateMinutes),
                ApprovedHours = Hrs(att.Where(x => GpsAttendanceStatuses.IsApproved(x.GpsStatus)).Sum(x => x.WorkedMinutes ?? 0)),
                PendingExceptions = att.Count(x => x.GpsStatus == GpsAttendanceStatuses.ExceptionPending),
            };
            a.AttendancePercent = Pct(a.PresentDays, a.PresentDays + a.AbsentDays);
            l.Attendance = a;
        }
        var equipment = usages.Where(u => ProjectResourceKinds.IsEquipment(u.Kind)).ToList();
        var e = row.Equipment;
        var daily = d.DailyHours.GetValueOrDefault(id);
        e.OperatingHours = equipment.Sum(u => u.Hours + u.Overtime); e.IdleHours = equipment.Sum(u => u.Idle); e.StandbyHours = equipment.Sum(u => u.Standby);
        e.EquipmentDays = equipment.Select(u => (u.Date, u.ResourceId)).Distinct().Count();
        e.UtilizationPercent = Pct(e.OperatingHours, e.OperatingHours + e.IdleHours + e.StandbyHours);
        e.CapacityUtilizationPercent = daily > 0 ? Pct(e.OperatingHours, e.EquipmentDays * daily) : null;
        e.ActiveEquipment = d.ActiveResources.Count(r => r.ContractId == id && ProjectResourceKinds.IsEquipment(r.Kind));
        e.Units = equipment.GroupBy(u => u.ResourceId).Select(g =>
        {
            var op = g.Sum(x => x.Hours + x.Overtime);
            var idle = g.Sum(x => x.Idle);
            var standby = g.Sum(x => x.Standby);
            return new ProjectEquipmentUsageDto
            {
                ProjectResourceId = g.Key, Name = g.First().Name, Kind = g.First().Kind, OperatingHours = op, IdleHours = idle, StandbyHours = standby,
                Days = g.Select(x => x.Date).Distinct().Count(), UtilizationPercent = Pct(op, op + idle + standby),
            };
        }).OrderBy(x => x.UtilizationPercent ?? decimal.MaxValue).ToList();

        // ---- الجدول الزمني ----
        var sc = row.Schedule;
        decimal? PhaseActual(ProjectPhase phase, DateTime day) => PhaseFraction(phase, items, cumulative, day);
        foreach (var phase in phases)
        {
            var plannedFrac = ConstructionProjectService.PhaseFraction(phase.PlannedStart, phase.PlannedEnd, today);
            var actualFrac = PhaseActual(phase, today);
            var dto = new ProjectPhasePerformanceDto
            {
                Id = phase.Id, Name = phase.Name, PlannedStart = phase.PlannedStart.Date, PlannedEnd = phase.PlannedEnd.Date, WeightPercent = phase.WeightPercent,
                PlannedCost = phase.PlannedCost, PlannedPercent = Math.Round(plannedFrac * 100m, 2),
                ActualPercent = actualFrac is decimal af ? Math.Round(af * 100m, 2) : null,
            };
            var act = dto.ActualPercent ?? 0m;
            if (act >= 99.99m) dto.Status = "completed";
            else if (today > phase.PlannedEnd.Date) { dto.Status = "overdue"; dto.DelayDays = (today - phase.PlannedEnd.Date).Days; }
            else if (today < phase.PlannedStart.Date && act <= 0) dto.Status = "not_started";
            else if (dto.PlannedPercent - act > PhaseBehindMargin) dto.Status = "delayed";
            else dto.Status = act > 0 || today >= phase.PlannedStart.Date ? "in_progress" : "not_started";
            if (dto.Status != "completed" && actualFrac is decimal f && f > 0 && today >= phase.PlannedStart.Date)
            {
                dto.ForecastEnd = Forecast(phase.PlannedStart.Date, today, f);
                if (dto.Status == "delayed" && dto.ForecastEnd > phase.PlannedEnd.Date) dto.DelayDays = (dto.ForecastEnd.Value - phase.PlannedEnd.Date).Days;
            }
            sc.Phases.Add(dto);
        }
        sc.PhasesCompleted = sc.Phases.Count(x => x.Status == "completed");
        sc.PhasesInProgress = sc.Phases.Count(x => x.Status == "in_progress");
        sc.PhasesNotStarted = sc.Phases.Count(x => x.Status == "not_started");
        sc.PhasesDelayed = sc.Phases.Count(x => x.Status == "delayed");
        sc.PhasesOverdue = sc.Phases.Count(x => x.Status == "overdue");
        (decimal Planned, decimal Actual) Progress(DateTime day) => ProjectProgress(phases, items, cumulative, scope, day);
        var now = Progress(today);
        sc.PlannedPercent = now.Planned;
        sc.ActualPercent = now.Actual;
        sc.Spi = sc.PlannedPercent > 0 ? Math.Round(sc.ActualPercent / sc.PlannedPercent, 3) : null;
        var projectStart = phaseId != null && phases.Count == 1 ? phases[0].PlannedStart.Date
            : s.StartDate?.Date ?? (allPhases.Count > 0 ? allPhases.Min(x => x.PlannedStart.Date) : (DateTime?)null);
        var projectEnd = phaseId != null && phases.Count == 1 ? phases[0].PlannedEnd.Date : s.EndDate?.Date;
        if (sc.ActualPercent >= 99.99m) { sc.ForecastBasis = "completed"; sc.ForecastEnd = p.LastApprovedReport; }
        else if (sc.ActualPercent > 0 && projectStart is DateTime ps && today >= ps)
        {
            sc.ForecastBasis = "rate";
            sc.ForecastEnd = Forecast(ps, today, sc.ActualPercent / 100m);
        }
        if (sc.ForecastEnd is DateTime fe && projectEnd is DateTime pe) sc.ForecastDelayDays = (fe - pe).Days;
        sc.OverdueMilestones = phaseId != null ? new() : d.Milestones.Where(m => m.ContractId == id).OrderBy(m => m.DueDate)
            .Select(m => new ProjectMilestoneStatusDto { Number = m.Number, Title = m.Title, DueDate = m.DueDate, DaysOverdue = (today - m.DueDate).Days, Amount = m.Amount }).ToList();

        // ---- المال (من تقرير التكلفة وحده) ----
        var fin = row.Financial;
        fin.ContractValue = cost.ContractValue; fin.Budget = cost.BudgetAtCompletion; fin.ActualCost = cost.ActualCost;
        fin.PeriodCost = cost.Lines.Where(x => x.Date.Date >= from && x.Date.Date <= to).Sum(x => x.Amount);
        fin.RemainingBudget = cost.BudgetAtCompletion - cost.ActualCost;
        fin.BudgetUsedPercent = cost.BudgetAtCompletion > 0 ? Math.Round(cost.ActualCost / cost.BudgetAtCompletion * 100m, 2) : null;
        fin.EarnedValue = cost.EarnedValue; fin.CostVariance = cost.EarnedValue - cost.ActualCost; fin.Cpi = cost.CostPerformanceIndex;
        fin.EstimateAtCompletion = cost.EstimateAtCompletion; fin.EstimateToComplete = cost.EstimateToComplete; fin.ForecastOverrun = cost.ForecastOverrun;
        fin.ForecastProfit = cost.ForecastProfit; fin.ForecastMarginPercent = cost.ForecastMarginPercent;
        fin.MissingCostCenter = cost.MissingCostCenter; fin.UnpostedSiteReports = cost.UnpostedSiteReports;

        // ---- الاتجاه ----
        foreach (var (bs, be) in buckets)
        {
            var bh = cumulative.Where(h => h.Date >= bs && h.Date <= be).ToList();
            var bu = usages.Where(u => u.Date >= bs && u.Date <= be).ToList();
            var progress = Progress(be);
            row.Trend.Add(new ProjectPerformancePointDto
            {
                Start = bs, End = be,
                TargetValue = Money(bh.Sum(h => h.Target * Price(h.ItemId))), ActualValue = Money(bh.Sum(h => h.Actual * Price(h.ItemId))),
                LaborHours = bu.Where(u => LaborKinds.Contains(u.Kind)).Sum(u => u.Hours + u.Overtime),
                EquipmentHours = bu.Where(u => ProjectResourceKinds.IsEquipment(u.Kind)).Sum(u => u.Hours + u.Overtime),
                DowntimeHours = Hrs(slots.Where(x => x.Date >= bs && x.Date <= be).Sum(x => x.Downtime)),
                Cost = cost.Lines.Where(x => x.Date.Date >= bs && x.Date.Date <= be).Sum(x => x.Amount),
                PlannedPercent = progress.Planned, ActualPercent = progress.Actual,
            });
        }

        row.Alerts = Alerts(s, row, coverage, running, phaseId != null);
        row.Health = row.Alerts.Any(x => x.Severity == "critical") ? "risk" : row.Alerts.Any(x => x.Severity == "warning") ? "watch" : "good";
        return row;
    }

    /// <summary>
    /// نسبة إنجاز المرحلة في يوم: قيمة المنفَّذ المعتمد حتى اليوم (محدوداً بكمية البند) ÷ قيمة بنودها؛ وبنود بلا سعر بمتوسط نسب كمياتها.
    /// فارغة لمرحلة بلا بنود قابلة للقياس.
    /// </summary>
    private static decimal? PhaseFraction(ProjectPhase phase, Dictionary<Guid, ItemInfo> items, List<HourAgg> cumulative, DateTime day)
    {
        var lines = phase.Items.Select(i => i.ContractItemId).Where(items.ContainsKey).Select(i => items[i]).Where(i => i.Quantity > 0).ToList();
        if (lines.Count == 0) return null;
        var ids = lines.Select(i => i.Id).ToHashSet();
        var done = cumulative.Where(h => h.Date <= day && ids.Contains(h.ItemId)).GroupBy(h => h.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Actual));
        decimal Done(ItemInfo i) => Math.Min(i.Quantity, Math.Max(0, done.GetValueOrDefault(i.Id)));
        var value = lines.Sum(i => i.Quantity * i.UnitPrice);
        return value > 0
            ? lines.Sum(i => Done(i) * i.UnitPrice) / value
            : lines.Average(i => Done(i) / i.Quantity);
    }

    /// <summary>
    /// المخطط والفعلي للمشروع في يوم بأوزان المراحل نفسها (Σ الوزن × النسبة ÷ Σ الأوزان)؛ وبلا مراحل قابلة للقياس فالفعلي بقيمة المنفَّذ من بنود العقد.
    /// </summary>
    private static (decimal Planned, decimal Actual) ProjectProgress(List<ProjectPhase> phases, Dictionary<Guid, ItemInfo> items, List<HourAgg> cumulative, HashSet<Guid>? scope, DateTime day)
    {
        var weight = phases.Sum(p => p.WeightPercent);
        var measured = phases.Select(p => (p.WeightPercent, Fraction: PhaseFraction(p, items, cumulative, day))).ToList();
        if (weight > 0 && measured.Any(m => m.Fraction != null))
        {
            var planned = phases.Sum(p => p.WeightPercent * ConstructionProjectService.PhaseFraction(p.PlannedStart, p.PlannedEnd, day)) / weight * 100m;
            var actual = measured.Sum(m => m.WeightPercent * (m.Fraction ?? 0m)) / weight * 100m;
            return (Math.Round(planned, 2), Math.Round(actual, 2));
        }
        var plannedOnly = weight > 0 ? Math.Round(phases.Sum(p => p.WeightPercent * ConstructionProjectService.PhaseFraction(p.PlannedStart, p.PlannedEnd, day)) / weight * 100m, 2) : 0m;
        var lines = items.Values.Where(i => i.Quantity > 0 && i.UnitPrice > 0 && (scope == null || scope.Contains(i.Id))).ToList();
        var total = lines.Sum(i => i.Quantity * i.UnitPrice);
        if (total <= 0) return (plannedOnly, 0m);
        var done = cumulative.Where(h => h.Date <= day).GroupBy(h => h.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Actual));
        var value = lines.Sum(i => Math.Min(i.Quantity, Math.Max(0, done.GetValueOrDefault(i.Id))) * i.UnitPrice);
        return (plannedOnly, Math.Round(value / total * 100m, 2));
    }

    /// <summary>النهاية المتوقعة بمعدل الإنجاز الفعلي منذ البداية: المدة الكلية = الأيام المنقضية ÷ النسبة المنجزة.</summary>
    private static DateTime Forecast(DateTime start, DateTime today, decimal fraction)
    {
        var elapsed = (today - start).Days + 1;
        var total = (int)Math.Ceiling(elapsed / fraction);
        return start.AddDays(Math.Max(total, elapsed) - 1);
    }

    // ---------- التنبيهات والإجراءات ----------

    private static List<ProjectManagementAlertDto> Alerts(ConstructionProjectSummaryDto s, ProjectPerformanceRowDto row, ProjectResourceCoverageDto? coverage, bool running, bool phaseView)
    {
        var list = new List<ProjectManagementAlertDto>();
        void Add(string code, string severity, string category, string action, string section, decimal? value = null, decimal? threshold = null, string? reference = null)
            => list.Add(new ProjectManagementAlertDto { Code = code, Severity = severity, Category = category, Action = action, Section = section, Value = value, Threshold = threshold, Reference = reference });

        var sc = row.Schedule;
        var p = row.Productivity;
        var fin = row.Financial;
        if (running)
        {
            if (!phaseView && s.EndDate is DateTime end && end.Date < SaudiTime.Today)
                Add("contract_overdue", "critical", "schedule", "extend_or_recover", "variations", (SaudiTime.Today - end.Date).Days);
            if (sc.Spi is decimal spi && spi < SpiWarning)
                Add("schedule_behind", spi < SpiCritical ? "critical" : "warning", "schedule", "accelerate_schedule", "plan", spi, SpiWarning);
            if (sc.ForecastDelayDays is int delay && delay > 0)
                Add("forecast_late", delay > 30 ? "critical" : "warning", "schedule", "recovery_plan", "plan", delay);
            foreach (var ph in sc.Phases.Where(x => x.Status == "overdue"))
                Add("phase_overdue", "critical", "schedule", "expedite_phase", "plan", ph.DelayDays, null, ph.Name);
            foreach (var ph in sc.Phases.Where(x => x.Status == "delayed"))
                Add("phase_delayed", "warning", "schedule", "reallocate_resources", "resources", ph.PlannedPercent - (ph.ActualPercent ?? 0), PhaseBehindMargin, ph.Name);
            foreach (var m in sc.OverdueMilestones)
                Add("milestone_overdue", "warning", "schedule", "follow_up_milestone", "certificates", m.DaysOverdue, null, m.Title);

            if (p.ProductivityPercent is decimal prod && prod < LowProductivity)
            {
                var top = p.Downtime.FirstOrDefault()?.Reason;
                Add("low_productivity", prod < CriticalProductivity ? "critical" : "warning", "productivity", ActionForDowntime(top), "reports", prod, LowProductivity, top);
            }
            if (p.DaysSinceLastReport is int gap && gap > MissingReportDays)
                Add("reports_missing", "warning", "reporting", "enforce_daily_reports", "reports", gap, MissingReportDays);

            var laborShort = coverage?.ByResource.Where(r => r.ResourceType == "labor").Sum(r => r.ShortageHours) ?? 0m;
            var laborDowntime = p.Downtime.Where(x => x.Reason == SiteDowntimeReasons.LaborShortage).Sum(x => x.Hours);
            if (!phaseView && laborShort > 0) Add("labor_shortage", "warning", "resources", "assign_labor", "resources", laborShort);
            else if (laborDowntime > 0) Add("labor_shortage", "warning", "resources", "assign_labor", "resources", laborDowntime, null, SiteDowntimeReasons.LaborShortage);
            var equipmentShort = coverage?.ByResource.Where(r => r.ResourceType == "equipment").Sum(r => r.ShortageHours) ?? 0m;
            var breakdown = p.Downtime.Where(x => x.Reason == SiteDowntimeReasons.EquipmentBreakdown).Sum(x => x.Hours);
            if (!phaseView && equipmentShort > 0) Add("equipment_shortage", "warning", "resources", "assign_equipment", "resources", equipmentShort);
            if (breakdown > 0) Add("equipment_breakdown", "warning", "resources", "schedule_maintenance", "resources", breakdown, null, SiteDowntimeReasons.EquipmentBreakdown);
            var materialDowntime = p.Downtime.Where(x => x.Reason == SiteDowntimeReasons.MaterialShortage).Sum(x => x.Hours);
            if (materialDowntime > 0) Add("material_shortage", "warning", "resources", "expedite_materials", "supply", materialDowntime, null, SiteDowntimeReasons.MaterialShortage);
            else if (!phaseView && s.MaterialsNotStocked > 0) Add("materials_not_stocked", "info", "resources", "link_materials", "materials", s.MaterialsNotStocked);
            if (row.Equipment.UtilizationPercent is decimal util && util < LowUtilization)
                Add("low_equipment_utilization", "warning", "resources", "release_idle_equipment", "resources", util, LowUtilization);
            if (row.Labor.Attendance?.AttendancePercent is decimal att && att < LowAttendance)
                Add("low_attendance", "warning", "resources", "review_attendance", "attendance", att, LowAttendance);
        }
        if (!phaseView)
        {
            if (fin.Budget > 0 && fin.ActualCost > fin.Budget)
                Add("budget_overrun", "critical", "cost", "cost_review", "costs", fin.ActualCost - fin.Budget, fin.Budget);
            else if (fin.ForecastOverrun > 0)
                Add("forecast_overrun", fin.Budget > 0 && fin.ForecastOverrun > fin.Budget * 0.1m ? "critical" : "warning", "cost", "control_costs", "costs", fin.ForecastOverrun, fin.Budget);
            if (fin.MissingCostCenter) Add("missing_cost_center", "warning", "cost", "set_cost_center", "setup");
            if (fin.UnpostedSiteReports > 0) Add("unposted_site_costs", "info", "cost", "post_site_costs", "reports", fin.UnpostedSiteReports);
        }
        if (p.ReportsPending > 0) Add("reports_pending", "info", "reporting", "approve_reports", "reports", p.ReportsPending);
        if (row.Labor.Attendance?.PendingExceptions is int pe && pe > 0) Add("attendance_exceptions", "info", "resources", "review_attendance", "attendance", pe);

        var order = new Dictionary<string, int> { ["critical"] = 0, ["warning"] = 1, ["info"] = 2 };
        return list.OrderBy(a => order[a.Severity]).ToList();
    }

    /// <summary>الإجراء المقترح لانخفاض الإنتاجية حسب أكثر أسباب التوقف ساعاتٍ.</summary>
    private static string ActionForDowntime(string? reason) => reason switch
    {
        SiteDowntimeReasons.EquipmentBreakdown => "schedule_maintenance",
        SiteDowntimeReasons.MaterialShortage => "expedite_materials",
        SiteDowntimeReasons.LaborShortage => "assign_labor",
        SiteDowntimeReasons.Weather => "adjust_schedule",
        SiteDowntimeReasons.WaitingInspection => "coordinate_inspection",
        SiteDowntimeReasons.Safety => "safety_review",
        _ => "investigate_productivity",
    };

    // ---------- مساعدات ----------

    private static bool IsRunning(string stage)
        => stage is not (ContractStageDefinition.InitialAcceptance or ContractStageDefinition.Closed or ContractStageDefinition.Suspended or ContractStageDefinition.Cancelled);

    private static string BucketOf(DateTime from, DateTime to)
    {
        var days = (to - from).Days + 1;
        return days <= 31 ? "day" : days <= 120 ? "week" : "month";
    }

    private static List<(DateTime Start, DateTime End)> Buckets(DateTime from, DateTime to, string bucket)
    {
        var list = new List<(DateTime, DateTime)>();
        for (var s = from; s <= to;)
        {
            var e = bucket switch
            {
                "day" => s,
                "week" => s.AddDays(6),
                _ => new DateTime(s.Year, s.Month, 1).AddMonths(1).AddDays(-1),
            };
            if (e > to) e = to;
            list.Add((s, e));
            s = e.AddDays(1);
        }
        return list;
    }

    private static decimal? Pct(decimal part, decimal whole) => whole > 0 ? Math.Round(part / whole * 100m, 2) : null;
    private static decimal Hrs(int minutes) => Math.Round(minutes / 60m, 2);
    private static decimal Money(decimal v) => DocumentPricing.Round(v);
}
