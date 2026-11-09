using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using P = ERP.Service.Services.Accounting.ConstructionProjectService;
using St = ERP.Core.Models.Accounting.SiteDailyReportStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// التقرير اليومي للموقع (العقد هو المشروع): واحد لكل يوم، بفترات الإنجاز (ساعة لكل فترة من ساعات عمل المشروع، يعدّلها المشرف)
/// لكل بند بمستهدفها من خطة الإنجاز ومنفّذها وتوقفها، والموارد المستخدمة بتكلفة ساعتها المثبّتة، والمواد المستهلكة.
/// المسودة تُعدَّل، ثم يقدّمها المشرف ويعتمدها المهندس المسؤول (أو يعيدها بسبب)، والمعتمد لا يُعدَّل.
/// التكلفة هنا تشغيلية للإنتاجية وتوزيع التكلفة، لا قيد لها: الأثر المحاسبي من مستنداته (الرواتب، فواتير الإيجار، الصرف للمشروع).
/// </summary>
public class SiteDailyReportService : ISiteDailyReportService
{
    /// <summary>الفترة التي قلّ إنجازها عن هذه النسبة من المستهدف تُعدّ منخفضة الإنتاجية.</summary>
    public const decimal LowProductivityRatio = 0.9m;

    private readonly ErpDbContext _db;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;
    private readonly IAccountingPostingService _posting;

    public SiteDailyReportService(ErpDbContext db, ITransactionRunner tx, ICurrentUser user, IAccountingPostingService posting)
    {
        _db = db; _tx = tx; _user = user; _posting = posting;
    }

    // ---------- القراءة ----------

    public async Task<List<SiteReportBoqLineDto>> BoqLinesAsync(Guid contractId, CancellationToken ct = default)
    {
        var contract = await P.LoadBoqContractAsync(_db, contractId, ct);
        return contract.Items.Where(i => !i.IsGroup).OrderBy(i => i.SortOrder)
            .Select(i => new SiteReportBoqLineDto { Id = i.Id, LineCode = i.LineCode, Description = i.Description, Unit = i.Unit, IsSubItem = i.IsSubItem })
            .ToList();
    }

    public async Task<List<SiteReportListItemDto>> ListAsync(Guid contractId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var contract = await P.LoadBoqContractAsync(_db, contractId, ct);
        var reports = await ReportsQuery(contractId, from, to, includeUnapproved: true).ToListAsync(ct);
        var ctx = await ContextAsync(contract, reports, ct);
        return reports.OrderByDescending(r => r.ReportDate).Select(r =>
        {
            var s = Summarize(r, ctx);
            return new SiteReportListItemDto
            {
                Id = r.Id, ReportDate = r.ReportDate, Status = r.Status, PreparedBy = r.PreparedBy, ApprovedBy = r.ApprovedBy,
                AchievementPercent = s.AchievementPercent, DowntimeHours = s.DowntimeHours, TotalCost = s.TotalCost,
            };
        }).ToList();
    }

    public async Task<SiteDailyReportDto> DayAsync(Guid contractId, DateTime date, CancellationToken ct = default)
    {
        var contract = await P.LoadBoqContractAsync(_db, contractId, ct);
        var day = date.Date;
        var existing = await Full().AsNoTracking().FirstOrDefaultAsync(r => r.ContractId == contractId && r.ReportDate == day, ct);
        if (existing != null) return await ToDtoAsync(contract, existing, ct);

        // قالب اليوم: لكل فترة من ساعات العمل × كل بند له خطة سارية، والمستهدف = المستهدف بالساعة × طول الفترة
        var slots = await SlotsAsync(contractId, ct);
        var plans = await _db.Set<ProjectDailyPlan>().AsNoTracking().Where(p => p.ContractId == contractId && p.EffectiveFrom <= day).ToListAsync(ct);
        var effective = plans.GroupBy(p => p.ContractItemId).Select(g => g.OrderByDescending(p => p.EffectiveFrom).First()).ToList();
        var order = contract.Items.ToDictionary(i => i.Id, i => i.SortOrder);
        var template = new SiteDailyReport { ContractId = contractId, ReportDate = day, Status = St.Draft };
        var sort = 0;
        foreach (var (start, end) in slots)
            foreach (var plan in effective.Where(p => order.ContainsKey(p.ContractItemId)).OrderBy(p => order[p.ContractItemId]))
                template.Hours.Add(new SiteHourEntry
                {
                    SlotStart = start, SlotEnd = end, ContractItemId = plan.ContractItemId, SortOrder = ++sort,
                    TargetQuantity = Math.Round(plan.TargetPerHour * (end - start) / 60m, 4),
                });
        var dto = await ToDtoAsync(contract, template, ct);
        dto.Id = null;
        return dto;
    }

