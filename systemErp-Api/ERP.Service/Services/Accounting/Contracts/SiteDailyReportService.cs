using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using P = ERP.Service.Services.Accounting.ConstructionProjectService;
using St = ERP.Core.Models.Accounting.SiteDailyReportStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// التقرير اليومي للموقع (العقد هو المشروع): واحد لكل يوم، بفترات الإنجاز (ساعة لكل فترة من ساعات عمل المشروع، يعدّلها المشرف)
/// لكل بند بمستهدفها من خطة الإنجاز ومنفّذها وتوقفها، والموارد المستخدمة بتكلفة ساعتها المثبّتة، والمواد المستهلكة.
/// المسودة تُعدَّل، ثم يقدّمها المشرف ويعتمدها المهندس المسؤول (أو يعيدها بسبب)؛ والمعتمد يُعاد فتحه بسبب فيُرحَّل الفرق وحده عند إعادة اعتماده.
/// الاعتماد يرحّل التكلفة: العمالة والمعدات المملوكة بقيد تحميل، والمستأجرة استحقاق إيجار يُسوّى بفواتير المؤجر (<see cref="IRentalAccrualService"/>).
/// </summary>
public class SiteDailyReportService : ISiteDailyReportService
{
    /// <summary>الفترة التي قلّ إنجازها عن هذه النسبة من المستهدف تُعدّ منخفضة الإنتاجية.</summary>
    public const decimal LowProductivityRatio = 0.9m;

    private readonly ErpDbContext _db;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;
    private readonly IAccountingPostingService _posting;
    private readonly IRentalAccrualService _rentals;
    private readonly IAuditService _audit;
    private readonly IEquipmentCostAllocationService _equipmentCost;

    public SiteDailyReportService(ErpDbContext db, ITransactionRunner tx, ICurrentUser user, IAccountingPostingService posting, IRentalAccrualService rentals, IAuditService audit,
        IEquipmentCostAllocationService equipmentCost)
    {
        _db = db; _tx = tx; _user = user; _posting = posting; _rentals = rentals; _audit = audit; _equipmentCost = equipmentCost;
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
            await ExecutionPlanService.EnsureExecutionAllowedAsync(_db, contractId, token);
            if (request.ReportDate == default) throw new ValidationFailedException(Messages.SiteReportDateRequired);
            var day = request.ReportDate.Date;
            EnsureReportDate(day, contract);
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
            await ExecutionPlanService.EnsureExecutionAllowedAsync(_db, contractId, token);
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
        // مسودة أُعيد فتحها بعد ترحيل تكلفتها: قيودها واستحقاقها قائمة، فلا تُحذف (تُعدَّل ويُعاد اعتمادها فيُرحَّل الفرق)
        if (report.CostRevision > 0 || report.CostJournalEntryId != null) throw new ConflictException(Messages.SiteReportHasPostedCost);
        // أرشيف بنود المسودة يُحذف معها
        await SiteReportAttachmentService.RemoveForReportsAsync(_db, _db.Set<SiteDailyReport>().Where(r => r.Id == report.Id).Select(r => r.Id), ct);
        _db.Remove(report);
        await _db.SaveChangesAsync(ct);
    }

