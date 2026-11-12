using System.Globalization;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// إعداد مشروع المقاولات (العقد هو المشروع، بلا جدول مشاريع): ملفه وساعات عمله (فترات الإنجاز ساعة لكل فترة)، ومراحل التنفيذ
/// وأوزانها (منها نسبة الإنجاز المخططة)، وأعضاؤه بأدوارهم (منهم نطاق «المسندة إليه» على العقود). الملف ومركز التكلفة يُنشآن
/// مع العقد (<see cref="CreateForContractAsync"/>)، والعقد القديم يُستكمل عند أول حفظ للملف.
/// </summary>
public class ConstructionProjectService : IConstructionProjectService
{
    public const string CostCenterPrefix = "PRJ-";
    private const int SlotMinutes = 60;

    private readonly ErpDbContext _db;
    private readonly ITransactionRunner _tx;
    private readonly IRoleService _roles;
    private readonly IPermissionService _permissions;

    public ConstructionProjectService(ErpDbContext db, ITransactionRunner tx, IRoleService roles, IPermissionService permissions)
    {
        _db = db; _tx = tx; _roles = roles; _permissions = permissions;
    }

    // ---------- الإنشاء مع العقد ----------

    /// <summary>
    /// عقد مقاولات جديد: ملف المشروع بالقيم الافتراضية، ومركز تكلفة للمشروع إن لم يُختر مركز (رمزه PRJ-رقم العقد).
    /// يستدعيها إنشاء العقد داخل معاملته (لا تحفظ بنفسها).
    /// </summary>
    public static async Task CreateForContractAsync(ErpDbContext db, CommercialContract contract, CancellationToken ct)
    {
        await EnsureCostCenterAsync(db, contract, ct);
        db.Add(new ConstructionProjectProfile { ContractId = contract.Id });
    }

    /// <summary>المركز التلقائي السابق (باسم المشروع) يُحذف إن لم يستخدمه قيد أو مركز فرعي أو عقد آخر.</summary>
    private async Task RemoveUnusedAutoCenterAsync(Guid? centerId, CommercialContract contract, CancellationToken ct)
    {
        if (centerId is not Guid id) return;
        var auto = CostCenterPrefix + contract.ContractNumber;
        var center = await _db.Set<CostCenter>().FirstOrDefaultAsync(c => c.Id == id && (c.Code == auto || c.Code.StartsWith(auto + "-")), ct);
        if (center != null
            && !await _db.Set<JournalEntryLine>().AnyAsync(l => l.CostCenterId == id, ct)
            && !await _db.Set<CostCenter>().AnyAsync(c => c.ParentId == id, ct)
            && !await _db.Set<CommercialContract>().AnyAsync(c => c.CostCenterId == id && c.Id != contract.Id, ct))
            _db.Remove(center);
    }

    internal static async Task EnsureCostCenterAsync(ErpDbContext db, CommercialContract contract, CancellationToken ct)
    {
        if (contract.CostCenterId.HasValue) return;
        var baseCode = CostCenterPrefix + contract.ContractNumber;
        var code = baseCode;
        for (var n = 2; await db.Set<CostCenter>().AnyAsync(c => c.Code == code, ct); n++) code = $"{baseCode}-{n}";
        var name = FirstText(contract.ProjectName, contract.Title, contract.ContractNumber);
        var center = new CostCenter
        {
            Id = Guid.NewGuid(), Code = code, NameAr = name, NameEn = FirstText(contract.TitleEn, name),
            Description = string.Format(Messages.ProjectCostCenterDescription, contract.ContractNumber), IsActive = true,
        };
        db.Add(center);
        contract.CostCenterId = center.Id;
    }

    // ---------- القراءة ----------

    public async Task<ConstructionProjectDto> GetAsync(Guid contractId, CancellationToken ct = default)
    {
        var contract = await LoadContractAsync(contractId, ct);
        var profile = await _db.Set<ConstructionProjectProfile>().AsNoTracking().FirstOrDefaultAsync(p => p.ContractId == contractId, ct)
            ?? new ConstructionProjectProfile { ContractId = contractId };
        var center = contract.CostCenterId is Guid cc
            ? await _db.Set<CostCenter>().AsNoTracking().Where(c => c.Id == cc).Select(c => new { c.Code, c.NameAr }).FirstOrDefaultAsync(ct)
            : null;
        var phases = await _db.Set<ProjectPhase>().AsNoTracking().Include(p => p.Items).Where(p => p.ContractId == contractId)
            .OrderBy(p => p.SortOrder).ToListAsync(ct);
        var members = await (from m in _db.Set<ProjectMember>().AsNoTracking()
                             join u in _db.Set<User>().AsNoTracking() on m.UserId equals u.Id
                             where m.ContractId == contractId
                             orderby m.CreatedAt
                             select new ProjectMemberDto { Id = m.Id, UserId = m.UserId, UserName = u.Name, Email = u.Email, Role = m.Role, Notes = m.Notes })
            .ToListAsync(ct);

        var today = SaudiTime.Today;
        var lineIds = contract.Items.Select(i => i.Id).ToHashSet();
        var phaseDtos = phases.Select(p => new ProjectPhaseDto
        {
            Id = p.Id, Name = p.Name, PlannedStart = p.PlannedStart, PlannedEnd = p.PlannedEnd, WeightPercent = p.WeightPercent, Notes = p.Notes,
            Description = p.Description, SupervisorUserId = p.SupervisorUserId, Source = p.Source, DatesSuggested = p.DatesSuggested, PlannedCost = p.PlannedCost,
            PredecessorIds = string.IsNullOrEmpty(p.PredecessorIds) ? new() : p.PredecessorIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList(),
            // سطر حُذف من مسودة العقد بعد ربطه لا يُعرض (المرجع بلا قيد)
            ContractItemIds = p.Items.Select(i => i.ContractItemId).Where(lineIds.Contains).ToList(),
            PlannedCompletionPercent = Math.Round(PhaseFraction(p.PlannedStart, p.PlannedEnd, today) * 100m, 2),
        }).ToList();

        return new ConstructionProjectDto
        {
            ContractId = contract.Id, ContractNumber = contract.ContractNumber, Title = contract.Title, ProjectName = contract.ProjectName,
            ProjectLocation = contract.ProjectLocation, PartyName = contract.PartyName, ConsultantName = contract.ConsultantName, Stage = contract.Stage,
            StartDate = contract.StartDate, EndDate = contract.EndDate, ContractValue = contract.ContractValue,
            CostCenterId = contract.CostCenterId, CostCenterCode = center?.Code, CostCenterName = center?.NameAr,
            EstimatedBudget = profile.EstimatedBudget, WorkdayStart = Clock(profile.WorkdayStartMinutes), WorkdayEnd = Clock(profile.WorkdayEndMinutes),
            BreakStart = profile.BreakStartMinutes is int bs ? Clock(bs) : null, BreakEnd = profile.BreakEndMinutes is int be ? Clock(be) : null,
            Notes = profile.Notes,
            HourSlots = HourSlots(profile.WorkdayStartMinutes, profile.WorkdayEndMinutes, profile.BreakStartMinutes, profile.BreakEndMinutes)
                .Select(s => new ProjectHourSlotDto { Start = Clock(s.Start), End = Clock(s.End), Hours = Math.Round((s.End - s.Start) / 60m, 2) }).ToList(),
            Phases = phaseDtos,
            PhaseWeightTotal = phases.Sum(p => p.WeightPercent),
            PlannedProgressPercent = PlannedProgress(phases.Select(p => (p.PlannedStart, p.PlannedEnd, p.WeightPercent)), today),
            Members = members,
        };
    }

