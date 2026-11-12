using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using St = ERP.Core.DTOs.Accounting.Contracts.MaterialCoverageStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// تغطية العمالة والمعدات (للقراءة): احتياجات خطة التنفيذ لكل (مورد مكتبة، مرحلة) مقابل الموارد الفعلية للمشروع المطابقة بـ
/// <c>LibraryResourceId</c>، بالساعات يوماً بيوم داخل تواريخ المرحلة:
/// <list type="bullet">
/// <item>الوحدة المادية: الموظف (<c>EmployeeId</c>) أو الأصل (<c>FixedAssetId</c>)؛ وغيرهما (فريق بلا موظف، أو مستأجرة) وحدة بسطرها.</item>
/// <item>سعتها اليومية = أكبر ساعات يومها في كل إسناداتها × (أيام عملها في الشهر ÷ 30) — لا تقويم عطل في النظام؛ وصفر في إجازة معتمدة،
/// أو بعد استبعاد الأصل، أو خارج فترة إسنادها (مدة العقد، ∩ فترة الإيجار).</item>
/// <item>اليوم المتداخل مع مشاريع أخرى (غير مغلقة ولا ملغاة) تُقسم سعته بالتساوي عليها، ثم حصة المشروع بحد ساعات دوامه وساعات
/// يوم المورد فيه: مجموع ما يُحسب لكل المشاريع لا يتجاوز السعة. مشروع آخر بلا تواريخ يُعدّ متداخلاً كل يوم (لا مبالغة في الإتاحة).</item>
/// <item>حصة المشروع اليومية تتوزع على المراحل المتزامنة التي تحتاج المورد بنسبة طلبها اليومي (الساعات ÷ أيام المرحلة).</item>
/// <item>المغطّى = min(المطلوب، المتاح الموثّق). نقص البيانات (دوام، تواريخ، ربط بالمكتبة، فترة إيجار) يجعل الصف «لا يمكن التحقق»
/// ما لم يكفِ الموثّق وحده؛ فلا يُعدّ اكتمالاً ولا نقصاً مؤكداً، ولا نسبة له.</item>
/// </list>
/// </summary>
public class ProjectResourceCoverageService : IProjectResourceCoverageService
{
    private const int Decimals = 2;
    /// <summary>أيام العمل الافتراضية في الشهر لوحدة جديدة تُقترح لسد النقص.</summary>
    private const decimal DefaultWorkdayFraction = 26m / 30m;
    private readonly ErpDbContext _db;

    public ProjectResourceCoverageService(ErpDbContext db) => _db = db;

    private const string Labor = BoqResourceTypes.Labor, Equipment = BoqResourceTypes.Equipment;
    private const string Info = MaterialCoverage.Info, Warning = MaterialCoverage.Warning, Critical = MaterialCoverage.Critical;

    public async Task<ProjectResourceCoverageDto> GetAsync(Guid contractId, CancellationToken ct = default)
    {
        var contract = await _db.Set<CommercialContract>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == contractId, ct)
            ?? throw new NotFoundException(Messages.ContractNotFound);
        var profile = await _db.Set<ConstructionProjectProfile>().AsNoTracking().FirstOrDefaultAsync(p => p.ContractId == contractId, ct);
        var dailyHours = profile == null ? 0m
            : ConstructionProjectService.HourSlots(profile.WorkdayStartMinutes, profile.WorkdayEndMinutes, profile.BreakStartMinutes, profile.BreakEndMinutes)
                .Sum(s => s.End - s.Start) / 60m;
        var phases = await _db.Set<ProjectPhase>().AsNoTracking().Where(p => p.ContractId == contractId).ToDictionaryAsync(p => p.Id, ct);
        var requirements = await _db.Set<ProjectResourceRequirement>().AsNoTracking()
            .Where(r => r.ContractId == contractId && (r.ResourceType == Labor || r.ResourceType == Equipment)).ToListAsync(ct);
        var assigned = await _db.Set<ProjectResource>().AsNoTracking().Where(r => r.ContractId == contractId && r.IsActive).ToListAsync(ct);