    public Task<SiteDailyReportDto> SubmitAsync(Guid contractId, Guid reportId, CancellationToken ct = default)
        => TransitionAsync(contractId, reportId, St.Draft, r =>
        {
            if (r.Hours.Count == 0) throw new ValidationFailedException(Messages.SiteReportEmpty);
            r.Status = St.Submitted; r.SubmittedAt = DateTime.UtcNow; r.SubmittedBy = _user.Name; r.SubmittedByUserId = _user.UserId; r.ReturnNote = null;
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
            await ApprovalSeparation.EnsureAsync(_db, _user.UserId, token, report.PreparedByUserId, report.SubmittedByUserId);
            // يُعاد فحص سقف البنود لحظة الاعتماد: أمر تغيير أنقص الكمية، أو تقرير آخر حُفظ بعد هذا
            var boqErrors = new List<string>();
            await CheckBoqCapAsync(report, contract, report.Hours, boqErrors, token);
            if (boqErrors.Count > 0) throw new ValidationFailedException(boqErrors[0], boqErrors);
            report.Status = St.Approved; report.ApprovedAt = DateTime.UtcNow; report.ApprovedBy = _user.Name; report.ApprovedByUserId = _user.UserId;
            await PostCostAsync(report, contract, token);
            await _db.SaveChangesAsync(token);
            // معدات طريقة الإهلاك: حصة المشروع من إهلاك الفترة بالساعات المعتمدة (بعد حفظ الاعتماد ليدخل التقرير في الساعات)
            await _equipmentCost.ReallocateForReportAsync(report.Id, token);
            await _audit.LogAsync("SITE_DAILY_REPORT_APPROVED", nameof(SiteDailyReport), reportId.ToString(),
                $"اعتماد تقرير يومي • عقد {contractId} • تاريخ {report.ReportDate:yyyy-MM-dd} • تكاليف محمّلة {report.PostedLaborCost + report.PostedEquipmentCost:N2}", token);
        }, ct);
        return await GetAsync(contractId, reportId, ct);
    }

    /// <summary>
    /// ترحيل تكلفة التقارير المعتمدة التي لم تُرحَّل بعد (اعتُمدت قبل تفعيل التحميل أو قبل ربط العقد بمركز تكلفة): العمالة والمعدات
    /// المملوكة واستحقاق المستأجرة. استحقاق المستأجرة يُطابَق تلقائياً بما رُحِّل من فواتير مؤجرها، فلا تُحتسب مرتين.
    /// </summary>
    public Task<int> PostPendingCostsAsync(Guid contractId, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var contract = await P.LoadBoqContractAsync(_db, contractId, token);
            var reports = await Full().Where(r => r.ContractId == contractId && r.Status == St.Approved && r.CostRevision == 0)
                .OrderBy(r => r.ReportDate).ToListAsync(token);
            var posted = 0;
            foreach (var r in reports)
            {
                await PostCostAsync(r, contract, token);
                if (r.CostRevision > 0) posted++;
            }
            await _db.SaveChangesAsync(token);
            foreach (var r in reports) await _equipmentCost.ReallocateForReportAsync(r.Id, token);
            return posted;
        }, ct);

    /// <summary>
    /// ترحيل تكلفة التقرير المعتمد بالفرق عمّا رُحِّل له من قبل (الاعتماد الأول: كلها). العمالة والمهندسون على «تكلفة عمالة
    /// المشاريع» والمعدات المملوكة على «تكلفة معدات المشاريع» بمركز المشروع، مقابل «التكاليف المحمّلة على المشاريع» بلا مركز
    /// (فلا يتغير إجمالي مصروف المنشأة ولا تُحتسب الرواتب والإهلاك مرتين)؛ والمستأجرة استحقاق إيجار (<see cref="IRentalAccrualService"/>).
    /// بعد إعادة فتح التقرير تبقى القيود الأصلية، ويُرحَّل الفرق فقط بقيد تعديل بتاريخ اليوم، ومرّة الترحيل تمنع تكراره.
    /// عقد بلا مركز: لا قيد (يبقى تشغيلياً ويُرحَّل لاحقاً من «ترحيل التكاليف المعلّقة»).
    /// </summary>
    private async Task PostCostAsync(SiteDailyReport report, CommercialContract contract, CancellationToken ct)
    {
        if (contract.CostCenterId is not Guid cc) return;
        var revision = ++report.CostRevision;
        var kinds = await _db.Set<ProjectResource>().AsNoTracking().Where(x => x.ContractId == contract.Id)
            .ToDictionaryAsync(x => x.Id, x => x.Kind, ct);
        string? KindOf(Guid id) => kinds.GetValueOrDefault(id);
        var labor = DocumentPricing.Round(report.Resources.Where(u => KindOf(u.ProjectResourceId) is ProjectResourceKinds.Labor or ProjectResourceKinds.Engineer).Sum(u => u.Cost));
        var equipment = DocumentPricing.Round(report.Resources.Where(u => KindOf(u.ProjectResourceId) == ProjectResourceKinds.OwnedEquipment).Sum(u => u.Cost));
        var laborDelta = labor - report.PostedLaborCost;
        var equipmentDelta = equipment - report.PostedEquipmentCost;

        if (laborDelta != 0 || equipmentDelta != 0)
        {
            await ContractAccountsSetup.EnsureMissingAsync(_db, ct);
            var settings = await PostingAccounts.SettingsAsync(_db, ct);
            var note = revision <= 1
                ? $"تحميل عمالة ومعدات التقرير اليومي {report.ReportDate:yyyy-MM-dd} - عقد {contract.ContractNumber}"
                : $"فرق إعادة اعتماد تحميل عمالة ومعدات التقرير اليومي {report.ReportDate:yyyy-MM-dd} - عقد {contract.ContractNumber}";
            var lines = new List<PostingLine>();
            void Add(string key, decimal amount, Guid? center)
            {
                if (amount == 0) return;
                var account = settings.ContractAccount(key);
                lines.Add(amount > 0 ? new(account, amount, 0, note, center) : new(account, 0, -amount, note, center));
            }
            Add(nameof(AccountingSettings.ProjectLaborCostAccountCode), laborDelta, cc);
            Add(nameof(AccountingSettings.ProjectEquipmentCostAccountCode), equipmentDelta, cc);
            Add(nameof(AccountingSettings.ProjectAbsorbedCostAccountCode), -(laborDelta + equipmentDelta), null);
            var posted = await _posting.PostAsync(new GenericPostingRequest
            {
                Date = revision <= 1 ? report.ReportDate : SaudiTime.Today, Description = note, SourceType = ProjectCostService.SiteCostSource,
                SourceId = report.Id, SourceNumber = contract.ContractNumber, WarehouseId = contract.WarehouseId, Lines = lines,
            }, ct);
            report.CostJournalEntryId ??= posted.JournalEntryId;
            report.PostedLaborCost = labor;
            report.PostedEquipmentCost = equipment;
        }
        await _rentals.PostReportAsync(report, contract, revision, ct);
    }

    /// <summary>
    /// إعادة فتح التقرير المعتمد للتعديل (صلاحية الاعتماد، بسبب إلزامي): يعود مسودة وقيوده كما هي؛ وعند اعتماده ثانية يُرحَّل
    /// الفرق وحده (قيد تعديل) وتُعاد موازنة تسوية فواتير الإيجار. سجل التدقيق يحفظ الحالة قبل وبعد والسبب.
    /// </summary>
    public async Task<SiteDailyReportDto> ReopenAsync(Guid contractId, Guid reportId, SiteReportActionDto request, CancellationToken ct = default)
    {
        var note = request.Note?.Trim();
        if (string.IsNullOrEmpty(note)) throw new ValidationFailedException(Messages.SiteReportReopenNoteRequired);
        await EnsureNotCertifiedAsync(contractId, reportId, ct);
        return await TransitionAsync(contractId, reportId, St.Approved, r =>
        {
            r.Status = St.Draft; r.ReopenNote = note.Length > 500 ? note[..500] : note; r.ReturnNote = null;
            r.SubmittedAt = null; r.SubmittedBy = null; r.SubmittedByUserId = null; r.ApprovedAt = null; r.ApprovedBy = null; r.ApprovedByUserId = null;
        }, ct, "SITE_DAILY_REPORT_REOPENED", $"إعادة فتح تقرير • عقد {contractId} • ملاحظة: {note}");
    }

    public Task<SiteDailyReportDto> ReturnAsync(Guid contractId, Guid reportId, SiteReportActionDto request, CancellationToken ct = default)
    {
        var note = request.Note?.Trim();
        if (string.IsNullOrEmpty(note)) throw new ValidationFailedException(Messages.SiteReportReturnNoteRequired);
        return TransitionAsync(contractId, reportId, St.Submitted, r =>
        {
            r.Status = St.Draft; r.ReturnNote = note.Length > 500 ? note[..500] : note; r.SubmittedAt = null; r.SubmittedBy = null; r.SubmittedByUserId = null;
        }, ct);
    }

    private async Task<SiteDailyReportDto> TransitionAsync(Guid contractId, Guid reportId, string from, Action<SiteDailyReport> apply,
        CancellationToken ct, string? action = null, string? details = null)
    {
        var contract = await P.LoadBoqContractAsync(_db, contractId, ct);
        P.EnsureEditable(contract);
        var report = await LoadTrackedAsync(contractId, reportId, ct);
        EnsureStatus(report, from);
        apply(report);
        await _db.SaveChangesAsync(ct);
        if (!string.IsNullOrWhiteSpace(action))
            await _audit.LogAsync(action, nameof(SiteDailyReport), reportId.ToString(),
                details ?? $"تحديث حالة تقرير {report.ReportDate:yyyy-MM-dd} على عقد {contractId}", ct);
        return await GetAsync(contractId, reportId, ct);
    }

    private static void EnsureStatus(SiteDailyReport r, string expected)
    {
        if (r.Status != expected) throw new ConflictException(string.Format(Messages.SiteReportStatusInvalid, r.Status));
    }

    /// <summary>
    /// التقرير الذي يقع تاريخه داخل فترة مستخلص معتمد أو مفوتر لا يُعاد فتحه: المستخلص اعتُمد على منفّذه، وتعديله يفصل
    /// المطالب به عن الموقع بلا أثر. يُلغى المستخلص أولاً (فتعود كمياته) ثم يُفتح التقرير.
    /// </summary>
    private async Task EnsureNotCertifiedAsync(Guid contractId, Guid reportId, CancellationToken ct)
    {
        var day = await _db.Set<SiteDailyReport>().AsNoTracking().Where(r => r.Id == reportId && r.ContractId == contractId)
            .Select(r => (DateTime?)r.ReportDate).FirstOrDefaultAsync(ct);
        if (day is not DateTime date) return; // غير موجود: يتولاه التحميل برسالته
        var covering = await _db.Set<ContractCertificate>().AsNoTracking()
            .Where(c => c.ContractId == contractId
                && (c.Status == ContractCertificateStatuses.Approved || c.Status == ContractCertificateStatuses.Invoiced)
                && (c.PeriodTo ?? c.CertificateDate) >= date.Date)
            .OrderBy(c => c.SequenceNo).Select(c => new { c.SequenceNo, Cutoff = c.PeriodTo ?? c.CertificateDate }).FirstOrDefaultAsync(ct);
        if (covering != null)
            throw new ConflictException(string.Format(Messages.SiteReportCoveredByCertificate, covering.SequenceNo, covering.Cutoff.ToString("yyyy-MM-dd")));
    }

    /// <summary>تاريخ التقرير: لا يتجاوز اليوم، ولا يسبق بداية العقد (تاريخ البدء، أو التوقيع إن لم يُحدَّد).</summary>
    private static void EnsureReportDate(DateTime day, CommercialContract contract)
    {
        if (day > SaudiTime.Today) throw new ValidationFailedException(string.Format(Messages.SiteReportDateInFuture, day.ToString("yyyy-MM-dd")));
        if ((contract.StartDate ?? contract.SignedDate) is DateTime start && day < start.Date)
            throw new ValidationFailedException(string.Format(Messages.SiteReportDateBeforeStart, day.ToString("yyyy-MM-dd"), start.ToString("yyyy-MM-dd")));
    }

    /// <summary>
    /// سقف المنفّذ: مجموع الكمية الفعلية للبند في تقارير العقد كلها (بأي حالة، عدا هذا التقرير) مع أسطر هذا التقرير لا يتجاوز
    /// كمية العقد المعدّلة بنسبة التجاوز المسموحة، وهو سقف المستخلص نفسه، فلا يُسجَّل في الموقع ما لا يُطالَب به.
    /// </summary>
    private async Task CheckBoqCapAsync(SiteDailyReport report, CommercialContract contract, IEnumerable<SiteHourEntry> hours, List<string> errors, CancellationToken ct)
    {
        var current = hours.GroupBy(h => h.ContractItemId).ToDictionary(g => g.Key, g => g.Sum(h => h.ActualQuantity));
        if (current.Values.All(q => q <= 0)) return;
        var ids = current.Keys.ToList();
        var reportId = report.Id;
        var others = await (from h in _db.Set<SiteHourEntry>().AsNoTracking()
                            join r in _db.Set<SiteDailyReport>().AsNoTracking() on h.ReportId equals r.Id
                            where r.ContractId == contract.Id && r.Id != reportId && ids.Contains(h.ContractItemId)
                            group h by h.ContractItemId into g
                            select new { g.Key, Qty = g.Sum(x => x.ActualQuantity) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        var items = contract.Items.ToDictionary(i => i.Id);
        foreach (var (itemId, qty) in current)
        {
            if (qty <= 0 || !items.TryGetValue(itemId, out var item)) continue;
            var total = qty + others.GetValueOrDefault(itemId);
            var max = item.RevisedQuantity * (1 + item.OverrunPercent / 100m);
            if (total > max + 0.00005m)
                errors.Add(string.Format(Messages.SiteHourExceedsBoq, P.LineLabel(item), total.ToString("0.####"), max.ToString("0.####"), item.Unit));
        }
    }

    /// <summary>
    /// حجز المورد في اليوم: الشخص أو الآلية نفسها (بالموظف، أو المعدة المسندة، أو الأصل الثابت؛ وإلا مورد المشروع ذاته) لا تتجاوز
    /// ساعاتها (تشغيل وإضافي وتوقف واستعداد) 24 ساعة في اليوم عبر أسطر هذا التقرير وتقارير المشاريع الأخرى في التاريخ نفسه.
    /// العمل في مشروعين باليوم نفسه جائز (التحويل بين المشاريع) ما دام المجموع في حدود اليوم.
    /// </summary>
    private async Task CheckDailyBookingAsync(SiteDailyReport report, IReadOnlyDictionary<Guid, ProjectResource> resources, List<SiteResourceUsage> usages,
        List<string> errors, CancellationToken ct)
    {
        if (usages.Count == 0) return;
        static decimal Total(SiteResourceUsage u) => u.Hours + u.OvertimeHours + u.IdleHours + u.StandbyHours;
        var ids = usages.Select(u => u.ProjectResourceId).Distinct().ToList();
        var equipmentOf = await _db.Set<EquipmentAssignment>().AsNoTracking()
            .Where(a => a.ProjectResourceId != null && ids.Contains(a.ProjectResourceId.Value))
            .Select(a => new { Resource = a.ProjectResourceId!.Value, a.EquipmentId }).ToListAsync(ct);
        var equipmentByResource = equipmentOf.GroupBy(a => a.Resource).ToDictionary(g => g.Key, g => g.First().EquipmentId);
        string KeyOf(Guid resourceId, Guid? employeeId, Guid? fixedAssetId, Guid? equipmentId)
            => employeeId is Guid e ? $"e:{e}" : equipmentId is Guid q ? $"q:{q}" : fixedAssetId is Guid f ? $"a:{f}" : $"r:{resourceId}";
        string Key(Guid resourceId)
        {
            var res = resources.GetValueOrDefault(resourceId);
            return KeyOf(resourceId, res?.EmployeeId, res?.FixedAssetId, equipmentByResource.TryGetValue(resourceId, out var q) ? (Guid?)q : null);
        }

        var mine = usages.GroupBy(u => Key(u.ProjectResourceId))
            .Select(g => new { g.Key, Hours = g.Sum(u => Total(u)), Name = resources.GetValueOrDefault(g.First().ProjectResourceId)?.Name ?? string.Empty }).ToList();

        // موارد المشاريع الأخرى التي تمثل الشخص أو الآلية نفسها
        var employees = resources.Values.Where(r => r.EmployeeId != null).Select(r => r.EmployeeId!.Value).Distinct().ToList();
        var assets = resources.Values.Where(r => r.FixedAssetId != null).Select(r => r.FixedAssetId!.Value).Distinct().ToList();
        var equipment = equipmentByResource.Values.Distinct().ToList();
        var twinRows = await _db.Set<ProjectResource>().AsNoTracking()
            .Where(r => r.ContractId != report.ContractId
                && ((r.EmployeeId != null && employees.Contains(r.EmployeeId.Value)) || (r.FixedAssetId != null && assets.Contains(r.FixedAssetId.Value))))
            .Select(r => new { r.Id, r.EmployeeId, r.FixedAssetId }).ToListAsync(ct);
        var twinEquipment = await _db.Set<EquipmentAssignment>().AsNoTracking()
            .Where(a => a.ContractId != report.ContractId && a.ProjectResourceId != null && equipment.Contains(a.EquipmentId))
            .Select(a => new { Resource = a.ProjectResourceId!.Value, a.EquipmentId }).ToListAsync(ct);
        var twinKeys = new Dictionary<Guid, string>();
        foreach (var t in twinEquipment) twinKeys[t.Resource] = $"q:{t.EquipmentId}";
        foreach (var t in twinRows) twinKeys.TryAdd(t.Id, KeyOf(t.Id, t.EmployeeId, t.FixedAssetId, null));
        // المورد المسند لمعدة يُعرَّف بالمعدة أولاً (كمفتاح هذا التقرير)، فلا يختلف مفتاحاه بين المشروعين
        var twinIds = twinKeys.Keys.ToList();
        var day = report.ReportDate.Date;
        var reportId = report.Id;
        var elsewhere = await (from u in _db.Set<SiteResourceUsage>().AsNoTracking()
                               join r in _db.Set<SiteDailyReport>().AsNoTracking() on u.ReportId equals r.Id
                               join c in _db.Set<CommercialContract>().AsNoTracking() on r.ContractId equals c.Id
                               where r.ReportDate == day && r.Id != reportId && twinIds.Contains(u.ProjectResourceId)
                               select new { u.ProjectResourceId, u.Hours, u.OvertimeHours, u.IdleHours, u.StandbyHours, c.ContractNumber })
                              .ToListAsync(ct);

        foreach (var m in mine)
        {
            var other = elsewhere.Where(x => twinKeys.GetValueOrDefault(x.ProjectResourceId) == m.Key).ToList();
            var total = m.Hours + other.Sum(x => x.Hours + x.OvertimeHours + x.IdleHours + x.StandbyHours);
            if (total <= 24m) continue;
            var projects = other.Select(x => x.ContractNumber).Distinct().ToList();
            var where = projects.Count == 0 ? string.Empty : string.Format(Messages.SiteResourceOtherProjects, string.Join("، ", projects));
            errors.Add(string.Format(Messages.SiteResourceDayOverbooked, m.Name, day.ToString("yyyy-MM-dd"), total.ToString("0.##"), where));
        }
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

        // تقرير اعتُمد من قبل ثم أُعيد فتحه: أسعار أسطره القائمة ثابتة (تكلفة الساعة ونسب الفوترة وتكلفة وحدة المادة) فلا يُعاد
        // تسعير يوم مضى بأسعار اليوم ولا يتغير الفرق المرحّل إلا بتغير الكميات؛ والسطر الجديد بسعر وقت الحفظ
        var reopened = report.CostRevision > 0 || !string.IsNullOrEmpty(report.ReopenNote);
        var frozen = reopened ? report.Resources.GroupBy(u => u.ProjectResourceId).ToDictionary(g => g.Key, g => g.First()) : new Dictionary<Guid, SiteResourceUsage>();
        var frozenMaterials = reopened
            ? report.Materials.Where(m => m.ResourceId != null).GroupBy(m => m.ResourceId!.Value).ToDictionary(g => g.Key, g => g.First().UnitCost)
            : new Dictionary<Guid, decimal>();
        // مورد أُوقف (أُرجعت المعدة أو حُوّلت) يبقى مقبولاً في تقرير كان فيه قبل إيقافه
        var existing = report.Resources.Select(u => u.ProjectResourceId).ToHashSet();

        var resourceIds = usages.Select(u => u.ProjectResourceId).Distinct().ToList();
        var resources = await _db.Set<ProjectResource>().AsNoTracking()
            .Where(r => r.ContractId == contract.Id && resourceIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        // فترة وجود المعدة في المشروع: إسنادها (من - إلى)
        var windows = await _db.Set<EquipmentAssignment>().AsNoTracking()
            .Where(a => a.ContractId == contract.Id && a.ProjectResourceId != null && resourceIds.Contains(a.ProjectResourceId.Value))
            .Select(a => new { Resource = a.ProjectResourceId!.Value, a.FromDate, a.ToDate }).ToListAsync(ct);
        var day = report.ReportDate.Date;
        var newUsages = new List<SiteResourceUsage>();
        var usageKeys = new HashSet<(Guid, Guid?)>();
        for (var k = 0; k < usages.Count; k++)
        {
            var u = usages[k];
            var row = k + 1;
            if (!resources.TryGetValue(u.ProjectResourceId, out var res) || (!res.IsActive && !existing.Contains(res.Id)))
                errors.Add(string.Format(Messages.SiteResourceInvalid, row));
            if (res != null)
            {
                // تاريخ التقرير داخل عقد إيجار المستأجرة وداخل إسناد المعدة للمشروع
                DateTime? winFrom = null, winTo = null;
                if (res.Kind == ProjectResourceKinds.RentedEquipment) { winFrom = res.RentalStart?.Date; winTo = res.RentalEnd?.Date; }
                var assigned = windows.Where(w => w.Resource == res.Id).ToList();
                var inAssignment = assigned.Count == 0 || assigned.Any(w => w.FromDate.Date <= day && (w.ToDate == null || w.ToDate.Value.Date >= day));
                if (!inAssignment)
                {
                    winFrom = assigned.Min(w => w.FromDate).Date;
                    winTo = assigned.Any(w => w.ToDate == null) ? null : assigned.Max(w => w.ToDate)?.Date;
                }
                if (!inAssignment || (winFrom is DateTime wf && day < wf) || (winTo is DateTime wt && day > wt))
                    errors.Add(string.Format(Messages.SiteResourceOutsideWindow, row, winFrom?.ToString("yyyy-MM-dd") ?? "…", winTo?.ToString("yyyy-MM-dd") ?? "…"));
            }
            if (u.Hours < 0 || u.OvertimeHours < 0 || u.IdleHours < 0 || u.StandbyHours < 0
                || u.Hours + u.OvertimeHours + u.IdleHours + u.StandbyHours is <= 0 or > 24)
                errors.Add(string.Format(Messages.SiteResourceHoursInvalid, row));
            var rented = res?.Kind == ProjectResourceKinds.RentedEquipment;
            if (u.StandbyHours > 0 && res != null && !rented) errors.Add(string.Format(Messages.SiteResourceStandbyOnlyRented, row));
            // وحدات العمل (رحلات/مهام) للآليات وحدها: أساس إيرادها التشغيلي، ولا تدخل التكلفة
            var isEquipment = res?.Kind is ProjectResourceKinds.OwnedEquipment or ProjectResourceKinds.RentedEquipment;
            if (u.WorkUnits < 0 || (u.WorkUnits > 0 && res != null && !isEquipment)) errors.Add($"السطر {row}: وحدات العمل (رحلات/مهام) للآليات فقط ولا تكون سالبة.");
            if (u.ContractItemId is Guid item && !lines.ContainsKey(item)) errors.Add(string.Format(Messages.SiteResourceItemInvalid, row));
            var idleReason = string.IsNullOrWhiteSpace(u.IdleReason) ? null : u.IdleReason.Trim();
            if (u.IdleHours > 0 && !SiteDowntimeReasons.IsValid(idleReason)) errors.Add(string.Format(Messages.SiteResourceIdleReasonRequired, row));
            if (!usageKeys.Add((u.ProjectResourceId, u.ContractItemId))) errors.Add(string.Format(Messages.SiteResourceDuplicate, row));
            var prior = frozen.GetValueOrDefault(u.ProjectResourceId);
            var hourly = prior?.HourlyCost ?? res?.HourlyCost ?? 0;
            var overtime = prior?.OvertimeHourlyCost ?? res?.OvertimeHourlyCost ?? 0;
            // شروط فوترة عقد الإيجار للمستأجرة (التوقف والاستعداد بنسبتيهما)؛ غيرها يُحتسب توقفها كاملاً كما كان
            var idlePercent = rented ? prior?.IdleBillingPercent ?? res!.IdleBillingPercent : 100m;
            var standbyPercent = rented ? prior?.StandbyBillingPercent ?? res!.StandbyBillingPercent : 0m;
            var standby = rented ? u.StandbyHours : 0;
            newUsages.Add(new SiteResourceUsage
            {
                ProjectResourceId = u.ProjectResourceId, ContractItemId = u.ContractItemId, Hours = u.Hours, OvertimeHours = u.OvertimeHours,
                IdleHours = u.IdleHours, IdleReason = u.IdleHours > 0 ? idleReason : null, StandbyHours = standby,
                WorkUnits = isEquipment ? u.WorkUnits : 0, HourlyCost = hourly, OvertimeHourlyCost = overtime, IdleBillingPercent = idlePercent, StandbyBillingPercent = standbyPercent,
                // التوقف المدفوع (والاستعداد للمستأجرة) جزء من التكلفة بنسبة الفوترة، ويُعرض منفصلاً
                Cost = DocumentPricing.Round(u.Hours * hourly + u.OvertimeHours * overtime
                    + u.IdleHours * hourly * idlePercent / 100m + standby * hourly * standbyPercent / 100m),
                Notes = Trim(u.Notes, 300),
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
                Unit = material?.Unit ?? string.Empty,
                UnitCost = material != null && frozenMaterials.TryGetValue(material.Id, out var frozenCost) ? frozenCost : material?.StandardCost ?? 0,
                Notes = Trim(m.Notes, 300),
            });
        }
        // الفحوص المجمّعة بعد سلامة الأسطر: سقف البند التراكمي، وحجز المورد في اليوم عبر المشاريع
        if (errors.Count == 0)
        {
            await CheckBoqCapAsync(report, contract, newHours, errors, ct);
            await CheckDailyBookingAsync(report, resources, newUsages, errors, ct);
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
            CostRevision = r.CostRevision, ReopenNote = r.ReopenNote,
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
                OvertimeHours = u.OvertimeHours, IdleHours = u.IdleHours, IdleReason = u.IdleReason, StandbyHours = u.StandbyHours, WorkUnits = u.WorkUnits, HourlyCost = u.HourlyCost,
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