    // ---------- قائمة المشاريع وملخصها ----------

    /// <summary>هامش التأخر: الإنجاز المعتمد أقل من المخطط بأكثر من هذه النقاط.</summary>
    private const decimal BehindPlanMargin = 10m;

    public Task<List<ConstructionProjectSummaryDto>> ListAsync(CancellationToken ct = default) => SummariesAsync(null, ct);

    public async Task<ConstructionProjectSummaryDto> SummaryAsync(Guid contractId, CancellationToken ct = default)
    {
        await LoadContractAsync(contractId, ct);
        return (await SummariesAsync(contractId, ct)).Single();
    }

    /// <summary>
    /// الملخص من البيانات المحفوظة مجمّعة بالعقد: المراحل (الإنجاز المخطط)، والمستخلصات (المعتمد = آخر قيمة تراكمية معتمدة أو مفوترة)،
    /// والاحتياجات (والمادة غير المربوطة بربطها الحالي)، والتقارير اليومية (التكلفة من المعتمدة فقط بتكلفتها المثبّتة). العقود من
    /// استعلام عام فيسري عليها نطاق الوصول.
    /// </summary>
    private async Task<List<ConstructionProjectSummaryDto>> SummariesAsync(Guid? onlyId, CancellationToken ct)
    {
        var boqTypes = await _db.Set<ContractTypeDefinition>().AsNoTracking()
            .Where(t => t.Billing == ContractBilling.BoqCertificate).Select(t => t.Code).ToListAsync(ct);
        var contracts = await _db.Set<CommercialContract>().AsNoTracking()
            // عقد مقاول الباطن (مورد) ليس مشروعاً (يظهر في قسم مقاولي الباطن لمشروعه)، والمناقصة لا تصير مشروعاً حتى تُرسى
            .Where(c => boqTypes.Contains(c.ContractType) && (onlyId == null
                ? c.PartyType != "supplier" && (c.TenderStatus == null || c.TenderStatus == ContractTenderStatuses.Awarded)
                    // دراسة الجدوى ليست مشروعاً حتى «إنشاء العقد» (سجلها في شاشة الدراسات)
                    && (c.FeasibilityStatus == null || c.FeasibilityStatus == FeasibilityStatuses.Contracted)
                : c.Id == onlyId))
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new
            {
                c.Id, c.ContractNumber, c.Title, c.ProjectName, c.ProjectLocation, c.PartyName, c.Stage, c.StartDate, c.EndDate, c.ContractValue, c.EstimatedCost,
            }).ToListAsync(ct);
        if (contracts.Count == 0) return new();
        var ids = contracts.Select(c => c.Id).ToList();

        var budgets = await _db.Set<ConstructionProjectProfile>().AsNoTracking().Where(p => ids.Contains(p.ContractId))
            .ToDictionaryAsync(p => p.ContractId, p => p.EstimatedBudget, ct);
        var phases = (await _db.Set<ProjectPhase>().AsNoTracking().Where(p => ids.Contains(p.ContractId))
            .Select(p => new { p.ContractId, p.PlannedStart, p.PlannedEnd, p.WeightPercent }).ToListAsync(ct)).ToLookup(p => p.ContractId);
        var team = (await (from m in _db.Set<ProjectMember>().AsNoTracking()
                           join u in _db.Set<User>().AsNoTracking() on m.UserId equals u.Id
                           where ids.Contains(m.ContractId)
                           select new { m.ContractId, m.UserId, u.Name, m.Role, m.CreatedAt }).ToListAsync(ct)).ToLookup(m => m.ContractId);
        var certificates = (await _db.Set<ContractCertificate>().AsNoTracking()
            .Where(c => ids.Contains(c.ContractId) && c.Status != ContractCertificateStatuses.Cancelled)
            .Select(c => new { c.ContractId, c.Status, c.SequenceNo, c.CumulativeWorkValue, c.TotalWithVat }).ToListAsync(ct)).ToLookup(c => c.ContractId);
        var plans = await _db.Set<ProjectMaterialPlan>().AsNoTracking().Where(p => ids.Contains(p.ContractId))
            .ToDictionaryAsync(p => p.ContractId, p => p.Status, ct);
        var requirements = (await (from l in _db.Set<ProjectMaterialRequirement>().AsNoTracking()
                                   join r in _db.Set<ConstructionResource>().AsNoTracking() on l.ResourceId equals r.Id into rs
                                   from r in rs.DefaultIfEmpty()
                                   where ids.Contains(l.ContractId)
                                   select new { l.ContractId, Stocked = (r != null ? r.ProductId : l.ProductId) != null }).ToListAsync(ct))
            .ToLookup(l => l.ContractId);
        var reports = (await _db.Set<SiteDailyReport>().AsNoTracking().Where(r => ids.Contains(r.ContractId))
            .Select(r => new { r.ContractId, r.ReportDate, r.Status }).ToListAsync(ct)).ToLookup(r => r.ContractId);
        var resourceCost = await (from u in _db.Set<SiteResourceUsage>().AsNoTracking()
                                  join r in _db.Set<SiteDailyReport>().AsNoTracking() on u.ReportId equals r.Id
                                  where ids.Contains(r.ContractId) && r.Status == SiteDailyReportStatuses.Approved
                                  group u by r.ContractId into g
                                  select new { ContractId = g.Key, Cost = g.Sum(x => x.Cost) }).ToDictionaryAsync(x => x.ContractId, x => x.Cost, ct);
        var materialCost = await (from m in _db.Set<SiteMaterialUsage>().AsNoTracking()
                                  join r in _db.Set<SiteDailyReport>().AsNoTracking() on m.ReportId equals r.Id
                                  where ids.Contains(r.ContractId) && r.Status == SiteDailyReportStatuses.Approved
                                  group m by r.ContractId into g
                                  select new { ContractId = g.Key, Cost = g.Sum(x => x.Quantity * x.UnitCost) }).ToDictionaryAsync(x => x.ContractId, x => x.Cost, ct);