    public async Task<SiteDailyReportDto> GetAsync(Guid contractId, Guid reportId, CancellationToken ct = default)
    {
        var contract = await P.LoadBoqContractAsync(_db, contractId, ct);
        var report = await Full().AsNoTracking().FirstOrDefaultAsync(r => r.Id == reportId && r.ContractId == contractId, ct)
            ?? throw new NotFoundException(Messages.SiteReportNotFound);
        return await ToDtoAsync(contract, report, ct);
    }

    // ---------- الكتابة ----------

    public async Task<SiteDailyReportDto> CreateAsync(Guid contractId, SaveSiteDailyReportDto request, CancellationToken ct = default)
    {
        var id = await _tx.RunAsync(async token =>
        {
            var contract = await P.LoadBoqContractAsync(_db, contractId, token);
            P.EnsureEditable(contract);
            if (request.ReportDate == default) throw new ValidationFailedException(Messages.SiteReportDateRequired);
            var day = request.ReportDate.Date;
            if (await _db.Set<SiteDailyReport>().AnyAsync(r => r.ContractId == contractId && r.ReportDate == day, token))
                throw new ConflictException(string.Format(Messages.SiteReportExists, day.ToString("yyyy-MM-dd")));
            var report = new SiteDailyReport
            {
                ContractId = contractId, ReportDate = day, Status = St.Draft, PreparedByUserId = _user.UserId, PreparedBy = _user.Name,
            };
            await ApplyAsync(report, contract, request, token);
            _db.Add(report);
            await _db.SaveChangesAsync(token);
            return report.Id;
        }, ct);
        return await GetAsync(contractId, id, ct);
    }

    public async Task<SiteDailyReportDto> UpdateAsync(Guid contractId, Guid reportId, SaveSiteDailyReportDto request, CancellationToken ct = default)
    {
        await _tx.RunAsync(async token =>
        {
            var contract = await P.LoadBoqContractAsync(_db, contractId, token);
            P.EnsureEditable(contract);
            var report = await LoadTrackedAsync(contractId, reportId, token);
            EnsureStatus(report, St.Draft);
            await ApplyAsync(report, contract, request, token);
            await _db.SaveChangesAsync(token);
        }, ct);
        return await GetAsync(contractId, reportId, ct);
    }

    public async Task DeleteAsync(Guid contractId, Guid reportId, CancellationToken ct = default)
    {
        await P.LoadBoqContractAsync(_db, contractId, ct);
        var report = await LoadTrackedAsync(contractId, reportId, ct);
        EnsureStatus(report, St.Draft);
        _db.Remove(report);
        await _db.SaveChangesAsync(ct);
    }

    public Task<SiteDailyReportDto> SubmitAsync(Guid contractId, Guid reportId, CancellationToken ct = default)
        => TransitionAsync(contractId, reportId, St.Draft, r =>
        {
            if (r.Hours.Count == 0) throw new ValidationFailedException(Messages.SiteReportEmpty);
            r.Status = St.Submitted; r.SubmittedAt = DateTime.UtcNow; r.SubmittedBy = _user.Name; r.ReturnNote = null;
        }, ct);

    /// <summary>الاعتماد يثبّت الإنجاز ويحمّل تكلفة العمالة والمعدات المملوكة على المشروع بقيد في المعاملة نفسها.</summary>
    public async Task<SiteDailyReportDto> ApproveAsync(Guid contractId, Guid reportId, CancellationToken ct = default)
    {
        await _tx.RunAsync(async token =>
        {
            var contract = await P.LoadBoqContractAsync(_db, contractId, token);
            P.EnsureEditable(contract);
            var report = await LoadTrackedAsync(contractId, reportId, token);
            EnsureStatus(report, St.Submitted);
            report.Status = St.Approved; report.ApprovedAt = DateTime.UtcNow; report.ApprovedBy = _user.Name; report.ApprovedByUserId = _user.UserId;
            await PostCostAsync(report, contract, token);
            await _db.SaveChangesAsync(token);
        }, ct);
        return await GetAsync(contractId, reportId, ct);
    }