        // الإسنادات في مشاريع أخرى للموظف أو الأصل نفسه (المشروع المغلق أو الملغى لا يحجز)
        var employeeIds = assigned.Where(r => r.EmployeeId != null).Select(r => r.EmployeeId!.Value).Distinct().ToList();
        var assetIds = assigned.Where(r => r.FixedAssetId != null && ProjectResourceKinds.IsEquipment(r.Kind)).Select(r => r.FixedAssetId!.Value).Distinct().ToList();
        var others = employeeIds.Count + assetIds.Count == 0 ? new List<OtherClaim>() : await (
            from r in _db.Set<ProjectResource>().AsNoTracking()
            join c in _db.Set<CommercialContract>().AsNoTracking() on r.ContractId equals c.Id
            where r.IsActive && r.ContractId != contractId && c.Stage != ContractStageDefinition.Closed && c.Stage != ContractStageDefinition.Cancelled
                && ((r.EmployeeId != null && employeeIds.Contains(r.EmployeeId.Value))
                    || (r.FixedAssetId != null && assetIds.Contains(r.FixedAssetId.Value)))
            select new OtherClaim(r.EmployeeId, r.FixedAssetId, r.Kind, r.HoursPerDay, r.RentalStart, r.RentalEnd, c.Id, c.ContractNumber, c.StartDate, c.EndDate))
            .ToListAsync(ct);
        var leaves = employeeIds.Count == 0 ? new List<(Guid EmployeeId, DateTime Start, DateTime End)>()
            : (await _db.Set<LeaveRequest>().AsNoTracking()
                .Where(l => employeeIds.Contains(l.EmployeeId) && l.Status == LeaveStatuses.Approved)
                .Select(l => new { l.EmployeeId, l.StartDate, l.EndDate }).ToListAsync(ct))
              .Select(l => (l.EmployeeId, l.StartDate.Date, l.EndDate.Date)).ToList();
        var disposed = assetIds.Count == 0 ? new Dictionary<Guid, DateTime?>()
            : await _db.Set<FixedAsset>().AsNoTracking().Where(a => assetIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.DisposedAt, ct);