        // التوريد: طلبات شراء المشروع، وتكلفة المواد المصروفة له فعلاً (مستندات الصرف المرحّلة)
        var requisitionCounts = await _db.Set<MaterialRequisition>().AsNoTracking().Where(r => r.ContractId != null && ids.Contains(r.ContractId.Value) && r.Status != "rejected")
            .GroupBy(r => r.ContractId!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var issuedCost = await _db.Set<ProjectMaterialIssue>().AsNoTracking().Where(i => ids.Contains(i.ContractId) && i.Status == ProjectMaterialIssueStatuses.Posted)
            // الصافي: التسليم − الإرجاع للمستودع (الهدر تكلفة مستهلكة تبقى)
            .GroupBy(i => i.ContractId).Select(g => new { g.Key, Cost = g.Sum(i => i.Kind == ProjectMaterialIssueKinds.Return ? -i.TotalCost : i.TotalCost) }).ToDictionaryAsync(x => x.Key, x => x.Cost, ct);

        var today = SaudiTime.Today;
        return contracts.Select(c =>
        {
            var ph = phases[c.Id].ToList();
            var certs = certificates[c.Id].ToList();
            var certified = certs.Where(x => x.Status is ContractCertificateStatuses.Approved or ContractCertificateStatuses.Invoiced)
                .OrderByDescending(x => x.SequenceNo).Select(x => x.CumulativeWorkValue).FirstOrDefault();
            var reqs = requirements[c.Id].ToList();
            var reps = reports[c.Id].ToList();
            var budget = budgets.GetValueOrDefault(c.Id);
            var cost = DocumentPricing.Round(resourceCost.GetValueOrDefault(c.Id) + materialCost.GetValueOrDefault(c.Id));
            var dto = new ConstructionProjectSummaryDto
            {
                ContractId = c.Id, ContractNumber = c.ContractNumber, Title = c.Title, ProjectName = c.ProjectName, ProjectLocation = c.ProjectLocation,
                PartyName = c.PartyName, Stage = c.Stage, StartDate = c.StartDate, EndDate = c.EndDate, ContractValue = c.ContractValue,
                EstimatedBudget = budget,
                EstimatedCost = c.EstimatedCost, PlannedMargin = c.EstimatedCost > 0 ? c.ContractValue - c.EstimatedCost : null,
                PlannedMarginPercent = c.EstimatedCost > 0 && c.ContractValue > 0 ? Math.Round((c.ContractValue - c.EstimatedCost) / c.ContractValue * 100m, 2) : null,
                Requisitions = requisitionCounts.GetValueOrDefault(c.Id), IssuedMaterialsCost = issuedCost.GetValueOrDefault(c.Id),
                ElapsedPercent = c.StartDate is DateTime s && c.EndDate is DateTime e ? Math.Round(PhaseFraction(s, e, today) * 100m, 2) : 0,
                PlannedProgressPercent = PlannedProgress(ph.Select(p => (p.PlannedStart, p.PlannedEnd, p.WeightPercent)), today),
                PhaseCount = ph.Count,
                CertifiedValue = certified,
                CertifiedProgressPercent = c.ContractValue > 0 ? Math.Round(Math.Min(100m, certified / c.ContractValue * 100m), 2) : 0,
                InvoicedAmount = certs.Where(x => x.Status == ContractCertificateStatuses.Invoiced).Sum(x => x.TotalWithVat),
                OpenCertificates = certs.Count(x => x.Status is ContractCertificateStatuses.Draft or ContractCertificateStatuses.Submitted),
                MaterialPlanStatus = plans.GetValueOrDefault(c.Id), MaterialLines = reqs.Count, MaterialsNotStocked = reqs.Count(r => !r.Stocked),
                ReportCount = reps.Count, PendingReports = reps.Count(r => r.Status == SiteDailyReportStatuses.Submitted),
                LastReportDate = reps.Count == 0 ? null : reps.Max(r => r.ReportDate),
                OperatingCost = cost, BudgetUsedPercent = budget > 0 ? Math.Round(cost / budget * 100m, 2) : null,
                Team = team[c.Id].OrderBy(m => m.CreatedAt).Select(m => new ProjectTeamMemberDto { UserId = m.UserId, Name = m.Name, Role = m.Role }).ToList(),
            };
            var finished = c.Stage is ContractStageDefinition.Closed or ContractStageDefinition.Cancelled or ContractStageDefinition.InitialAcceptance;
            if (dto.PhaseCount == 0) dto.Alerts.Add(ConstructionProjectAlerts.NoPhases);
            if (dto.MaterialPlanStatus != ProjectMaterialPlanStatuses.Approved) dto.Alerts.Add(ConstructionProjectAlerts.MaterialsNotApproved);
            if (dto.MaterialsNotStocked > 0) dto.Alerts.Add(ConstructionProjectAlerts.MaterialsNotStocked);
            if (dto.PendingReports > 0) dto.Alerts.Add(ConstructionProjectAlerts.ReportsPending);
            if (dto.PhaseCount > 0 && dto.PlannedProgressPercent - dto.CertifiedProgressPercent > BehindPlanMargin) dto.Alerts.Add(ConstructionProjectAlerts.BehindPlan);
            if (budget > 0 && cost > budget) dto.Alerts.Add(ConstructionProjectAlerts.OverBudget);
            if (!finished && c.EndDate is DateTime end && end.Date < today) dto.Alerts.Add(ConstructionProjectAlerts.Overdue);
            return dto;
        }).ToList();
    }

    // ---------- الملف ----------