    public Task<int> PostPendingCostsAsync(Guid contractId, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var contract = await P.LoadBoqContractAsync(_db, contractId, token);
            var reports = await Full().Where(r => r.ContractId == contractId && r.Status == St.Approved && r.CostJournalEntryId == null)
                .OrderBy(r => r.ReportDate).ToListAsync(token);
            var posted = 0;
            foreach (var r in reports)
            {
                await PostCostAsync(r, contract, token);
                if (r.CostJournalEntryId != null) posted++;
            }
            await _db.SaveChangesAsync(token);
            return posted;
        }, ct);

    /// <summary>
    /// قيد تحميل التقرير المعتمد: العمالة والمهندسون على «تكلفة عمالة المشاريع»، والمعدات المملوكة على «تكلفة معدات المشاريع»،
    /// كلاهما بمركز تكلفة المشروع، مقابل «التكاليف المحمّلة على المشاريع» بلا مركز (فلا يتغير إجمالي مصروف المنشأة ولا تُحتسب
    /// الرواتب والإهلاك مرتين). المعدات المستأجرة تكلفتها من فاتورة الإيجار على مركز المشروع، فلا تُحمَّل هنا. عقد بلا مركز أو تقرير بلا
    /// تكلفة: لا قيد (يبقى تشغيلياً ويُنبَّه عنه في تقرير التكلفة).
    /// </summary>
    private async Task PostCostAsync(SiteDailyReport report, CommercialContract contract, CancellationToken ct)
    {
        if (contract.CostCenterId is not Guid cc) return;
        var kinds = await _db.Set<ProjectResource>().AsNoTracking().Where(x => x.ContractId == contract.Id)
            .ToDictionaryAsync(x => x.Id, x => x.Kind, ct);
        string? KindOf(Guid id) => kinds.GetValueOrDefault(id);
        var labor = DocumentPricing.Round(report.Resources.Where(u => KindOf(u.ProjectResourceId) is ProjectResourceKinds.Labor or ProjectResourceKinds.Engineer).Sum(u => u.Cost));
        var equipment = DocumentPricing.Round(report.Resources.Where(u => KindOf(u.ProjectResourceId) == ProjectResourceKinds.OwnedEquipment).Sum(u => u.Cost));
        if (labor + equipment <= 0) return;

        await ContractAccountsSetup.EnsureMissingAsync(_db, ct);
        var settings = await PostingAccounts.SettingsAsync(_db, ct);
        var note = $"تحميل عمالة ومعدات التقرير اليومي {report.ReportDate:yyyy-MM-dd} - عقد {contract.ContractNumber}";
        var lines = new List<PostingLine>();
        if (labor > 0) lines.Add(new(settings.ContractAccount(nameof(AccountingSettings.ProjectLaborCostAccountCode)), labor, 0, note, cc));
        if (equipment > 0) lines.Add(new(settings.ContractAccount(nameof(AccountingSettings.ProjectEquipmentCostAccountCode)), equipment, 0, note, cc));
        lines.Add(new(settings.ContractAccount(nameof(AccountingSettings.ProjectAbsorbedCostAccountCode)), 0, labor + equipment, note));
        var posted = await _posting.PostAsync(new GenericPostingRequest
        {
            Date = report.ReportDate, Description = note, SourceType = ProjectCostService.SiteCostSource, SourceId = report.Id,
            SourceNumber = contract.ContractNumber, WarehouseId = contract.WarehouseId, Lines = lines,
        }, ct);
        report.CostJournalEntryId = posted.JournalEntryId;
        report.PostedLaborCost = labor;
        report.PostedEquipmentCost = equipment;
    }

    public Task<SiteDailyReportDto> ReturnAsync(Guid contractId, Guid reportId, SiteReportActionDto request, CancellationToken ct = default)
    {
        var note = request.Note?.Trim();
        if (string.IsNullOrEmpty(note)) throw new ValidationFailedException(Messages.SiteReportReturnNoteRequired);
        return TransitionAsync(contractId, reportId, St.Submitted, r =>
        {
            r.Status = St.Draft; r.ReturnNote = note.Length > 500 ? note[..500] : note; r.SubmittedAt = null; r.SubmittedBy = null;
        }, ct);
    }

    private async Task<SiteDailyReportDto> TransitionAsync(Guid contractId, Guid reportId, string from, Action<SiteDailyReport> apply, CancellationToken ct)
    {
        var contract = await P.LoadBoqContractAsync(_db, contractId, ct);
        P.EnsureEditable(contract);
        var report = await LoadTrackedAsync(contractId, reportId, ct);
        EnsureStatus(report, from);
        apply(report);
        await _db.SaveChangesAsync(ct);
        return await GetAsync(contractId, reportId, ct);
    }

    private static void EnsureStatus(SiteDailyReport r, string expected)
    {
        if (r.Status != expected) throw new ConflictException(string.Format(Messages.SiteReportStatusInvalid, r.Status));
    }

    /// <summary>
    /// يتحقق من الأسطر كلها ويجمع أخطاءها برقم كل سطر، ثم يستبدل أسطر التقرير. تكلفة ساعة المورد وتكلفة وحدة المادة
    /// تُؤخذان وقت الحفظ (المسودة وحدها تُحفظ، فما قُدّم أو اعتُمد ثابت).
    /// </summary>
    private async Task ApplyAsync(SiteDailyReport report, CommercialContract contract, SaveSiteDailyReportDto d, CancellationToken ct)
    {
        var errors = new List<string>();
        var lines = contract.Items.Where(i => !i.IsGroup).ToDictionary(i => i.Id);
        var hours = d.Hours ?? new List<SiteHourEntryDto>();
        var usages = d.Resources ?? new List<SiteResourceUsageDto>();
        var materials = d.Materials ?? new List<SiteMaterialUsageDto>();

        var newHours = new List<SiteHourEntry>();
        var slotKeys = new HashSet<(int, Guid)>();
        for (var k = 0; k < hours.Count; k++)
        {
            var h = hours[k];
            var row = k + 1;
            var timeErrors = new List<string>();
            var start = P.ParseClock(h.Start, timeErrors);
            var end = P.ParseClock(h.End, timeErrors);
            if (timeErrors.Count > 0 || end <= start) errors.Add(string.Format(Messages.SiteHourSlotInvalid, row));
            if (!lines.ContainsKey(h.ContractItemId)) errors.Add(string.Format(Messages.SiteHourItemInvalid, row));
            if (h.TargetQuantity < 0 || h.ActualQuantity < 0) errors.Add(string.Format(Messages.SiteHourQuantityNegative, row));
            if (h.DowntimeMinutes < 0 || (end > start && h.DowntimeMinutes > end - start)) errors.Add(string.Format(Messages.SiteDowntimeInvalid, row));
            var reason = string.IsNullOrWhiteSpace(h.DowntimeReason) ? null : h.DowntimeReason.Trim();
            if ((h.DowntimeMinutes > 0 && !SiteDowntimeReasons.IsValid(reason)) || (reason != null && !SiteDowntimeReasons.IsValid(reason)))
                errors.Add(string.Format(Messages.SiteDowntimeReasonRequired, row));
            if (!slotKeys.Add((start, h.ContractItemId))) errors.Add(string.Format(Messages.SiteHourDuplicate, row));
            newHours.Add(new SiteHourEntry
            {
                SlotStart = start, SlotEnd = end, ContractItemId = h.ContractItemId, TargetQuantity = h.TargetQuantity, ActualQuantity = h.ActualQuantity,
                DowntimeMinutes = h.DowntimeMinutes, DowntimeReason = h.DowntimeMinutes > 0 ? reason : null, Notes = Trim(h.Notes, 300), SortOrder = row,
            });
        }

        var resourceIds = usages.Select(u => u.ProjectResourceId).Distinct().ToList();
        var resources = await _db.Set<ProjectResource>().AsNoTracking()
            .Where(r => r.ContractId == contract.Id && resourceIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        var newUsages = new List<SiteResourceUsage>();
        var usageKeys = new HashSet<(Guid, Guid?)>();
        for (var k = 0; k < usages.Count; k++)
        {
            var u = usages[k];
            var row = k + 1;
            if (!resources.TryGetValue(u.ProjectResourceId, out var res) || !res.IsActive) errors.Add(string.Format(Messages.SiteResourceInvalid, row));
            if (u.Hours < 0 || u.OvertimeHours < 0 || u.IdleHours < 0 || u.Hours + u.OvertimeHours + u.IdleHours is <= 0 or > 24)
                errors.Add(string.Format(Messages.SiteResourceHoursInvalid, row));
            if (u.ContractItemId is Guid item && !lines.ContainsKey(item)) errors.Add(string.Format(Messages.SiteResourceItemInvalid, row));
            var idleReason = string.IsNullOrWhiteSpace(u.IdleReason) ? null : u.IdleReason.Trim();
            if (u.IdleHours > 0 && !SiteDowntimeReasons.IsValid(idleReason)) errors.Add(string.Format(Messages.SiteResourceIdleReasonRequired, row));
            if (!usageKeys.Add((u.ProjectResourceId, u.ContractItemId))) errors.Add(string.Format(Messages.SiteResourceDuplicate, row));
            var hourly = res?.HourlyCost ?? 0;
            var overtime = res?.OvertimeHourlyCost ?? 0;
            newUsages.Add(new SiteResourceUsage
            {
                ProjectResourceId = u.ProjectResourceId, ContractItemId = u.ContractItemId, Hours = u.Hours, OvertimeHours = u.OvertimeHours,
                IdleHours = u.IdleHours, IdleReason = u.IdleHours > 0 ? idleReason : null, HourlyCost = hourly, OvertimeHourlyCost = overtime,
                // التوقف المدفوع جزء من التكلفة ويُعرض منفصلاً
                Cost = DocumentPricing.Round((u.Hours + u.IdleHours) * hourly + u.OvertimeHours * overtime), Notes = Trim(u.Notes, 300),
            });
        }

        // المواد من مكتبة المقاولات (لا صنف مخزني مباشر): الوحدة وتكلفتها من المادة، والصنف من ربطها إن وُجد
        var materialIds = materials.Where(m => m.ResourceId.HasValue).Select(m => m.ResourceId!.Value).Distinct().ToList();
        var library = await _db.Set<ConstructionResource>().AsNoTracking().Include(r => r.Components)
            .Where(r => materialIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        var newMaterials = new List<SiteMaterialUsage>();
        var materialKeys = new HashSet<(Guid, Guid)>();
        for (var k = 0; k < materials.Count; k++)
        {
            var m = materials[k];
            var row = k + 1;
            if (!lines.ContainsKey(m.ContractItemId)) errors.Add(string.Format(Messages.SiteMaterialItemInvalid, row));
            var material = m.ResourceId is Guid rid ? library.GetValueOrDefault(rid) : null;
            if (material == null || !ProjectMaterialAnalysis.IsMaterial(material)) errors.Add(string.Format(Messages.SiteMaterialResourceInvalid, row));
            if (m.Quantity <= 0) errors.Add(string.Format(Messages.SiteMaterialQuantityInvalid, row));
            if (m.ResourceId is Guid key && !materialKeys.Add((m.ContractItemId, key))) errors.Add(string.Format(Messages.SiteMaterialDuplicate, row));
            newMaterials.Add(new SiteMaterialUsage
            {
                ContractItemId = m.ContractItemId, ResourceId = material?.Id, ProductId = material?.ProductId, Quantity = m.Quantity,
                Unit = material?.Unit ?? string.Empty, UnitCost = material?.StandardCost ?? 0, Notes = Trim(m.Notes, 300),
            });
        }
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        report.Notes = Trim(d.Notes, 1000);
        foreach (var old in report.Hours.ToList()) _db.Remove(old);
        foreach (var old in report.Resources.ToList()) _db.Remove(old);
        foreach (var old in report.Materials.ToList()) _db.Remove(old);
        report.Hours.Clear(); report.Resources.Clear(); report.Materials.Clear();
        foreach (var h in newHours) report.Hours.Add(h);
        foreach (var u in newUsages) report.Resources.Add(u);
        foreach (var m in newMaterials) report.Materials.Add(m);
    }

    // ---------- التقارير ----------

    public async Task<List<SiteHourlyRowDto>> HourlyAsync(Guid contractId, SiteReportFilterDto filter, CancellationToken ct = default)
    {
        var contract = await P.LoadBoqContractAsync(_db, contractId, ct);
        var reports = await ReportsQuery(contractId, filter.From, filter.To, filter.IncludeUnapproved).ToListAsync(ct);
        var lines = contract.Items.ToDictionary(i => i.Id);
        return reports.OrderBy(r => r.ReportDate)
            .SelectMany(r => r.Hours.Where(h => filter.ContractItemId == null || h.ContractItemId == filter.ContractItemId)
                .OrderBy(h => h.SlotStart).ThenBy(h => h.SortOrder)
                .Select(h => new SiteHourlyRowDto
                {
                    Date = r.ReportDate, Status = r.Status, Start = P.Clock(h.SlotStart), End = P.Clock(h.SlotEnd), ContractItemId = h.ContractItemId,
                    ItemLabel = Label(lines, h.ContractItemId), Unit = lines.GetValueOrDefault(h.ContractItemId)?.Unit ?? string.Empty,
                    Target = h.TargetQuantity, Actual = h.ActualQuantity, AchievementPercent = Percent(h.ActualQuantity, h.TargetQuantity),
                    DowntimeMinutes = h.DowntimeMinutes, DowntimeReason = h.DowntimeReason,
                }))
            .ToList();
    }

    public async Task<List<SiteProductivityRowDto>> ProductivityAsync(Guid contractId, SiteReportFilterDto filter, CancellationToken ct = default)
    {
        var contract = await P.LoadBoqContractAsync(_db, contractId, ct);
        var reports = await ReportsQuery(contractId, filter.From, filter.To, filter.IncludeUnapproved).ToListAsync(ct);
        var ctx = await ContextAsync(contract, reports, ct);
        return reports.OrderBy(r => r.ReportDate)
            .SelectMany(r => Summarize(r, ctx).Items
                .Where(i => filter.ContractItemId == null || i.ContractItemId == filter.ContractItemId)
                .Select(i => new SiteProductivityRowDto
                {
                    Date = r.ReportDate, Status = r.Status, ContractItemId = i.ContractItemId, ItemLabel = i.Label, Unit = i.Unit, Target = i.Target,
                    Actual = i.Actual, AchievementPercent = i.AchievementPercent, DowntimeMinutes = i.DowntimeMinutes, Cost = i.Cost, CostPerUnit = i.CostPerUnit,
                }))
            .ToList();
    }

    public async Task<List<SiteResourceRowDto>> ResourceUsageAsync(Guid contractId, SiteReportFilterDto filter, CancellationToken ct = default)
    {
        var contract = await P.LoadBoqContractAsync(_db, contractId, ct);
        var reports = await ReportsQuery(contractId, filter.From, filter.To, filter.IncludeUnapproved).ToListAsync(ct);
        var ctx = await ContextAsync(contract, reports, ct);
        return reports.OrderBy(r => r.ReportDate)
            .SelectMany(r => r.Resources.Where(u => filter.ProjectResourceId == null || u.ProjectResourceId == filter.ProjectResourceId)
                .GroupBy(u => u.ProjectResourceId)
                .Select(g =>
                {
                    var res = ctx.Resources.GetValueOrDefault(g.Key);
                    return new SiteResourceRowDto
                    {
                        Date = r.ReportDate, Status = r.Status, ProjectResourceId = g.Key, ResourceName = res?.Name ?? string.Empty, Kind = res?.Kind ?? string.Empty,
                        Hours = g.Sum(u => u.Hours), OvertimeHours = g.Sum(u => u.OvertimeHours), IdleHours = g.Sum(u => u.IdleHours), Cost = g.Sum(u => u.Cost),
                    };
                }))
            .ToList();
    }

    // ---------- الحسابات ----------

    /// <summary>بيانات العرض المشتركة: بنود العقد، وموارد المشروع، ومواد المكتبة، والأصناف (لأسماء الأسطر القديمة بلا مادة).</summary>
    public sealed record SummaryContext(
        IReadOnlyDictionary<Guid, CommercialContractItem> Lines,
        IReadOnlyDictionary<Guid, ProjectResource> Resources,
        IReadOnlyDictionary<Guid, ConstructionResource> Materials,
        IReadOnlyDictionary<Guid, Product> Products);

    private async Task<SummaryContext> ContextAsync(CommercialContract contract, IEnumerable<SiteDailyReport> reports, CancellationToken ct)
    {
        var list = reports.ToList();
        var used = list.SelectMany(r => r.Materials).ToList();
        var materialIds = used.Where(m => m.ResourceId.HasValue).Select(m => m.ResourceId!.Value).Distinct().ToList();
        var productIds = used.Where(m => m.ResourceId == null && m.ProductId.HasValue).Select(m => m.ProductId!.Value).Distinct().ToList();
        var resources = await _db.Set<ProjectResource>().AsNoTracking().Where(r => r.ContractId == contract.Id).ToDictionaryAsync(r => r.Id, ct);
        var materials = await _db.Set<ConstructionResource>().AsNoTracking().Where(r => materialIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        var products = await _db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        return new SummaryContext(contract.Items.ToDictionary(i => i.Id), resources, materials, products);
    }

    /// <summary>
    /// ملخص اليوم: ساعات العمل = مجموع الفترات المميزة، والتوقف = أطول توقف في كل فترة (لا يُعدّ مرتين لبندين في الفترة نفسها)،
    /// ونسبة التحقيق بقيمة البنود (الكمية × سعر البند) لأن الوحدات مختلفة. تكلفة البند = موارده ومواده، وتكلفة المورد بلا بند
    /// تُوزَّع على بنود اليوم بنسبة قيمة منفَّذها (ويوم بلا إنتاج تبقى غير موزعة). تكلفة الوحدة = تكلفة البند ÷ المنفَّذ.
    /// </summary>
    public static SiteDailySummaryDto Summarize(SiteDailyReport r, SummaryContext ctx)
    {
        decimal Price(Guid item) => ctx.Lines.GetValueOrDefault(item)?.UnitPrice ?? 0;
        var slots = r.Hours.GroupBy(h => (h.SlotStart, h.SlotEnd)).ToList();
        var workingHours = slots.Sum(g => (g.Key.SlotEnd - g.Key.SlotStart) / 60m);
        var downtimeMinutes = slots.Sum(g => g.Max(h => h.DowntimeMinutes));
        var resourceCost = r.Resources.Sum(u => u.Cost);
        var materialCost = r.Materials.Sum(m => DocumentPricing.Round(m.Quantity * m.UnitCost));
        var total = resourceCost + materialCost;

        var itemIds = r.Hours.Select(h => h.ContractItemId)
            .Concat(r.Resources.Where(u => u.ContractItemId.HasValue).Select(u => u.ContractItemId!.Value))
            .Concat(r.Materials.Select(m => m.ContractItemId)).Distinct().ToList();
        var actual = itemIds.ToDictionary(i => i, i => r.Hours.Where(h => h.ContractItemId == i).Sum(h => h.ActualQuantity));
        var actualValue = itemIds.ToDictionary(i => i, i => actual[i] * Price(i));
        var producedValue = actualValue.Values.Sum();
        var unassigned = r.Resources.Where(u => u.ContractItemId == null).Sum(u => u.Cost);

        var items = itemIds.Select(i =>
        {
            var target = r.Hours.Where(h => h.ContractItemId == i).Sum(h => h.TargetQuantity);
            var direct = r.Resources.Where(u => u.ContractItemId == i).Sum(u => u.Cost)
                + r.Materials.Where(m => m.ContractItemId == i).Sum(m => DocumentPricing.Round(m.Quantity * m.UnitCost));
            var share = producedValue > 0 ? unassigned * actualValue[i] / producedValue : 0;
            var cost = DocumentPricing.Round(direct + share);
            return new SiteItemSummaryDto
            {
                ContractItemId = i, Label = Label(ctx.Lines, i), Unit = ctx.Lines.GetValueOrDefault(i)?.Unit ?? string.Empty,
                Target = target, Actual = actual[i], AchievementPercent = Percent(actual[i], target),
                DowntimeMinutes = r.Hours.Where(h => h.ContractItemId == i).Sum(h => h.DowntimeMinutes),
                Cost = cost, CostPerUnit = actual[i] > 0 ? Math.Round(cost / actual[i], 4) : 0,
            };
        }).OrderBy(i => ctx.Lines.GetValueOrDefault(i.ContractItemId)?.SortOrder ?? int.MaxValue).ToList();

        var targetValue = r.Hours.Sum(h => h.TargetQuantity * Price(h.ContractItemId));
        var reasons = r.Hours.Where(h => h.DowntimeMinutes > 0 && h.DowntimeReason != null).Select(h => (Reason: h.DowntimeReason!, Minutes: h.DowntimeMinutes))
            .Concat(r.Resources.Where(u => u.IdleHours > 0 && u.IdleReason != null).Select(u => (Reason: u.IdleReason!, Minutes: (int)Math.Round(u.IdleHours * 60))))
            .GroupBy(x => x.Reason)
            .Select(g => new SiteDowntimeReasonDto { Reason = g.Key, Minutes = g.Sum(x => x.Minutes), Entries = g.Count() })
            .OrderByDescending(x => x.Minutes).ToList();
        var low = r.Hours.Where(h => h.TargetQuantity > 0 && h.ActualQuantity < h.TargetQuantity * LowProductivityRatio)
            .OrderBy(h => h.SlotStart).ThenBy(h => h.SortOrder)
            .Select(h => new SiteLowSlotDto
            {
                Start = P.Clock(h.SlotStart), End = P.Clock(h.SlotEnd), ItemLabel = Label(ctx.Lines, h.ContractItemId), Target = h.TargetQuantity,
                Actual = h.ActualQuantity, AchievementPercent = Percent(h.ActualQuantity, h.TargetQuantity), Reason = h.DowntimeReason,
            }).ToList();

        return new SiteDailySummaryDto
        {
            WorkingHours = Math.Round(workingHours, 2), DowntimeHours = Math.Round(downtimeMinutes / 60m, 2),
            IdleResourceHours = r.Resources.Sum(u => u.IdleHours), ResourceCost = resourceCost, MaterialCost = materialCost, TotalCost = total,
            CostPerHour = workingHours > 0 ? DocumentPricing.Round(total / workingHours) : 0,
            TargetValue = DocumentPricing.Round(targetValue), ActualValue = DocumentPricing.Round(producedValue),
            AchievementPercent = Percent(producedValue, targetValue),
            UnallocatedCost = producedValue > 0 ? 0 : unassigned,
            Items = items, Reasons = reasons, LowSlots = low,
        };
    }

    private static decimal Percent(decimal actual, decimal target) => target > 0 ? Math.Round(actual / target * 100m, 2) : 0;

    private static string Label(IReadOnlyDictionary<Guid, CommercialContractItem> lines, Guid id)
        => lines.TryGetValue(id, out var line) ? P.LineLabel(line) : string.Empty;

    // ---------- مساعدات ----------

    private IQueryable<SiteDailyReport> Full()
        => _db.Set<SiteDailyReport>().Include(r => r.Hours).Include(r => r.Resources).Include(r => r.Materials).AsSplitQuery();

    private IQueryable<SiteDailyReport> ReportsQuery(Guid contractId, DateTime? from, DateTime? to, bool includeUnapproved)
    {
        var q = Full().AsNoTracking().Where(r => r.ContractId == contractId);
        if (from.HasValue) { var f = from.Value.Date; q = q.Where(r => r.ReportDate >= f); }
        if (to.HasValue) { var t = to.Value.Date; q = q.Where(r => r.ReportDate <= t); }
        if (!includeUnapproved) q = q.Where(r => r.Status == St.Approved);
        return q;
    }

    private async Task<SiteDailyReport> LoadTrackedAsync(Guid contractId, Guid reportId, CancellationToken ct)
        => await Full().FirstOrDefaultAsync(r => r.Id == reportId && r.ContractId == contractId, ct)
            ?? throw new NotFoundException(Messages.SiteReportNotFound);

    private async Task<List<(int Start, int End)>> SlotsAsync(Guid contractId, CancellationToken ct)
    {
        var profile = await _db.Set<ConstructionProjectProfile>().AsNoTracking().FirstOrDefaultAsync(p => p.ContractId == contractId, ct)
            ?? new ConstructionProjectProfile { ContractId = contractId };
        return P.HourSlots(profile.WorkdayStartMinutes, profile.WorkdayEndMinutes, profile.BreakStartMinutes, profile.BreakEndMinutes);
    }

    private async Task<SiteDailyReportDto> ToDtoAsync(CommercialContract contract, SiteDailyReport r, CancellationToken ct)
    {
        var ctx = await ContextAsync(contract, new[] { r }, ct);
        var slots = await SlotsAsync(contract.Id, ct);
        return new SiteDailyReportDto
        {
            Id = r.Id, ContractId = r.ContractId, ReportDate = r.ReportDate, Status = r.Status, Notes = r.Notes, ReturnNote = r.ReturnNote,
            PreparedBy = r.PreparedBy, SubmittedAt = r.SubmittedAt, SubmittedBy = r.SubmittedBy, ApprovedAt = r.ApprovedAt, ApprovedBy = r.ApprovedBy,
            Slots = slots.Select(s => new ProjectHourSlotDto { Start = P.Clock(s.Start), End = P.Clock(s.End), Hours = Math.Round((s.End - s.Start) / 60m, 2) }).ToList(),
            Hours = r.Hours.OrderBy(h => h.SlotStart).ThenBy(h => h.SortOrder).Select(h => new SiteHourEntryDto
            {
                Id = h.Id == Guid.Empty ? null : h.Id, Start = P.Clock(h.SlotStart), End = P.Clock(h.SlotEnd), ContractItemId = h.ContractItemId,
                ItemLabel = Label(ctx.Lines, h.ContractItemId), Unit = ctx.Lines.GetValueOrDefault(h.ContractItemId)?.Unit,
                TargetQuantity = h.TargetQuantity, ActualQuantity = h.ActualQuantity, DowntimeMinutes = h.DowntimeMinutes, DowntimeReason = h.DowntimeReason,
                Notes = h.Notes, AchievementPercent = Percent(h.ActualQuantity, h.TargetQuantity),
            }).ToList(),
            Resources = r.Resources.Select(u => new SiteResourceUsageDto
            {
                Id = u.Id, ProjectResourceId = u.ProjectResourceId, ResourceName = ctx.Resources.GetValueOrDefault(u.ProjectResourceId)?.Name,
                Kind = ctx.Resources.GetValueOrDefault(u.ProjectResourceId)?.Kind, ContractItemId = u.ContractItemId, Hours = u.Hours,
                OvertimeHours = u.OvertimeHours, IdleHours = u.IdleHours, IdleReason = u.IdleReason, HourlyCost = u.HourlyCost,
                OvertimeHourlyCost = u.OvertimeHourlyCost, Cost = u.Cost, Notes = u.Notes,
            }).OrderBy(u => u.ResourceName).ToList(),
            Materials = r.Materials.Select(m => new SiteMaterialUsageDto
            {
                Id = m.Id, ContractItemId = m.ContractItemId, ResourceId = m.ResourceId, ProductId = m.ProductId,
                ResourceCode = m.ResourceId is Guid rc ? ctx.Materials.GetValueOrDefault(rc)?.Code : null,
                ResourceName = m.ResourceId is Guid rn ? ctx.Materials.GetValueOrDefault(rn)?.NameAr
                    : m.ProductId is Guid pn ? ctx.Products.GetValueOrDefault(pn)?.NameAr : null,
                Unit = m.Unit, Quantity = m.Quantity, UnitCost = m.UnitCost, Cost = DocumentPricing.Round(m.Quantity * m.UnitCost), Notes = m.Notes,
            }).ToList(),
            Summary = Summarize(r, ctx),
        };
    }

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var t = value.Trim();
        return t.Length > max ? t[..max] : t;
    }
}