        var input = new Input(contract.StartDate?.Date, contract.EndDate?.Date, dailyHours, phases, requirements, assigned, others, leaves, disposed);
        var dto = Calculate(input);
        dto.ContractId = contractId;
        return dto;
    }

    // ---------------- الحساب ----------------

    internal sealed record OtherClaim(Guid? EmployeeId, Guid? FixedAssetId, string Kind, decimal HoursPerDay, DateTime? RentalStart, DateTime? RentalEnd,
        Guid ContractId, string ContractNumber, DateTime? Start, DateTime? End);

    internal sealed record Input(DateTime? ProjectStart, DateTime? ProjectEnd, decimal DailyHours, Dictionary<Guid, ProjectPhase> Phases,
        List<ProjectResourceRequirement> Requirements, List<ProjectResource> Assigned, List<OtherClaim> Others,
        List<(Guid EmployeeId, DateTime Start, DateTime End)> Leaves, Dictionary<Guid, DateTime?> Disposed);

    /// <summary>فترة بيومي بدايتها ونهايتها؛ الفارغة غير معروفة.</summary>
    private readonly record struct Window(DateTime? Start, DateTime? End)
    {
        public bool Known => Start != null && End != null;
        /// <summary>غير المعروفة تشمل كل يوم (تحفّظ: لا يُفترض فراغ المورد).</summary>
        public bool Contains(DateTime d) => !Known || (d >= Start!.Value && d <= End!.Value);
        public Window Intersect(DateTime? s, DateTime? e) => new(Max(Start, s), Min(End, e));
        private static DateTime? Max(DateTime? a, DateTime? b) => a == null ? b : b == null ? a : (a > b ? a : b);
        private static DateTime? Min(DateTime? a, DateTime? b) => a == null ? b : b == null ? a : (a < b ? a : b);
    }

    /// <summary>صف احتياج: مورد مكتبة في مرحلة، وما يُجمع له يوماً بيوم.</summary>
    private sealed class RowState
    {
        public required ResourceCoverageRowDto Dto { get; init; }
        public DateTime? Start { get; init; }
        public DateTime? End { get; init; }
        public bool MissingCost { get; set; }
        public decimal DailyDemand => Start is DateTime s && End is DateTime e && e >= s ? Dto.RequiredHours / ((e - s).Days + 1) : 0;
        public Dictionary<Guid, AssignedResourceCoverageDto> Units { get; } = new();
    }

    /// <summary>وحدة مادية في المشروع: سطورها المدمجة (الموظف أو الأصل نفسه مرة واحدة)، وفترتها، وإسناداتها الأخرى.</summary>
    private sealed class Unit
    {
        public required ProjectResource Row { get; init; }
        public required Window Window { get; init; }
        public bool Unverifiable { get; init; }
        public decimal HoursPerDay { get; init; }
        public decimal PhysicalHours { get; init; }
        public decimal Fraction { get; init; }
        public List<(string Number, Window Window)> Others { get; init; } = new();
    }

    internal static ProjectResourceCoverageDto Calculate(Input x)
    {
        var dto = new ProjectResourceCoverageDto { DailyHours = Math.Round(x.DailyHours, Decimals), ContractStart = x.ProjectStart, ContractEnd = x.ProjectEnd };
        var projectWindow = new Window(x.ProjectStart, x.ProjectEnd);
        var noSchedule = x.DailyHours <= 0;
        var noDates = !projectWindow.Known;
        if (noSchedule) dto.Alerts.Add(Alert("missing_schedule", Critical, 0));
        if (noDates) dto.Alerts.Add(Alert("missing_dates", Critical, 0));

        // الصفوف: (مورد المكتبة أو اسم اليدوي، المرحلة)
        var rows = x.Requirements
            .GroupBy(r => (r.ResourceType, r.ResourceId, Name: r.ResourceId == null ? r.Name : null, r.PhaseId))
            .Select(g =>
            {
                var first = g.First();
                var phase = g.Key.PhaseId is Guid pid ? x.Phases.GetValueOrDefault(pid) : null;
                var start = phase?.PlannedStart.Date ?? x.ProjectStart;
                var end = phase?.PlannedEnd.Date ?? x.ProjectEnd;
                return new RowState
                {
                    Start = start, End = end, MissingCost = g.Any(r => r.UnitCost <= 0),
                    Dto = new ResourceCoverageRowDto
                    {
                        ResourceType = g.Key.ResourceType, ResourceId = g.Key.ResourceId, Name = first.Name, Category = first.Category,
                        Ownership = g.Key.ResourceType == Equipment ? g.Select(r => r.Ownership ?? "undecided").Distinct().OrderBy(o => o).First() : null,
                        PhaseId = g.Key.PhaseId, PhaseName = phase?.Name, PhaseStart = start, PhaseEnd = end,
                        RequiredHours = Math.Round(g.Sum(r => r.PlannedHours), Decimals), SuggestedCount = Math.Round(g.Sum(r => r.SuggestedCount), 1),
                    },
                };
            })
            .Where(r => r.Dto.RequiredHours > 0)
            .OrderBy(r => r.Dto.ResourceType == Labor ? 0 : 1).ThenBy(r => r.Start).ThenBy(r => r.Dto.Name).ToList();
        var needed = rows.Where(r => r.Dto.ResourceId != null).Select(r => r.Dto.ResourceId!.Value).ToHashSet();

        // الوحدات المادية في المشروع (الموظف أو الأصل المكرر في سطرين يُعدّ مرة)
        var units = new List<Unit>();
        foreach (var g in x.Assigned.GroupBy(r => r.EmployeeId is Guid e ? $"e:{e}" : r.FixedAssetId is Guid a && ProjectResourceKinds.IsEquipment(r.Kind) ? $"a:{a}" : $"r:{r.Id}"))
        {
            var row = g.First();
            var rented = row.Kind == ProjectResourceKinds.RentedEquipment;
            var window = projectWindow;
            if (rented) window = window.Intersect(row.RentalStart?.Date, row.RentalEnd?.Date);
            var mine = x.Others.Where(o => (row.EmployeeId != null && o.EmployeeId == row.EmployeeId)
                || (row.FixedAssetId != null && ProjectResourceKinds.IsEquipment(row.Kind) && o.FixedAssetId == row.FixedAssetId)).ToList();
            units.Add(new Unit
            {
                Row = row, Window = window,
                Unverifiable = noDates || (rented && (row.RentalStart == null || row.RentalEnd == null)),
                HoursPerDay = g.Max(r => r.HoursPerDay),
                PhysicalHours = Math.Max(g.Max(r => r.HoursPerDay), mine.Select(o => o.HoursPerDay).DefaultIfEmpty(0).Max()),
                Fraction = row.DaysPerMonth > 0 ? Math.Min(1m, row.DaysPerMonth / 30m) : DefaultWorkdayFraction,
                Others = mine.GroupBy(o => o.ContractId).Select(o =>
                {
                    var f = o.First();
                    var w = new Window(f.Start?.Date, f.End?.Date);
                    if (f.Kind == ProjectResourceKinds.RentedEquipment) w = w.Intersect(f.RentalStart?.Date, f.RentalEnd?.Date);
                    return (f.ContractNumber, w);
                }).ToList(),
            });
        }
        foreach (var u in units.Where(u => u.Row.LibraryResourceId is not Guid lib || !needed.Contains(lib)))
            dto.Unmatched.Add(UnitDto(u));
        if (dto.Unmatched.Count > 0) dto.Alerts.Add(Alert("unmatched_assigned", Warning, dto.Unmatched.Count));

        // يوماً بيوم: حصة كل وحدة من المشروع على مراحل موردها المتزامنة
        foreach (var lib in needed)
        {
            var libRows = rows.Where(r => r.Dto.ResourceId == lib && r.Start != null && r.End != null).ToList();
            var libUnits = units.Where(u => u.Row.LibraryResourceId == lib).ToList();
            foreach (var u in libUnits)
                foreach (var r in rows.Where(r => r.Dto.ResourceId == lib))
                    r.Units[u.Row.Id] = UnitDto(u);
            if (noSchedule || libRows.Count == 0) continue;
            var from = libRows.Min(r => r.Start!.Value);
            var to = libRows.Max(r => r.End!.Value);
            foreach (var u in libUnits.Where(u => !u.Unverifiable))
            {
                var employee = u.Row.EmployeeId;
                var asset = ProjectResourceKinds.IsEquipment(u.Row.Kind) ? u.Row.FixedAssetId : null;
                var disposedAt = asset is Guid aid ? x.Disposed.GetValueOrDefault(aid)?.Date : null;
                for (var d = from; d <= to; d = d.AddDays(1))
                {
                    var active = libRows.Where(r => d >= r.Start && d <= r.End).ToList();
                    if (active.Count == 0) continue;
                    var cap = u.PhysicalHours * u.Fraction;
                    var off = !u.Window.Contains(d)
                        || (employee is Guid e && x.Leaves.Any(l => l.EmployeeId == e && d >= l.Start && d <= l.End))
                        || (disposedAt is DateTime da && d >= da);
                    var claimants = 1 + u.Others.Count(o => o.Window.Contains(d));
                    var share = off ? 0 : Math.Min(cap / claimants, Math.Min(u.HoursPerDay, x.DailyHours) * u.Fraction);
                    var sharedLost = off ? 0 : cap / claimants * (claimants - 1);
                    var unavailable = off ? cap : 0;
                    var totalDemand = active.Sum(r => r.DailyDemand);
                    foreach (var r in active)
                    {
                        var w = totalDemand > 0 ? r.DailyDemand / totalDemand : 1m / active.Count;
                        var a = r.Units[u.Row.Id];
                        a.AvailableHours += share * w;
                        a.SharedHours += sharedLost * w;
                        a.UnavailableHours += unavailable * w;
                    }
                }
            }
        }

        // الحالة والتنبيهات لكل صف
        foreach (var r in rows)
        {
            var d = r.Dto;
            d.Assigned = r.Units.Values.ToList();
            foreach (var a in d.Assigned)
            {
                a.AvailableHours = Math.Round(a.AvailableHours, Decimals);
                a.SharedHours = Math.Round(a.SharedHours, Decimals);
                a.UnavailableHours = Math.Round(a.UnavailableHours, Decimals);
            }
            d.AssignedCount = d.Assigned.Count;
            var verified = d.Assigned.Where(a => !a.Unverifiable).Sum(a => a.AvailableHours);
            d.AvailableHours = Math.Round(verified, Decimals);
            d.CoveredHours = Math.Round(Math.Min(d.RequiredHours, verified), Decimals);
            d.ShortageHours = Math.Round(d.RequiredHours - d.CoveredHours, Decimals);
            var missingData = noSchedule || r.Start == null || r.End == null || d.ResourceId == null || d.Assigned.Any(a => a.Unverifiable);
            d.Status = d.CoveredHours >= d.RequiredHours ? St.Full
                : missingData ? St.Unverifiable
                : d.CoveredHours > 0 ? St.Partial : St.None;
            d.CoveragePercent = d.Status == St.Unverifiable ? null : Math.Round(d.CoveredHours / d.RequiredHours * 100m, 1);
            var stageDays = r.Start is DateTime s && r.End is DateTime e ? (e - s).Days + 1 : 0;
            var unitCapacity = stageDays * x.DailyHours * DefaultWorkdayFraction;
            d.AdditionalUnitsNeeded = d.Status is St.Partial or St.None && unitCapacity > 0 ? (int)Math.Ceiling(d.ShortageHours / unitCapacity) : 0;
            RowAlerts(r, x, noSchedule);
        }
        dto.Rows = rows.Select(r => r.Dto).ToList();

        // التجميع: لكل مورد عبر المراحل، وللعمالة بالتخصص
        dto.ByResource = rows.GroupBy(r => (r.Dto.ResourceType, r.Dto.ResourceId, Name: r.Dto.ResourceId == null ? r.Dto.Name : null))
            .Select(g => Total(g.Select(r => r.Dto).ToList(), g.First().Dto.Name, g.Key.ResourceType, g.Key.ResourceId, g.First().Dto.Category)).ToList();
        dto.ByCategory = rows.Where(r => r.Dto.ResourceType == Labor).GroupBy(r => r.Dto.Category ?? r.Dto.Name)
            .Select(g => Total(g.Select(r => r.Dto).ToList(), g.Key, Labor, null, g.Key)).ToList();
        return dto;
    }

    private static void RowAlerts(RowState r, Input x, bool noSchedule)
    {
        var d = r.Dto;
        var equipment = d.ResourceType == Equipment;
        switch (d.Status)
        {
            case St.Full: d.Alerts.Add(Alert("fully_covered", Info, d.RequiredHours)); break;
            case St.Partial: d.Alerts.Add(Alert("partially_covered", Warning, d.ShortageHours)); break;
            case St.None: d.Alerts.Add(d.AssignedCount == 0 ? Alert("none_assigned", Critical, d.RequiredHours) : Alert("insufficient_availability", Critical, d.ShortageHours)); break;
            default: d.Alerts.Add(Alert("unverifiable", Warning, d.RequiredHours - d.CoveredHours)); break;
        }
        if (noSchedule) d.Alerts.Add(Alert("missing_schedule", Critical, 0));
        if (r.Start == null || r.End == null) d.Alerts.Add(Alert("missing_dates", Critical, 0));
        if (d.ResourceId == null) d.Alerts.Add(Alert("requirement_not_linked", Warning, d.RequiredHours));
        var shared = d.Assigned.Sum(a => a.SharedHours);
        if (shared > 0) d.Alerts.Add(Alert("shared_other_project", Warning, Math.Round(shared, Decimals)));
        var unavailable = d.Assigned.Sum(a => a.UnavailableHours);
        if (unavailable > 0) d.Alerts.Add(Alert(equipment ? "equipment_unavailable" : "absence", equipment ? Warning : Info, Math.Round(unavailable, Decimals)));
        if (r.MissingCost || d.Assigned.Any(a => x.Assigned.FirstOrDefault(p => p.Id == a.ProjectResourceId)?.HourlyCost <= 0))
            d.Alerts.Add(Alert("missing_cost", Warning, d.RequiredHours));
        // لا مورد مسند بهذا المورد، وفي المشروع موارد بالتخصص نفسه غير مربوطة به
        if (d.AssignedCount == 0 && d.Category != null
            && x.Assigned.Any(p => p.LibraryResourceId != d.ResourceId && string.Equals(p.Category, d.Category, StringComparison.OrdinalIgnoreCase)))
            d.Alerts.Add(Alert("no_matching_link", Info, d.RequiredHours));
        if (!equipment) return;
        if (d.Ownership == "undecided") d.Alerts.Add(Alert("ownership_undecided", Warning, d.RequiredHours));
        foreach (var a in d.Assigned.Where(a => a.Kind == ProjectResourceKinds.RentedEquipment))
        {
            var p = x.Assigned.First(z => z.Id == a.ProjectResourceId);
            if (p.RentalStart == null || p.RentalEnd == null) d.Alerts.Add(Alert("rental_incomplete", Warning, 0));
            else if (r.End is DateTime end && p.RentalEnd.Value.Date < end) d.Alerts.Add(Alert("rental_expired", Warning, a.UnavailableHours));
        }
        if (d.Assigned.Any(a => a.FixedAssetId is Guid id && x.Disposed.GetValueOrDefault(id) != null)) d.Alerts.Add(Alert("asset_disposed", Warning, 0));
        if (d.AdditionalUnitsNeeded > 0) d.Alerts.Add(Alert("rental_needed", Warning, d.AdditionalUnitsNeeded));
    }

    private static ResourceCoverageTotalDto Total(List<ResourceCoverageRowDto> rows, string name, string type, Guid? resourceId, string? category)
    {
        var required = rows.Sum(r => r.RequiredHours);
        var covered = rows.Sum(r => r.CoveredHours);
        var unverifiable = rows.Any(r => r.Status == St.Unverifiable);
        var status = covered >= required ? St.Full : unverifiable ? St.Unverifiable : covered > 0 ? St.Partial : St.None;
        return new ResourceCoverageTotalDto
        {
            ResourceType = type, ResourceId = resourceId, Name = name, Category = category,
            RequiredHours = Math.Round(required, Decimals), AvailableHours = Math.Round(rows.Sum(r => r.AvailableHours), Decimals),
            CoveredHours = Math.Round(covered, Decimals), ShortageHours = Math.Round(required - covered, Decimals),
            CoveragePercent = status == St.Unverifiable || required <= 0 ? null : Math.Round(covered / required * 100m, 1),
            AssignedCount = rows.SelectMany(r => r.Assigned).Select(a => a.ProjectResourceId).Distinct().Count(), Status = status,
        };
    }

    private static AssignedResourceCoverageDto UnitDto(Unit u) => new()
    {
        ProjectResourceId = u.Row.Id, Name = u.Row.Name, Kind = u.Row.Kind, EmployeeId = u.Row.EmployeeId, FixedAssetId = u.Row.FixedAssetId,
        LibraryResourceId = u.Row.LibraryResourceId, Unverifiable = u.Unverifiable, SharedWith = u.Others.Select(o => o.Number).Distinct().ToList(),
    };

    private static ProjectSupplyAlertDto Alert(string code, string severity, decimal qty) => new() { Code = code, Severity = severity, Quantity = qty };
}