    public async Task<ConstructionProjectDto> SaveProfileAsync(Guid contractId, SaveProjectProfileDto request, CancellationToken ct = default)
    {
        await _tx.RunAsync(async token =>
        {
            var contract = await LoadContractAsync(contractId, token, track: true);
            EnsureEditable(contract);
            var errors = new List<string>();
            var start = ParseClock(request.WorkdayStart, errors);
            var end = ParseClock(request.WorkdayEnd, errors);
            int? breakStart = string.IsNullOrWhiteSpace(request.BreakStart) ? null : ParseClock(request.BreakStart, errors);
            int? breakEnd = string.IsNullOrWhiteSpace(request.BreakEnd) ? null : ParseClock(request.BreakEnd, errors);
            if (errors.Count == 0)
            {
                if (end <= start) errors.Add(Messages.ProjectWorkdayInvalid);
                if (breakStart.HasValue != breakEnd.HasValue || (breakStart is int b1 && breakEnd is int b2 && (b2 <= b1 || b1 < start || b2 > end)))
                    errors.Add(Messages.ProjectBreakInvalid);
            }
            if (request.EstimatedBudget < 0) errors.Add(Messages.ProjectBudgetNegative);
            var oldCenter = contract.CostCenterId;
            var changeCenter = request.CostCenterId is Guid wanted && wanted != oldCenter;
            if (changeCenter)
            {
                if (!await _db.Set<CostCenter>().AnyAsync(c => c.Id == request.CostCenterId && c.IsActive, token))
                    errors.Add("مركز التكلفة المختار غير موجود أو غير نشط.");
                // مركز عليه قيود: تغييره يفصل تكاليف المشروع المرحّلة عن مركزه الجديد فتنقسم تقاريره
                else if (oldCenter is Guid used && await _db.Set<JournalEntryLine>().AnyAsync(l => l.CostCenterId == used, token))
                    errors.Add("مركز تكلفة المشروع الحالي عليه قيود مرحّلة؛ لا يُغيَّر بعد الترحيل.");
            }
            if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
            if (changeCenter)
            {
                contract.CostCenterId = request.CostCenterId;
                await RemoveUnusedAutoCenterAsync(oldCenter, contract, token);
            }

            var profile = await _db.Set<ConstructionProjectProfile>().FirstOrDefaultAsync(p => p.ContractId == contractId, token);
            if (profile == null)
            {
                profile = new ConstructionProjectProfile { ContractId = contractId };
                _db.Add(profile);
            }
            if (request.EstimatedBudget is decimal estimatedBudget) profile.EstimatedBudget = estimatedBudget;
            profile.WorkdayStartMinutes = start; profile.WorkdayEndMinutes = end;
            profile.BreakStartMinutes = breakStart; profile.BreakEndMinutes = breakEnd;
            profile.Notes = Trim(request.Notes, 1000);
            await EnsureCostCenterAsync(_db, contract, token);
            await _db.SaveChangesAsync(token);
        }, ct);
        return await GetAsync(contractId, ct);
    }

    // ---------- المراحل ----------

    public async Task<ConstructionProjectDto> SavePhasesAsync(Guid contractId, SaveProjectPhasesDto request, CancellationToken ct = default)
    {
        await _tx.RunAsync(async token =>
        {
            var contract = await LoadContractAsync(contractId, token);
            EnsureEditable(contract);
            await ExecutionPlanService.EnsurePlanEditableAsync(_db, contractId, token);
            var incoming = request.Phases ?? new List<ProjectPhaseDto>();
            var errors = new List<string>();
            var lines = contract.Items.ToDictionary(i => i.Id);
            var seen = new HashSet<Guid>();
            for (var k = 0; k < incoming.Count; k++)
            {
                var p = incoming[k];
                var label = string.IsNullOrWhiteSpace(p.Name) ? (k + 1).ToString(CultureInfo.InvariantCulture) : p.Name.Trim();
                if (string.IsNullOrWhiteSpace(p.Name)) errors.Add(string.Format(Messages.ProjectPhaseNameRequired, k + 1));
                if (p.PlannedEnd.Date < p.PlannedStart.Date) errors.Add(string.Format(Messages.ProjectPhaseDatesInvalid, label));
                if (p.WeightPercent is <= 0 or > 100) errors.Add(string.Format(Messages.ProjectPhaseWeightInvalid, label));
                foreach (var itemId in p.ContractItemIds ?? new List<Guid>())
                {
                    if (!lines.TryGetValue(itemId, out var line) || line.IsGroup) errors.Add(string.Format(Messages.ProjectPhaseItemInvalid, label));
                    else if (!seen.Add(itemId)) errors.Add(string.Format(Messages.ProjectPhaseItemDuplicate, LineLabel(line)));
                }
            }
            var total = incoming.Sum(p => p.WeightPercent);
            if (total > 100) errors.Add(string.Format(Messages.ProjectPhaseWeightTotal, total.ToString("0.##", CultureInfo.InvariantCulture)));
            if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors.Distinct().ToList());

            // استبدال كامل: المحفوظ بمعرّفه يُحدَّث، والجديد يُضاف، والغائب يُحذف مع بنوده
            var existing = await _db.Set<ProjectPhase>().Include(p => p.Items).Where(p => p.ContractId == contractId).ToListAsync(token);
            var keep = incoming.Where(p => p.Id.HasValue).Select(p => p.Id!.Value).ToHashSet();
            foreach (var gone in existing.Where(p => !keep.Contains(p.Id))) _db.Remove(gone);
            var order = 0;
            foreach (var p in incoming)
            {
                var phase = p.Id is Guid id ? existing.FirstOrDefault(e => e.Id == id) : null;
                if (phase == null)
                {
                    phase = new ProjectPhase { ContractId = contractId, Source = PlanItemSources.Manual };
                    _db.Add(phase);
                }
                var name = p.Name.Trim()[..Math.Min(p.Name.Trim().Length, 200)];
                var wanted = (p.ContractItemIds ?? new List<Guid>()).ToHashSet();
                var datesChanged = phase.PlannedStart != p.PlannedStart.Date || phase.PlannedEnd != p.PlannedEnd.Date;
                // المرحلة المولَّدة من الدراسة إذا عُدِّلت تصير «معدّلة» فلا يستبدلها التوليد، والتواريخ المعدّلة لم تعد «مقترحة»
                if (phase.Source == PlanItemSources.Derived && (phase.Name != name || datesChanged || phase.WeightPercent != p.WeightPercent
                    || !wanted.SetEquals(phase.Items.Select(i => i.ContractItemId))))
                    phase.Source = PlanItemSources.Modified;
                if (datesChanged) phase.DatesSuggested = false;
                phase.Name = name;
                phase.PlannedStart = p.PlannedStart.Date; phase.PlannedEnd = p.PlannedEnd.Date;
                phase.WeightPercent = p.WeightPercent; phase.SortOrder = ++order; phase.Notes = Trim(p.Notes, 500);
                // الوصف والسابقات: غيابها من الطلب (عميل قديم) يبقيها؛ والسابقات مراحل محفوظة من المشروع غير المرحلة ذاتها
                if (p.Description != null) phase.Description = Trim(p.Description, 1000);
                phase.SupervisorUserId = p.SupervisorUserId;
                if (p.PredecessorIds != null)
                    phase.PredecessorIds = p.PredecessorIds.Where(x => x != phase.Id && existing.Any(e => e.Id == x)).Distinct() is var preds && preds.Any()
                        ? string.Join(',', preds) : null;
                foreach (var gone in phase.Items.Where(i => !wanted.Contains(i.ContractItemId)).ToList()) _db.Remove(gone);
                foreach (var add in wanted.Where(w => phase.Items.All(i => i.ContractItemId != w)))
                    phase.Items.Add(new ProjectPhaseItem { ContractItemId = add });
            }
            await _db.SaveChangesAsync(token);
        }, ct);
        return await GetAsync(contractId, ct);
    }

    // ---------- الأعضاء ----------

    public async Task<ConstructionProjectDto> AddMemberAsync(Guid contractId, SaveProjectMemberDto request, CancellationToken ct = default)
    {
        var contract = await LoadContractAsync(contractId, ct);
        EnsureEditable(contract);
        await ValidateMemberAsync(request, ct);
        if (await _db.Set<ProjectMember>().AnyAsync(m => m.ContractId == contractId && m.UserId == request.UserId, ct))
            throw new ConflictException(Messages.ProjectMemberExists);
        _db.Add(new ProjectMember { ContractId = contractId, UserId = request.UserId, Role = request.Role, Notes = Trim(request.Notes, 300) });
        await _db.SaveChangesAsync(ct);
        return await GetAsync(contractId, ct);
    }

    public async Task<ConstructionProjectDto> UpdateMemberAsync(Guid contractId, Guid memberId, SaveProjectMemberDto request, CancellationToken ct = default)
    {
        var contract = await LoadContractAsync(contractId, ct);
        EnsureEditable(contract);
        var member = await _db.Set<ProjectMember>().FirstOrDefaultAsync(m => m.Id == memberId && m.ContractId == contractId, ct)
            ?? throw new NotFoundException(Messages.ProjectMemberNotFound);
        request.UserId = member.UserId; // تغيير المستخدم = حذف وإضافة
        await ValidateMemberAsync(request, ct);
        member.Role = request.Role;
        member.Notes = Trim(request.Notes, 300);
        await _db.SaveChangesAsync(ct);
        return await GetAsync(contractId, ct);
    }

    public async Task<ConstructionProjectDto> RemoveMemberAsync(Guid contractId, Guid memberId, CancellationToken ct = default)
    {
        var contract = await LoadContractAsync(contractId, ct);
        EnsureEditable(contract);
        var member = await _db.Set<ProjectMember>().FirstOrDefaultAsync(m => m.Id == memberId && m.ContractId == contractId, ct)
            ?? throw new NotFoundException(Messages.ProjectMemberNotFound);
        _db.Remove(member);
        await _db.SaveChangesAsync(ct);
        return await GetAsync(contractId, ct);
    }

    public Task<List<ProjectMemberCandidateDto>> MemberCandidatesAsync(CancellationToken ct = default)
        => _db.Set<User>().AsNoTracking().Where(u => u.IsActive).OrderBy(u => u.Name)
            .Select(u => new ProjectMemberCandidateDto { Id = u.Id, Name = u.Name, Email = u.Email }).ToListAsync(ct);

    private async Task ValidateMemberAsync(SaveProjectMemberDto request, CancellationToken ct)
    {
        if (!ProjectMemberRoles.IsValid(request.Role)) throw new ValidationFailedException(Messages.ProjectMemberRoleInvalid);
        if (!await _db.Set<User>().AnyAsync(u => u.Id == request.UserId && u.IsActive, ct))
            throw new ValidationFailedException(Messages.ProjectMemberUserInvalid);
    }

    // ---------- موارد المشروع وتكلفة الساعة ----------

    public async Task<List<ProjectResourceDto>> ResourcesAsync(Guid contractId, CancellationToken ct = default)
    {
        await LoadContractAsync(contractId, ct);
        var list = await _db.Set<ProjectResource>().AsNoTracking().Where(r => r.ContractId == contractId)
            .OrderBy(r => r.Kind).ThenBy(r => r.Name).ToListAsync(ct);
        var supplierIds = list.Where(r => r.SupplierId.HasValue).Select(r => r.SupplierId!.Value).Distinct().ToList();
        var suppliers = await _db.Set<Supplier>().AsNoTracking().Where(s => supplierIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.NameAr, ct);
        var employeeIds = list.Where(r => r.EmployeeId.HasValue).Select(r => r.EmployeeId!.Value).Distinct().ToList();
        var employees = await _db.Set<Employee>().AsNoTracking().Where(e => employeeIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.NameAr, ct);
        // المشاريع الأخرى للموظف نفسه (ضمن نطاق المستخدم: العقود خارج نطاقه لا تظهر)
        var others = (await (from r in _db.Set<ProjectResource>().AsNoTracking()
                             join c in _db.Set<CommercialContract>().AsNoTracking() on r.ContractId equals c.Id
                             where r.EmployeeId != null && employeeIds.Contains(r.EmployeeId.Value) && r.ContractId != contractId && r.IsActive
                             select new { EmployeeId = r.EmployeeId!.Value, c.ContractNumber, Name = c.ProjectName ?? c.Title }).ToListAsync(ct))
            .GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.Select(x => $"{x.ContractNumber} - {x.Name}").Distinct().ToList());

        return list.Select(r => new ProjectResourceDto
        {
            Id = r.Id, Kind = r.Kind, Name = r.Name, Code = r.Code, Category = r.Category, LibraryResourceId = r.LibraryResourceId,
            FixedAssetId = r.FixedAssetId, SupplierId = r.SupplierId, SupplierName = r.SupplierId is Guid s ? suppliers.GetValueOrDefault(s) : null,
            EmployeeId = r.EmployeeId, EmployeeName = r.EmployeeId is Guid e ? employees.GetValueOrDefault(e) : null,
            RateBasis = r.RateBasis, Rate = r.Rate, FuelPerHour = r.FuelPerHour, MaintenancePerHour = r.MaintenancePerHour, OperatorPerHour = r.OperatorPerHour,
            ExtraCostPerHour = r.ExtraCostPerHour, HoursPerDay = r.HoursPerDay, DaysPerMonth = r.DaysPerMonth, OvertimeMultiplier = r.OvertimeMultiplier,
            RentalStart = r.RentalStart, RentalEnd = r.RentalEnd, IsActive = r.IsActive, Notes = r.Notes,
            IdleBillingPercent = r.IdleBillingPercent, StandbyBillingPercent = r.StandbyBillingPercent,
            HourlyCost = r.HourlyCost, OvertimeHourlyCost = r.OvertimeHourlyCost,
            OtherProjects = r.EmployeeId is Guid emp ? others.GetValueOrDefault(emp) ?? new List<string>() : new List<string>(),
        }).ToList();
    }

    public async Task<List<ProjectResourceDto>> SaveResourceAsync(Guid contractId, Guid? resourceId, ProjectResourceDto d, CancellationToken ct = default)
    {
        var contract = await LoadContractAsync(contractId, ct);
        EnsureEditable(contract);
        var errors = new List<string>();
        if (!ProjectResourceKinds.IsValid(d.Kind)) throw new ValidationFailedException(Messages.ProjectResourceKindInvalid);
        // المملوكة تكلفتها بالساعة؛ والمهندس شهرياً افتراضاً
        var basis = d.Kind == ProjectResourceKinds.OwnedEquipment ? ProjectRateBases.Hour : d.RateBasis?.Trim();
        if (!ProjectRateBases.IsValid(basis)) errors.Add(Messages.ProjectResourceBasisInvalid);
        if (d.Rate < 0 || d.FuelPerHour < 0 || d.MaintenancePerHour < 0 || d.OperatorPerHour < 0 || d.ExtraCostPerHour < 0)
            errors.Add(Messages.ProjectResourceCostNegative);
        if (d.HoursPerDay is <= 0 or > 24) errors.Add(Messages.ProjectResourceHoursPerDayInvalid);
        if (d.DaysPerMonth is <= 0 or > 31) errors.Add(Messages.ProjectResourceDaysPerMonthInvalid);
        if (d.OvertimeMultiplier is < 1m or > 5m) errors.Add(Messages.ProjectResourceOvertimeInvalid);
        if (d.RentalStart.HasValue && d.RentalEnd.HasValue && d.RentalEnd.Value.Date < d.RentalStart.Value.Date) errors.Add(Messages.ProjectResourceRentalPeriodInvalid);
        if (d.IdleBillingPercent is < 0m or > 100m || d.StandbyBillingPercent is < 0m or > 100m) errors.Add(Messages.ProjectResourceBillingPercentInvalid);

        Employee? employee = null;
        if (d.EmployeeId is Guid empId)
        {
            employee = await _db.Set<Employee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == empId, ct);
            if (employee == null) errors.Add(Messages.ProjectResourceEmployeeNotFound);
        }
        if (d.Kind == ProjectResourceKinds.RentedEquipment
            && (d.SupplierId is not Guid supplierId || !await _db.Set<Supplier>().AnyAsync(s => s.Id == supplierId, ct)))
            errors.Add(Messages.ProjectResourceSupplierRequired);
        if (d.FixedAssetId is Guid assetId && !await _db.Set<FixedAsset>().AnyAsync(a => a.Id == assetId, ct))
            errors.Add(Messages.ProjectResourceAssetNotFound);
        if (d.LibraryResourceId is Guid libId && !await _db.Set<ConstructionResource>().AnyAsync(r => r.Id == libId, ct))
            errors.Add(Messages.ProjectResourceLibraryNotFound);

        var name = FirstText(d.Name, employee?.NameAr);
        if (name.Length == 0) errors.Add(Messages.ProjectResourceNameRequired);
        var rate = d.Rate;
        string? code = d.Code, category = d.Category;
        // مهندس أو عامل مربوط بموظف: شؤون الموظفين مصدر بياناته وأجرته. التكلفة الشهرية المحمّلة (الراتب والبدلات وحصة المنشأة
        // في التأمينات ومخصص نهاية الخدمة) أساسها الشهر، ورقمه ومسماه منه ما لم يُدخلا
        var fromHr = employee != null && (d.Kind == ProjectResourceKinds.Engineer || d.Kind == ProjectResourceKinds.Labor);
        if (fromHr)
        {
            rate = HrLaborCost.MonthlyCost(employee!, DateTime.UtcNow.Date);
            basis = ProjectRateBases.Month;
            code = string.IsNullOrWhiteSpace(code) ? employee!.Code : code;
            category = string.IsNullOrWhiteSpace(category) ? employee!.JobTitle : category;
        }
        var multiplier = d.OvertimeMultiplier ?? (ProjectResourceKinds.IsEquipment(d.Kind) ? 1m : 1.5m);
        var (hourly, overtime) = HourlyCost(basis ?? ProjectRateBases.Hour, rate, d.HoursPerDay, d.DaysPerMonth,
            d.FuelPerHour + d.MaintenancePerHour + d.OperatorPerHour + d.ExtraCostPerHour, multiplier);
        if (errors.Count == 0 && hourly <= 0) errors.Add(Messages.ProjectResourceCostRequired);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        ProjectResource entity;
        if (resourceId is Guid id)
            entity = await _db.Set<ProjectResource>().FirstOrDefaultAsync(r => r.Id == id && r.ContractId == contractId, ct)
                ?? throw new NotFoundException(Messages.ProjectResourceNotFound);
        else
        {
            entity = new ProjectResource { ContractId = contractId };
            _db.Add(entity);
        }
        entity.Kind = d.Kind; entity.Name = name[..Math.Min(name.Length, 200)]; entity.Code = Trim(code, 50); entity.Category = Trim(category, 100);
        entity.LibraryResourceId = d.LibraryResourceId; entity.FixedAssetId = d.Kind == ProjectResourceKinds.OwnedEquipment ? d.FixedAssetId : null;
        entity.SupplierId = d.Kind == ProjectResourceKinds.RentedEquipment ? d.SupplierId : null; entity.EmployeeId = d.EmployeeId;
        entity.RateBasis = basis!; entity.Rate = rate; entity.FuelPerHour = d.FuelPerHour; entity.MaintenancePerHour = d.MaintenancePerHour;
        entity.OperatorPerHour = d.OperatorPerHour; entity.ExtraCostPerHour = d.ExtraCostPerHour; entity.HoursPerDay = d.HoursPerDay;
        entity.DaysPerMonth = d.DaysPerMonth; entity.OvertimeMultiplier = multiplier;
        entity.RentalStart = d.Kind == ProjectResourceKinds.RentedEquipment ? d.RentalStart?.Date : null;
        entity.RentalEnd = d.Kind == ProjectResourceKinds.RentedEquipment ? d.RentalEnd?.Date : null;
        // شروط الفوترة لعقد الإيجار وحده؛ المملوكة والعمالة يُحتسب توقفها كاملاً ولا استعداد لها
        var rented = d.Kind == ProjectResourceKinds.RentedEquipment;
        entity.IdleBillingPercent = rented ? d.IdleBillingPercent ?? 100m : 100m;
        entity.StandbyBillingPercent = rented ? d.StandbyBillingPercent ?? 0m : 0m;
        entity.HourlyCost = hourly; entity.OvertimeHourlyCost = overtime; entity.IsActive = d.IsActive; entity.Notes = Trim(d.Notes, 500);
        await _db.SaveChangesAsync(ct);
        return await ResourcesAsync(contractId, ct);
    }

    public async Task<List<ProjectResourceDto>> DeleteResourceAsync(Guid contractId, Guid resourceId, CancellationToken ct = default)
    {
        var contract = await LoadContractAsync(contractId, ct);
        EnsureEditable(contract);
        var entity = await _db.Set<ProjectResource>().FirstOrDefaultAsync(r => r.Id == resourceId && r.ContractId == contractId, ct)
            ?? throw new NotFoundException(Messages.ProjectResourceNotFound);
        if (await _db.Set<SiteResourceUsage>().AnyAsync(u => u.ProjectResourceId == resourceId, ct))
            throw new ConflictException(Messages.ProjectResourceInUse);
        _db.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return await ResourcesAsync(contractId, ct);
    }

    /// <summary>
    /// تكلفة الساعة = السعر ÷ ساعات أساسه (ساعة = 1، يوم = ساعات اليوم، شهر = ساعات اليوم × أيام الشهر) + تكاليف الساعة الإضافية
    /// (الوقود والصيانة والمشغّل والتشغيل الإضافي)؛ والساعة الإضافية = تكلفة الساعة × المعامل. المعادلة نفسها لمكتبة التكاليف.
    /// </summary>
    public static (decimal Hourly, decimal Overtime) HourlyCost(string basis, decimal rate, decimal hoursPerDay, decimal daysPerMonth, decimal perHour, decimal multiplier)
        => ResourceCostRules.HourlyCost(basis, rate, hoursPerDay, daysPerMonth, perHour, multiplier);

    // ---------- خطة الإنجاز اليومية ----------

    public async Task<List<ProjectDailyPlanDto>> DailyPlansAsync(Guid contractId, CancellationToken ct = default)
    {
        var contract = await LoadContractAsync(contractId, ct);
        var lines = contract.Items.ToDictionary(i => i.Id);
        var plans = await _db.Set<ProjectDailyPlan>().AsNoTracking().Where(p => p.ContractId == contractId).ToListAsync(ct);
        return plans
            .OrderBy(p => lines.TryGetValue(p.ContractItemId, out var l) ? l.SortOrder : int.MaxValue).ThenBy(p => p.EffectiveFrom)
            .Select(p => new ProjectDailyPlanDto
            {
                Id = p.Id, ContractItemId = p.ContractItemId, TargetPerHour = p.TargetPerHour, EffectiveFrom = p.EffectiveFrom, Notes = p.Notes,
                ItemLabel = lines.TryGetValue(p.ContractItemId, out var line) ? LineLabel(line) : null, Unit = line?.Unit,
            }).ToList();
    }

    public async Task<List<ProjectDailyPlanDto>> SaveDailyPlansAsync(Guid contractId, SaveProjectDailyPlansDto request, CancellationToken ct = default)
    {
        await _tx.RunAsync(async token =>
        {
            var contract = await LoadContractAsync(contractId, token);
            EnsureEditable(contract);
            var lines = contract.Items.ToDictionary(i => i.Id);
            var incoming = request.Plans ?? new List<ProjectDailyPlanDto>();
            var errors = new List<string>();
            var seen = new HashSet<(Guid, DateTime)>();
            for (var k = 0; k < incoming.Count; k++)
            {
                var p = incoming[k];
                if (!lines.TryGetValue(p.ContractItemId, out var line) || line.IsGroup) { errors.Add(string.Format(Messages.ProjectPlanItemInvalid, k + 1)); continue; }
                if (p.TargetPerHour <= 0) errors.Add(string.Format(Messages.ProjectPlanTargetInvalid, LineLabel(line)));
                if (p.EffectiveFrom == default) errors.Add(string.Format(Messages.ProjectPlanDateRequired, LineLabel(line)));
                else if (!seen.Add((p.ContractItemId, p.EffectiveFrom.Date))) errors.Add(string.Format(Messages.ProjectPlanDuplicate, LineLabel(line), p.EffectiveFrom.ToString("yyyy-MM-dd")));
            }
            if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

            _db.RemoveRange(await _db.Set<ProjectDailyPlan>().Where(p => p.ContractId == contractId).ToListAsync(token));
            foreach (var p in incoming)
                _db.Add(new ProjectDailyPlan
                {
                    ContractId = contractId, ContractItemId = p.ContractItemId, TargetPerHour = p.TargetPerHour, EffectiveFrom = p.EffectiveFrom.Date, Notes = Trim(p.Notes, 300),
                });
            await _db.SaveChangesAsync(token);
        }, ct);
        return await DailyPlansAsync(contractId, ct);
    }

    // ---------- أدوار المقاولات الجاهزة ----------

    private sealed record RoleTemplate(string NameAr, string NameEn, string Description, DataScope Scope, Dictionary<string, string[]> Screens);

    private const string Contracts = PermissionCatalog.CommercialContractsScreenId;
    private const string Projects = PermissionCatalog.ConstructionProjectsScreenId;
    private const string Library = PermissionCatalog.ConstructionLibraryScreenId;
    private const string SiteReports = PermissionCatalog.SiteDailyReportsScreenId;
    private static readonly string[] Docs = { "print", "export", "pdf" };
    private static string[] With(params string[] actions) => actions.Concat(Docs).ToArray();

    /// <summary>
    /// قوالب الأدوار: مدير المشروع والمهندس المشرف يريان مشاريعهم فقط (نطاق «المسندة إليه»: العضوية أو الإسناد)، والبقية كل المشاريع.
    /// الاستلام على العقود = إعداد المستخلصات والاحتياجات، والاعتماد = اعتماد المستخلص والاحتياجات.
    /// </summary>
    private static readonly RoleTemplate[] RoleTemplates =
    {
        new("مدير المشروع", "Project Manager", "مقاولات: إدارة مشاريعه المسندة إليه وإعدادها وتقديم المستخلصات.", DataScope.Assigned, new()
        {
            [Contracts] = With("view", "receive"), [Projects] = With("view", "edit"), [Library] = With("view"),
            [SiteReports] = With("view", "create", "edit", "submit"),
        }),
        new("المهندس المشرف", "Supervising Engineer", "مقاولات: متابعة تنفيذ مشاريعه المسندة إليه واعتماد تقاريرها اليومية.", DataScope.Assigned, new()
        {
            [Contracts] = With("view", "receive"), [Projects] = With("view"), [Library] = With("view"),
            [SiteReports] = With("view", "create", "edit", "delete", "submit", "approve"),
        }),
        // التقرير اليومي وحده: مشاريعه وبنودها وموارده من مسارات التقرير نفسه، فلا تظهر له العقود ولا المشاريع ولا المكتبة
        new("مشرف الموقع", "Site Supervisor", "مقاولات: تسجيل الإنجاز اليومي بالساعة في مشاريعه المسندة إليه وتقديمه.", DataScope.Assigned, new()
        {
            [SiteReports] = With("view", "create", "edit", "delete", "submit"),
        }),
        new("مراقب التكاليف", "Cost Controller", "مقاولات: الميزانيات وتكاليف البنود ومكتبة التكلفة.", DataScope.All, new()
        {
            [Contracts] = With("view"), [Projects] = With("view", "edit"), [Library] = With("view", "create", "edit"), [SiteReports] = With("view"),
        }),
        new("مسؤول المشتريات", "Procurement Officer", "مقاولات: مشتريات المشاريع.", DataScope.All, new()
        {
            [Contracts] = With("view"), [Projects] = With("view"), ["purchases"] = With("view", "create", "edit"),
        }),
        new("مسؤول المستودع", "Storekeeper", "مقاولات: مخزون المشاريع وصرف المواد.", DataScope.All, new()
        {
            [Contracts] = With("view"), [Projects] = With("view"), ["warehouses"] = With("view"), ["inventory-counts"] = With("view", "create"),
            [SiteReports] = With("view"),
        }),
        new("المدير المالي", "Finance Manager", "مقاولات: الاعتماد المالي للمستخلصات والقيود والتقارير.", DataScope.All, new()
        {
            [Contracts] = With("view", "financial_review", "approve", "post"), [Projects] = With("view"), [SiteReports] = With("view"),
            ["accounts"] = With("view", "create", "edit", "approve", "post"), ["reports"] = With("view"),
        }),
        new("الإدارة العليا", "Executive Management", "مقاولات: متابعة كل المشاريع والاعتمادات الإدارية.", DataScope.All, new()
        {
            [Contracts] = With("view", "approve"), [Projects] = With("view"), [Library] = With("view"), [SiteReports] = With("view"),
            ["dashboard"] = new[] { "view" }, ["reports"] = With("view"),
        }),
    };

    public async Task<ConstructionRoleTemplatesResultDto> CreateRoleTemplatesAsync(CancellationToken ct = default)
    {
        var result = new ConstructionRoleTemplatesResultDto();
        var existing = (await _roles.ListAsync(ct)).Select(r => r.NameAr).ToHashSet();
        foreach (var t in RoleTemplates)
        {
            if (existing.Contains(t.NameAr)) { result.Existing.Add(t.NameAr); continue; }
            var role = await _roles.CreateAsync(new SaveRoleDto { NameAr = t.NameAr, NameEn = t.NameEn, Description = t.Description }, ct);
            await _permissions.SaveAsync(new SavePermissionsRequestDto
            {
                RoleId = role.Code, Settings = new PermissionSettingsDto { Scope = t.Scope },
                Permissions = t.Screens.Select(s => new ScreenPermissionDto { ScreenId = s.Key, Actions = s.Value.ToList() }).ToList(),
            }, ct);
            result.Created.Add(t.NameAr);
        }
        return result;
    }

    // ---------- الحسابات ----------

    /// <summary>
    /// فترات الإنجاز: ساعة لكل فترة من بداية يوم العمل لنهايته، والاستراحة لا فترة لها، وآخر فترة قبل الاستراحة أو النهاية قد تقصر.
    /// </summary>
    public static List<(int Start, int End)> HourSlots(int start, int end, int? breakStart, int? breakEnd)
    {
        var ranges = breakStart is int bs && breakEnd is int be && bs > start && be < end && be > bs
            ? new[] { (start, bs), (be, end) }
            : new[] { (start, end) };
        var slots = new List<(int, int)>();
        foreach (var (from, to) in ranges)
            for (var t = from; t < to; t += SlotMinutes) slots.Add((t, Math.Min(t + SlotMinutes, to)));
        return slots;
    }

    /// <summary>النسبة المخططة من المرحلة في يوم: تتوزع بالتساوي على أيامها (اليوم الأول والأخير محسوبان).</summary>
    public static decimal PhaseFraction(DateTime start, DateTime end, DateTime day)
    {
        if (day.Date < start.Date) return 0;
        if (day.Date >= end.Date) return 1;
        var total = (end.Date - start.Date).Days + 1;
        var done = (day.Date - start.Date).Days + 1;
        return (decimal)done / total;
    }

    /// <summary>نسبة الإنجاز المخططة للمشروع = Σ وزن المرحلة × نسبتها المخططة في اليوم.</summary>
    public static decimal PlannedProgress(IEnumerable<(DateTime Start, DateTime End, decimal Weight)> phases, DateTime day)
        => Math.Round(phases.Sum(p => p.Weight * PhaseFraction(p.Start, p.End, day)), 2);

    // ---------- مساعدات ----------

    private Task<CommercialContract> LoadContractAsync(Guid contractId, CancellationToken ct, bool track = false)
        => LoadBoqContractAsync(_db, contractId, ct, track);

    /// <summary>عقد المقاولات (بجدول كميات) ببنوده؛ غيره يُرفض. مشترك مع التقرير اليومي.</summary>
    internal static async Task<CommercialContract> LoadBoqContractAsync(ErpDbContext db, Guid contractId, CancellationToken ct, bool track = false)
    {
        var query = db.Set<CommercialContract>().Include(c => c.Items).AsQueryable();
        if (!track) query = query.AsNoTracking();
        var contract = await query.FirstOrDefaultAsync(c => c.Id == contractId, ct) ?? throw new NotFoundException(Messages.ContractNotFound);
        if (!await CommercialContractService.IsBoqContractAsync(db, contract.ContractType, ct))
            throw new ConflictException(Messages.ProjectOnlyForBoq);
        return contract;
    }

    internal static void EnsureEditable(CommercialContract c)
    {
        if (c.Stage is ContractStageDefinition.Closed or ContractStageDefinition.Cancelled) throw new ConflictException(Messages.ProjectContractClosed);
    }

    internal static int ParseClock(string? value, List<string> errors)
    {
        if (TimeSpan.TryParseExact(value?.Trim(), new[] { @"hh\:mm", @"h\:mm" }, CultureInfo.InvariantCulture, out var t) && t < TimeSpan.FromDays(1))
            return (int)t.TotalMinutes;
        // نهاية اليوم «24:00» مقبولة نهايةً لفترة
        if (value?.Trim() == "24:00") return 1440;
        errors.Add(string.Format(Messages.ProjectTimeInvalid, value));
        return 0;
    }

    internal static string Clock(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

    internal static string LineLabel(CommercialContractItem i) => string.IsNullOrWhiteSpace(i.LineCode) ? i.Description : $"{i.LineCode} - {i.Description}";

    private static string FirstText(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var t = value.Trim();
        return t.Length > max ? t[..max] : t;
    }
}
