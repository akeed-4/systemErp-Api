using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

public class CommercialContractService : CrudService<CommercialContract, CommercialContractDto, CreateCommercialContractDto, UpdateCommercialContractDto>, ICommercialContractService
{
    private readonly INumberSequenceService _numbers;
    private readonly ISalesInvoiceService _invoices;
    private readonly IPurchaseInvoiceService _purchases;
    private readonly ICurrentUser _user;

    private readonly ISubscriptionEntitlements _entitlements;

    public CommercialContractService(ErpDbContext db, INumberSequenceService numbers, ISalesInvoiceService invoices, IPurchaseInvoiceService purchases, ICurrentUser user,
        ISubscriptionEntitlements entitlements) : base(db)
    {
        _numbers = numbers; _invoices = invoices; _purchases = purchases; _user = user; _entitlements = entitlements;
    }

    protected override string Label => Messages.LabelContract;
    protected override bool Transactional => true;

    public async Task<ContractStageCatalogDto> GetStageCatalogAsync(CancellationToken ct = default)
    {
        var definitions = await Db.Set<ContractStageDefinition>()
            .AsNoTracking()
            .Where(stage => stage.IsActive)
            .OrderBy(stage => stage.WorkflowType)
            .ThenBy(stage => stage.Number)
            .ToListAsync(ct);

        // دورة واحدة لكل الأنواع؛ قائمة دورة المشتريات القديمة تبقى فارغة حتى تُحدَّث الواجهة (المرحلة 4)
        return new(definitions.Select(ToStageDto).ToArray(), Array.Empty<ContractStageDefinitionDto>());
    }

    /// <summary>ملخص الحالة القديم (ContractStatus) المشتق من حالة العقد.</summary>
    public static ContractStatus StatusFor(string stage) => stage switch
    {
        ContractStageDefinition.Draft => ContractStatus.Draft,
        ContractStageDefinition.Review or ContractStageDefinition.Approval => ContractStatus.UnderReview,
        ContractStageDefinition.Active or ContractStageDefinition.InitialAcceptance => ContractStatus.Active,
        ContractStageDefinition.Closed => ContractStatus.Completed,
        ContractStageDefinition.Suspended => ContractStatus.Suspended,
        ContractStageDefinition.Cancelled => ContractStatus.Terminated,
        _ => ContractStatus.Draft,
    };

    private static ContractStageDefinitionDto ToStageDto(ContractStageDefinition stage)
        => new(stage.Id, stage.WorkflowType, stage.Number, stage.NameAr, stage.NameEn, stage.DescAr, stage.DescEn,
            stage.Icon, stage.BadgeColor, stage.HeaderColor,
            PermissionStore.ParseActions(stage.AllowedActions).Select(PermissionCatalog.Code).ToList(),
            null,
            stage.ShortNameAr, stage.ShortNameEn, stage.Phase, stage.SlaDays);

    public override async Task<CommercialContractDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var dto = await base.GetAsync(id, ct);
        await ApplyCollectionsAsync(new[] { dto }, ct);
        return dto;
    }

    public override async Task<PagedResult<CommercialContractDto>> ListAsync(PaginationParams p, CancellationToken ct = default)
    {
        var page = await base.ListAsync(p, ct);
        await ApplyCollectionsAsync(page.Items, ct);
        return page;
    }

    /// <summary>
    /// التحصيل يُشتق من السندات الموزّعة على فواتير العقد ولا يُخزَّن: المحصّل = مجموع التوزيعات على فواتير دفعاته
    /// وبيانات تسليمه وسجل فواتيره، والدفعة المفوترة «مدفوعة» متى صار المتبقي على فاتورتها صفراً (InvoiceBalances).
    /// </summary>
    private async Task ApplyCollectionsAsync(IReadOnlyCollection<CommercialContractDto> contracts, CancellationToken ct)
    {
        if (contracts.Count == 0) return;
        var ids = contracts.Select(c => c.Id).ToList();
        var logged = await Db.Set<ContractInvoice>().AsNoTracking()
            .Where(i => ids.Contains(i.ContractId) && i.Kind != "credit_note" && i.InvoiceId != null)
            .Select(i => new { i.ContractId, InvoiceId = i.InvoiceId!.Value }).ToListAsync(ct);
        var delivered = await Db.Set<DeliveryNote>().AsNoTracking()
            .Where(n => n.ContractId != null && ids.Contains(n.ContractId.Value) && n.InvoiceId != null)
            .Select(n => new { ContractId = n.ContractId!.Value, InvoiceId = n.InvoiceId!.Value }).ToListAsync(ct);
        var links = logged.Select(x => (x.ContractId, x.InvoiceId))
            .Concat(delivered.Select(x => (x.ContractId, x.InvoiceId)))
            .Concat(contracts.SelectMany(c => c.Milestones.Where(m => m.InvoiceId != null).Select(m => (ContractId: c.Id, InvoiceId: m.InvoiceId!.Value))))
            .Distinct().ToList();
        if (links.Count == 0) return;

        var invoiceIds = links.Select(x => x.InvoiceId).Distinct().ToList();
        var collected = await Db.Set<VoucherAllocation>().AsNoTracking()
            .Where(a => invoiceIds.Contains(a.InvoiceId))
            .GroupBy(a => a.InvoiceId).Select(g => new { g.Key, Sum = g.Sum(a => a.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        var balances = await InvoiceBalances.ForAsync(Db, invoiceIds, null, ct);
        var byContract = links.ToLookup(x => x.ContractId, x => x.InvoiceId);

        foreach (var c in contracts)
        {
            c.TotalCollected = byContract[c.Id].Sum(id => collected.GetValueOrDefault(id));
            foreach (var m in c.Milestones)
                if (m.Status == ContractMilestoneStatus.Invoiced && m.InvoiceId is Guid invoiceId
                    && balances.TryGetValue(invoiceId, out var b) && b.CreditAmount > 0 && b.AmountDue <= 0)
                    m.Status = ContractMilestoneStatus.Paid;
        }
    }

    protected override IQueryable<CommercialContract> ApplySearch(IQueryable<CommercialContract> q, string t)
        => q.Where(c => c.ContractNumber.Contains(t) || c.Title.Contains(t) || c.PartyName.Contains(t));

    /// <summary>دراسة الجدوى قبل «إنشاء العقد» ليست عقداً: لا تظهر في قوائم العقود (سجلها في <c>feasibility-studies</c>).</summary>
    protected override IQueryable<CommercialContract> ApplyFilters(IQueryable<CommercialContract> q, PaginationParams p)
    {
        q = q.Where(c => c.FeasibilityStatus == null || (c.FeasibilityStatus != FeasibilityStatuses.Study && c.FeasibilityStatus != FeasibilityStatuses.PlanApproved));
        return string.IsNullOrWhiteSpace(p.Status) ? q : q.Where(c => c.Stage == p.Status);
    }

    protected override async Task ValidateAsync(CreateCommercialContractDto d, CommercialContract? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(d.Title)) errors.Add(Messages.ContractTitleRequired);
        TradeHelper.RequireParty(d.PartyName, errors);
        if (d.ContractValue < 0) errors.Add(Messages.ContractValueCannotBeNegative);
        if (d.VatRate is < 0 or > 100) errors.Add(Messages.VatRateRange);
        if (d.StartDate.HasValue && d.EndDate.HasValue && d.EndDate < d.StartDate) errors.Add(Messages.EndDateBeforeStart);
        if (!string.IsNullOrWhiteSpace(d.PartyVatNumber) && !SaudiVat.IsValid(d.PartyVatNumber)) errors.Add(Messages.PartyVatNumberInvalid);
        if (d.Milestones.Sum(m => m.Percentage) > 100.0001m) errors.Add(Messages.MilestonePercentagesExceed100);
        if (d.Milestones.Any(m => m.Percentage < 0 || m.Amount < 0)) errors.Add(Messages.MilestoneAmountsCannotBeNegative);
        if (d.Milestones.Any(m => m.RetentionDeductionPercent is < 0 or > 100)) errors.Add(Messages.RetentionRateRange);
        if (d.RetentionPercent is < 0 or > 100) errors.Add(Messages.PerformanceBondRateRange);
        if (d.AdvancePercent is < 0 or > 100) errors.Add(Messages.AdvancePercentRange);
        if (d.MaxPenaltyPercent is < 0 or > 100) errors.Add(Messages.MaxPenaltyRange);
        if (d.LatePenaltyPerDay < 0) errors.Add(Messages.DailyPenaltyCannotBeNegative);
        if (d.DurationMonths is < 1) errors.Add(Messages.ContractMinimumOneMonth);
        if (d.Items.Any(i => i.Quantity <= 0 || i.UnitPrice < 0 || i.TenderUnitPrice < 0 || i.EstimatedUnitCost < 0 || i.VatRate is < 0 or > 100)) errors.Add(Messages.ContractItemsInvalid);
        if (d.OverheadPercent is < 0 or > 1000 || d.RiskPercent is < 0 or > 1000 || d.ProfitPercent is < 0 or > 1000) errors.Add(Messages.TenderMarkupRange);
        if (d.DiscountPercent is < 0 or >= 100) errors.Add(Messages.TenderDiscountRange);
        if (d.Items.Any(i => !ContractBoqLineKinds.IsValid(i.LineKind))) errors.Add(Messages.BoqLineKindInvalid);
        if (d.Items.Any(i => i.OverrunPercent is < 0 or > 100)) errors.Add(Messages.BoqOverrunRange);
        if (d.Workflow != null && !ContractWorkflows.IsValid(d.Workflow.Trim())) errors.Add(Messages.ContractWorkflowInvalid);
        var schedule = d.CertificateSchedule?.Trim();
        if (schedule != null && !ContractCertificateSchedules.IsValid(schedule)) errors.Add(Messages.CertificateScheduleInvalid);
        if (schedule == ContractCertificateSchedules.Installments && d.CertificateCount is not (>= 1 and <= ContractCertificateSchedules.MaxCount))
            errors.Add(string.Format(Messages.CertificateCountRange, ContractCertificateSchedules.MaxCount));
        if (schedule == ContractCertificateSchedules.Periodic)
        {
            if (d.CertificatePeriodMonths is not (>= 1 and <= ContractCertificateSchedules.MaxPeriodMonths))
                errors.Add(string.Format(Messages.CertificatePeriodMonthsRange, ContractCertificateSchedules.MaxPeriodMonths));
            if (!d.StartDate.HasValue) errors.Add(Messages.CertificateScheduleNeedsStartDate);
        }
        if (existing != null && existing.Stage is ContractStageDefinition.Closed or ContractStageDefinition.Cancelled) errors.Add(Messages.CannotEditClosedContract);
        await ValidateParentAsync(d, existing, errors, ct);
        // عقد بجدول كميات (مقاولات أو مقاول باطن): إضافة المقاولات مرخّصة (سعرها باتفاق، يمنحها مدير المنصة)
        if (await IsBoqContractAsync(Db, d.ContractType, ct) && !(await _entitlements.GetAsync(ct)).Allows(PlatformModules.Construction))
            errors.Add("عقود المقاولات (جدول الكميات) تتطلب إضافة المقاولات في اشتراكك. تواصل مع إدارة المنصة لتفعيلها.");
        // مركز التكلفة المختار من المراكز المعرّفة (بلا اختيار: يُنشأ للمشروع مركز تلقائي، ويرث عقد الباطن مركز مشروعه)
        if (d.CostCenterId is Guid centerId && d.ParentContractId == null
            && !await Db.Set<CostCenter>().AnyAsync(c => c.Id == centerId && c.IsActive, ct))
            errors.Add("مركز التكلفة المختار غير موجود أو غير نشط.");
        // المناقصة المقدَّمة أو الخاسرة لا تُعدَّل (يُعاد العطاء للتسعير أولاً فيبقى العرض المقدَّم كما سُجِّل)
        if (existing != null && ContractTenderStatuses.LocksEditing(existing.TenderStatus))
            errors.Add("العطاء مقدَّم أو خاسر: أعده للتسعير من شاشة المناقصات قبل التعديل.");
        if (existing == null && d.TenderStatus != null && d.TenderStatus != ContractTenderStatuses.New)
            errors.Add("المناقصة الجديدة تبدأ بحالة «جديدة».");
        if (existing == null && d.TenderStatus != null && (IsSupplier(d.PartyType) || d.ParentContractId != null))
            errors.Add("المناقصة لعقد عميل (لا لعقد مقاول باطن).");
        if (d.TenderIssueDate.HasValue && d.TenderDeadline.HasValue && d.TenderDeadline < d.TenderIssueDate)
            errors.Add("آخر موعد للتقديم قبل تاريخ طرح المناقصة.");
        // الدراسة بخطة معتمدة مجمّدة: تُفتح بمراجعة الخطة قبل تعديلها
        if (existing?.FeasibilityStatus == FeasibilityStatuses.PlanApproved) errors.Add(Messages.FeasibilityPlanApprovedLocked);
        // دراسة الجدوى: مشروع مقاولات لعميل (لا باطن ولا مناقصة)
        if (existing == null && d.IsFeasibilityStudy == true
            && (!await IsBoqContractAsync(Db, d.ContractType, ct) || IsSupplier(d.PartyType) || d.ParentContractId != null || d.TenderStatus != null))
            errors.Add(Messages.FeasibilityStudyBoqCustomerOnly);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        // مصدر السطر في مكتبة المقاولات (للتتبع): البند موجود حين يُرسل لأول مرة؛ السطر القديم يبقى بمصدره ولو حُذف من المكتبة
        var workItemIds = d.Items.Where(i => i.WorkItemId.HasValue).Select(i => i.WorkItemId!.Value).Distinct().ToList();
        if (workItemIds.Count == 0) return;
        var known = existing?.Items.Where(i => i.WorkItemId.HasValue).Select(i => i.WorkItemId!.Value).ToHashSet() ?? new HashSet<Guid>();
        var fresh = workItemIds.Where(id => !known.Contains(id)).ToList();
        if (fresh.Count > 0 && await Db.Set<WorkItem>().CountAsync(w => fresh.Contains(w.Id), ct) != fresh.Count)
            throw new ValidationFailedException(Messages.ContractWorkItemNotFound);
    }

    private static bool IsSupplier(string? partyType) => string.Equals(partyType, "supplier", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// عقد مقاول الباطن: الربط بعقد المشروع لعقد مورد وحده، والمشروع عقد عميل بجدول كميات (ليس عقد باطن آخر) غير ملغى ولا مغلق.
    /// بنود المشروع المرتبطة بأسطر الباطن من عقد المشروع نفسه وليست مجموعات؛ بلا مشروع لا ربط بنود.
    /// </summary>
    private async Task ValidateParentAsync(CreateCommercialContractDto d, CommercialContract? existing, List<string> errors, CancellationToken ct)
    {
        var mainItemIds = d.Items.Where(i => i.MainContractItemId.HasValue).Select(i => i.MainContractItemId!.Value).Distinct().ToList();
        if (d.ParentContractId is not Guid parentId)
        {
            if (mainItemIds.Count > 0) errors.Add("ربط أسطر الباطن ببنود المشروع يحتاج اختيار عقد المشروع.");
            return;
        }
        if (!IsSupplier(d.PartyType)) { errors.Add("الربط بعقد المشروع لعقد مقاول الباطن (عقد مورد) وحده."); return; }
        if (existing != null && existing.Id == parentId) { errors.Add("لا يرتبط العقد بنفسه."); return; }
        var parent = await Db.Set<CommercialContract>().AsNoTracking().Where(c => c.Id == parentId)
            .Select(c => new { c.PartyType, c.ParentContractId, c.ContractType, c.Stage }).FirstOrDefaultAsync(ct);
        if (parent == null) { errors.Add("عقد المشروع غير موجود."); return; }
        if (IsSupplier(parent.PartyType) || parent.ParentContractId != null || !await IsBoqContractAsync(Db, parent.ContractType, ct))
            errors.Add("عقد المشروع يجب أن يكون عقد عميل بجدول كميات.");
        if (parent.Stage is ContractStageDefinition.Closed or ContractStageDefinition.Cancelled)
            errors.Add("عقد المشروع مغلق أو ملغى.");
        if (mainItemIds.Count > 0)
        {
            var valid = await Db.Set<CommercialContractItem>().AsNoTracking()
                .CountAsync(i => i.ContractId == parentId && !i.IsGroup && mainItemIds.Contains(i.Id), ct);
            if (valid != mainItemIds.Count) errors.Add("بند المشروع المرتبط بسطر الباطن ليس من عقد المشروع أو أنه مجموعة.");
        }
    }

    /// <summary>عقد الباطن المرتبط بمشروع يأخذ مركز تكلفته (فتُحمَّل مستخلصاته على المشروع)؛ وبلا مشروع لا ربط بنود.</summary>
    private async Task ApplyParentAsync(CommercialContract e, CancellationToken ct)
    {
        if (e.ParentContractId is not Guid parentId)
        {
            foreach (var i in e.Items) i.MainContractItemId = null;
            return;
        }
        e.CostCenterId = await Db.Set<CommercialContract>().AsNoTracking().Where(c => c.Id == parentId).Select(c => c.CostCenterId).FirstOrDefaultAsync(ct);
    }

    protected override async Task OnCreatingAsync(CommercialContract e, CreateCommercialContractDto d, CancellationToken ct)
    {
        var study = d.IsFeasibilityStudy == true;
        if (study)
        {
            // رقم الدراسة وحده (FS-سنة-تسلسل)؛ رقم العقد يُعطى عند «إنشاء العقد»، وحتى ذلك يحمل الحقل رقم الدراسة (فريد)
            e.FeasibilityStatus = FeasibilityStatuses.Study;
            e.FeasibilityNumber = await _numbers.AssignAsync(new DocumentNumberRequest
            {
                Key = "feasibility_study", DefaultPrefix = $"FS-{(e.StartDate ?? DateTime.UtcNow).Year}-", WarehouseId = e.WarehouseId, Date = e.StartDate,
                Exists = (n, c) => Db.Set<CommercialContract>().AnyAsync(x => x.FeasibilityNumber == n || x.ContractNumber == n, c),
            }, ct);
            e.ContractNumber = e.FeasibilityNumber;
        }
        else
        {
            e.FeasibilityStatus = null; e.FeasibilityNumber = null;
            e.ContractNumber = await _numbers.AssignAsync(new DocumentNumberRequest
            {
                Key = "commercial_contract", DefaultPrefix = "CON-", WarehouseId = e.WarehouseId, Date = e.StartDate,
                ManualNumber = d.ContractNumber, UserEntered = true,
                Exists = (n, c) => Db.Set<CommercialContract>().AnyAsync(x => x.ContractNumber == n, c),
            }, ct);
        }
        var type = await ResolveTypeAsync(e, ct);
        e.ReviewRound = 1;
        e.TotalInvoiced = 0; e.TotalCollected = 0;
        foreach (var m in e.Milestones) ResetBillingFields(m);
        ApplyBoq(e, type);
        Recalculate(e);
        await ApplyParentAsync(e, ct);
        // المناقصة لعقد مقاولات وحده؛ تبدأ «جديدة» ونتيجتها من مسارها لا من الطلب
        if (e.TenderStatus != null && type.Billing != ContractBilling.BoqCertificate)
            throw new ValidationFailedException("المناقصة لعقود المقاولات (جدول الكميات) وحدها.");
        e.TenderDecidedAt = null; e.TenderResultNote = null;
        // عقد المقاولات للعميل هو المشروع: ملفه ومركز تكلفته يُنشآن معه. عقد مقاول الباطن (مورد) ليس مشروعاً: يتبع مشروعه.
        // والمناقصة لا مشروع لها حتى تُرسى (يُنشأ عند الترسية).
        // والدراسة لها ملف المشروع (ساعات العمل والميزانية للخطة) بلا مركز تكلفة حتى «إنشاء العقد».
        if (study) Db.Add(new ConstructionProjectProfile { ContractId = e.Id });
        else if (type.Billing == ContractBilling.BoqCertificate && !IsSupplier(e.PartyType) && e.TenderStatus == null)
            await ConstructionProjectService.CreateForContractAsync(Db, e, ct);

        // الحالة يحددها النظام لا المستخدم: كل عقد جديد «مسودة». المسار يختاره المستخدم لكل عقد (Workflow) ويُثبَّت هنا
        // ليحدد انتقالاته؛ بلا اختيار (عميل قديم/طلب اعتماد معلّق) يُشتق من النوع وحد التبسيط كما كان.
        // تفعيل المبسط انتقال عادي عبر move (صلاحية الاعتماد وحد يغطي القيمة).
        e.IsSimplified = SelectedSimplified(d.Workflow) ?? ContractTypeCodes.IsSimplified(type, e.ContractValue);
        // الضمان البنكي اختيار لكل عقد؛ بلا اختيار يأخذ افتراض النوع (يلزمه ضمان + مسار كامل)، والاختيار الصريح يغلبه دائماً
        ApplyGuarantee(e, d.RequiresGuarantee ?? ContractTypeCodes.GuaranteeDefault(type, e.IsSimplified));
        e.Stage = ContractStageDefinition.Draft;
        e.Status = StatusFor(e.Stage);
        e.StageChangedAt = DateTime.UtcNow;
        Db.Add(new ContractStatusLog
        {
            ContractId = e.Id, ToStatus = e.Stage, At = DateTime.UtcNow, UserId = _user.UserId, UserName = _user.Name ?? "system",
        });
    }

    /// <summary>
    /// نوع العقد من جدول الأنواع (الرموز القديمة تُحوَّل لنظائرها)؛ نوع غير معروف يُرفض، والمعطّل يُرفض لعقد جديد أو لتغيير النوع
    /// (تعطيل النوع يخفيه من العقود الجديدة ولا يمنع تعديل مسودة قائمة عليه).
    /// </summary>
    private async Task<ContractTypeDefinition> ResolveTypeAsync(CommercialContract e, CancellationToken ct, string? currentCode = null)
    {
        e.ContractType = ContractTypeCodes.Normalize(e.ContractType);
        var code = e.ContractType;
        var keepInactive = code == currentCode;
        return await Db.Set<ContractTypeDefinition>().AsNoTracking().FirstOrDefaultAsync(t => t.Code == code && (t.IsActive || keepInactive), ct)
            ?? throw new ValidationFailedException(string.Format(Messages.ContractTypeUnknown, code));
    }

    /// <summary>بلا ضمان: تُمسح بياناته فلا يبقى ضمان معلّق على عقد لا يلزمه.</summary>
    private static void ApplyGuarantee(CommercialContract e, bool required)
    {
        e.RequiresGuarantee = required;
        if (required) return;
        e.GuaranteeNumber = null; e.GuaranteeBank = null; e.GuaranteeAmount = null; e.GuaranteeExpiry = null;
    }

    /// <summary>مسار العقد الذي اختاره المستخدم: true = مبسط، false = كامل، null = لم يُرسل (التحقق يرفض القيم الأخرى).</summary>
    private static bool? SelectedSimplified(string? workflow) => workflow?.Trim() switch
    {
        ContractWorkflows.Simple => true,
        ContractWorkflows.Full => false,
        _ => null,
    };

    protected override async Task OnUpdatingAsync(CommercialContract e, UpdateCommercialContractDto d, CancellationToken ct)
    {
        // التعديل في المسودة فقط (يفحصه المتحكم مع الصلاحية)؛ تغيّر النوع أو القيمة لا يغيّر المسار المثبت عند الإنشاء
        var o = Db.Entry(e).OriginalValues;
        var type = await ResolveTypeAsync(e, ct, o.GetValue<string>(nameof(CommercialContract.ContractType)));
        e.ContractNumber = o.GetValue<string>(nameof(CommercialContract.ContractNumber));
        e.Stage = o.GetValue<string>(nameof(CommercialContract.Stage));
        e.WarehouseId ??= o.GetValue<Guid?>(nameof(CommercialContract.WarehouseId)); // بلا موقع في الطلب: يبقى موقعه
        e.CostCenterId ??= o.GetValue<Guid?>(nameof(CommercialContract.CostCenterId)); // بلا مركز في الطلب: يبقى مركزه (لا يضيع التلقائي)
        e.Status = o.GetValue<ContractStatus>(nameof(CommercialContract.Status));
        e.StageChangedAt = o.GetValue<DateTime?>(nameof(CommercialContract.StageChangedAt));
        e.ReviewRound = o.GetValue<int>(nameof(CommercialContract.ReviewRound));
        // حالة المناقصة ونتيجتها من مسار المناقصة وحده (IContractTenderService)
        e.TenderStatus = o.GetValue<string?>(nameof(CommercialContract.TenderStatus));
        e.TenderDecidedAt = o.GetValue<DateTime?>(nameof(CommercialContract.TenderDecidedAt));
        e.TenderResultNote = o.GetValue<string?>(nameof(CommercialContract.TenderResultNote));
        // المسار من العقد نفسه لا من نوعه. يتغير فقط في المسودة الأولى (قبل أي إرسال للمراجعة): بعدها تكون للعقد
        // جولات مراجعة وقرارات على المسار الكامل، وتحويله لمبسط يتخطاها. null = يبقى كما هو.
        e.IsSimplified = o.GetValue<bool>(nameof(CommercialContract.IsSimplified));
        if (SelectedSimplified(d.Workflow) is bool selected && selected != e.IsSimplified)
        {
            if (e.Stage != ContractStageDefinition.Draft || e.ReviewRound > 1
                || await Db.Set<ContractStatusLog>().AnyAsync(l => l.ContractId == e.Id && l.FromStatus != null, ct))
                throw new ValidationFailedException(Messages.ContractWorkflowLocked);
            e.IsSimplified = selected;
        }
        // الضمان يُفعَّل أو يُلغى في المسودة (التعديل في المسودة فقط)؛ null = يبقى كما هو
        ApplyGuarantee(e, d.RequiresGuarantee ?? o.GetValue<bool>(nameof(CommercialContract.RequiresGuarantee)));
        e.TotalInvoiced = o.GetValue<decimal>(nameof(CommercialContract.TotalInvoiced));
        e.TotalCollected = o.GetValue<decimal>(nameof(CommercialContract.TotalCollected));
        // جدولة المستخلصات لم تُرسل (عميل قديم): تبقى كما هي
        if (d.CertificateSchedule == null)
        {
            e.CertificateSchedule = o.GetValue<string>(nameof(CommercialContract.CertificateSchedule));
            e.CertificateCount = o.GetValue<int?>(nameof(CommercialContract.CertificateCount));
            e.CertificatePeriodMonths = o.GetValue<int?>(nameof(CommercialContract.CertificatePeriodMonths));
        }

        // المستخلصات المفوترة تُقفل: لا تعديل ولا حذف. وغير المفوترة لا تأخذ حالة الفوترة ولا فاتورتها من الطلب.
        foreach (var entry in Db.ChangeTracker.Entries<ContractMilestone>())
        {
            var wasBilled = entry.State != EntityState.Added
                && entry.OriginalValues.GetValue<ContractMilestoneStatus>(nameof(ContractMilestone.Status)) is ContractMilestoneStatus.Invoiced or ContractMilestoneStatus.Paid;
            if (!wasBilled)
            {
                if (entry.State is EntityState.Added or EntityState.Modified) ResetBillingFields(entry.Entity);
                continue;
            }
            if (entry.State == EntityState.Deleted) throw new ConflictException(Messages.CannotDeleteBilledMilestone);
            if (entry.State == EntityState.Modified) entry.CurrentValues.SetValues(entry.OriginalValues);
        }
        ApplyBoq(e, type);
        if (type.Billing == ContractBilling.BoqCertificate)
        {
            await ApplyCostAnalysisAsync(e, ct);
            RecomputeEstimate(e);
        }
        Recalculate(e);
        await ApplyParentAsync(e, ct);
        // قيمة عقد الدراسة تغيّرت عمّا اعتُمد: تُسقط الموافقة المالية الآلية فتلزم المراجعة المالية العادية
        await FeasibilityFinancialApproval.InvalidateOnValueChangeAsync(Db, e, _user, ct);
    }

    protected override async Task OnDeletingAsync(CommercialContract e, CancellationToken ct)
    {
        if (e.TotalInvoiced > 0 || await Db.Set<ContractMilestone>().AnyAsync(m => m.ContractId == e.Id && m.InvoiceId != null, ct))
            throw new ConflictException(Messages.CannotDeleteContractWithBilledMilestones);
        if (await Db.Set<CommercialContract>().AnyAsync(c => c.ParentContractId == e.Id, ct))
            throw new ConflictException("للمشروع عقود مقاولي باطن مرتبطة به: لا يُحذف العقد.");
        if (await Db.Set<DeliveryNote>().AnyAsync(n => n.ContractId == e.Id, ct))
            throw new ConflictException(Messages.CannotDeleteContractWithDeliveryNotes);
        if (await Db.Set<ProjectMaterialIssue>().AnyAsync(i => i.ContractId == e.Id, ct)
            || await Db.Set<MaterialRequisition>().AnyAsync(r => r.ContractId == e.Id, ct))
            throw new ConflictException("للمشروع طلبات شراء أو مستندات صرف مواد: لا يُحذف العقد.");
        // الحذف للمسودة وحدها؛ بعدها للعقد أثر (مراجعات معتمدة، مستخلصات، فواتير) فيُلغى ولا يُحذف
        if (e.Stage != ContractStageDefinition.Draft) throw new ConflictException(Messages.ContractDeleteDraftOnly);
        if (await Db.Set<ContractCertificate>().AnyAsync(c => c.ContractId == e.Id, ct) || await Db.Set<ContractInvoice>().AnyAsync(i => i.ContractId == e.Id, ct))
            throw new ConflictException(Messages.CannotDeleteContractWithBilledMilestones);

        // سجلات المسودة التابعة لها (سجل الحالات، قرارات المراجعة المُعادة، المرفقات، خطة الاحتياجات) تُحذف معها في المعاملة نفسها.
        // قيودها تبقى «منع الحذف» في قاعدة البيانات، فلا يُحذف عقد بعد المسودة بأثرها مهما كان المسار.
        Db.RemoveRange(await Db.Set<ContractStatusLog>().Where(l => l.ContractId == e.Id).ToListAsync(ct));
        Db.RemoveRange(await Db.Set<ContractTenderRevision>().Where(r => r.ContractId == e.Id).ToListAsync(ct));
        Db.RemoveRange(await Db.Set<ContractApproval>().Where(a => a.ContractId == e.Id).ToListAsync(ct));
        Db.RemoveRange(await Db.Set<ContractAttachment>().Where(a => a.ContractId == e.Id).ToListAsync(ct));
        Db.RemoveRange(await Db.Set<ProjectMaterialRequirement>().Where(r => r.ContractId == e.Id).ToListAsync(ct));
        Db.RemoveRange(await Db.Set<ProjectMaterialPlan>().Where(p => p.ContractId == e.Id).ToListAsync(ct));
        Db.RemoveRange(await Db.Set<ContractItemResourceCost>().Where(r => r.ContractId == e.Id).ToListAsync(ct));
        // إعداد المشروع (بنود المراحل تُحذف مع مراحلها)، ومركز التكلفة الذي أُنشئ للمشروع تلقائياً إن لم يُستخدم في قيد أو عقد آخر
        Db.RemoveRange(await Db.Set<ProjectMember>().Where(m => m.ContractId == e.Id).ToListAsync(ct));
        Db.RemoveRange(await Db.Set<ProjectPhase>().Where(p => p.ContractId == e.Id).ToListAsync(ct));
        Db.RemoveRange(await Db.Set<ConstructionProjectProfile>().Where(p => p.ContractId == e.Id).ToListAsync(ct));
        // التقارير اليومية (أسطرها معها) ثم الموارد التي تشير إليها، وخطة الإنجاز
        // الاستخدامات تُحمَّل لتُحذف قبل مواردها (قيدها «منع الحذف»)
        await SiteReportAttachmentService.RemoveForReportsAsync(Db, Db.Set<SiteDailyReport>().Where(r => r.ContractId == e.Id).Select(r => r.Id), ct);
        Db.RemoveRange(await Db.Set<SiteDailyReport>().Include(r => r.Resources).Where(r => r.ContractId == e.Id).ToListAsync(ct));
        Db.RemoveRange(await Db.Set<ProjectResource>().Where(r => r.ContractId == e.Id).ToListAsync(ct));
        Db.RemoveRange(await Db.Set<ProjectDailyPlan>().Where(p => p.ContractId == e.Id).ToListAsync(ct));
        if (e.CostCenterId is Guid centerId)
        {
            var auto = ConstructionProjectService.CostCenterPrefix + e.ContractNumber;
            var center = await Db.Set<CostCenter>().FirstOrDefaultAsync(c => c.Id == centerId && (c.Code == auto || c.Code.StartsWith(auto + "-")), ct);
            if (center != null
                && !await Db.Set<JournalEntryLine>().AnyAsync(l => l.CostCenterId == centerId, ct)
                && !await Db.Set<CostCenter>().AnyAsync(c => c.ParentId == centerId, ct)
                && !await Db.Set<CommercialContract>().AnyAsync(c => c.CostCenterId == centerId && c.Id != e.Id, ct))
                Db.Remove(center);
        }
    }

    /// <summary>
    /// بنود العقد كجدول كميات: الكمية المعتمدة = الكمية (حتى تُعتمد ملاحق)، والترتيب يُكمل تلقائياً. وفي عقد بجدول كميات
    /// قيمة العقد يحسبها الخادم من البنود (الكمية × السعر)، والاستحقاق بالمستخلصات فلا جدول دفعات.
    /// تسعير العطاء: المستخدم يُدخل سعر العطاء لكل سطر، وخصم التفاوض يُطبَّق بالنسبة نفسها على كل سعر، فسعر الوحدة
    /// المتعاقد عليه = سعر العطاء × (1 − الخصم)، وقيمة العقد مجموع الأسطر بعده فتبقى مطابقة لجدول الكميات.
    /// </summary>
    private static void ApplyBoq(CommercialContract c, ContractTypeDefinition type)
    {
        var boq = type.Billing == ContractBilling.BoqCertificate;
        // الخصم ونسب العطاء لجدول الكميات وحده؛ غيره يُدخل قيمته وأسعاره مباشرة
        if (!boq) { c.DiscountPercent = 0; c.OverheadPercent = 0; c.RiskPercent = 0; c.ProfitPercent = 0; }
        var factor = 1 - c.DiscountPercent / 100m;
        var order = 0;
        var ordered = c.Items.OrderBy(i => i.SortOrder == 0 ? int.MaxValue : i.SortOrder).ToList();
        foreach (var i in ordered)
        {
            i.SortOrder = ++order;
            i.LineCode = string.IsNullOrWhiteSpace(i.LineCode) ? null : i.LineCode.Trim();
            i.SectionName = string.IsNullOrWhiteSpace(i.SectionName) ? null : i.SectionName.Trim();
            i.UnitDescription = string.IsNullOrWhiteSpace(i.UnitDescription) ? null : i.UnitDescription.Trim();
            i.DescriptionEn = string.IsNullOrWhiteSpace(i.DescriptionEn) ? null : i.DescriptionEn.Trim();
            i.SectionNameEn = string.IsNullOrWhiteSpace(i.SectionNameEn) ? null : i.SectionNameEn.Trim();
            i.RevisedQuantity = i.Quantity; // التعديل في المسودة وحدها، قبل أي أمر تغيير
            i.VariationId = null; // يضبطه اعتماد أمر التغيير وحده
            // بلا سعر عطاء (عميل قديم أو عقد قبل الحقل): السعر المرسل هو سعر العطاء
            if (i.TenderUnitPrice <= 0) i.TenderUnitPrice = i.UnitPrice;
            if (boq) i.UnitPrice = Math.Round(i.TenderUnitPrice * factor, 4);
            else i.TenderUnitPrice = i.UnitPrice;
        }
        LinkSubItems(ordered, boq);
        // الجدولة لعقود المقاولات وحدها، ويُحفظ من إعداداتها ما يخص النمط المختار فقط
        var schedule = boq ? c.CertificateSchedule?.Trim() : null;
        c.CertificateSchedule = ContractCertificateSchedules.IsValid(schedule) ? schedule! : ContractCertificateSchedules.OnDemand;
        if (c.CertificateSchedule != ContractCertificateSchedules.Installments) c.CertificateCount = null;
        if (c.CertificateSchedule != ContractCertificateSchedules.Periodic) c.CertificatePeriodMonths = null;
        c.EstimatedCost = 0; // يُحسب لعقود جدول الكميات وحدها، لا يُقبل من الطلب
        c.TenderValue = c.ContractValue; // غير جدول الكميات: قيمته المُدخلة، بلا خصم
        if (!boq) return;

        if (c.Milestones.Count > 0) throw new ValidationFailedException(Messages.BoqContractMilestonesNotAllowed);
        // السطر الرئيسي ذو الفروع لا يُحتسب: قيمة العقد من الأسطر المسعّرة فعلاً (العادية والفرعية) بعد الخصم
        c.ContractValue = DocumentPricing.Round(c.Items.Where(i => !i.IsGroup).Sum(i => i.Quantity * i.UnitPrice));
        // قيمة العطاء قبل الخصم من الأسطر نفسها؛ الفرق عن قيمة العقد هو مبلغ خصم التفاوض
        c.TenderValue = DocumentPricing.Round(c.Items.Where(i => !i.IsGroup).Sum(i => i.Quantity * i.TenderUnitPrice));
        RecomputeEstimate(c);
    }

    /// <summary>
    /// دراسة الجدوى: تكلفة وحدة المجموعة من فروعها، والتكلفة التقديرية للعقد من الأسطر المسعّرة نفسها (المجموعة لا تُكرر).
    /// يستعملها حفظ العقد وتحليل التكلفة (<see cref="ContractCostAnalysisService"/>).
    /// </summary>
    internal static void RecomputeEstimate(CommercialContract c)
    {
        foreach (var group in c.Items.Where(i => i.IsGroup))
        {
            var cost = c.Items.Where(x => x.ParentItemId == group.Id).Sum(x => DocumentPricing.Round(x.Quantity * x.EstimatedUnitCost));
            group.EstimatedUnitCost = group.Quantity > 0 ? Math.Round(cost / group.Quantity, 4) : 0;
        }
        c.EstimatedCost = DocumentPricing.Round(c.Items.Where(i => !i.IsGroup).Sum(i => i.Quantity * Math.Max(0, i.EstimatedUnitCost)));
    }

    /// <summary>
    /// السطر المسعّر من المكتبة تكلفته من تحليله المجمّد لا من الطلب؛ وتحليل سطر حُذف أو تغيّر ربطه بالمكتبة يسقط فيعود يدوياً.
    /// </summary>
    private async Task ApplyCostAnalysisAsync(CommercialContract c, CancellationToken ct)
    {
        var rows = await Db.Set<ContractItemResourceCost>().Where(r => r.ContractId == c.Id).ToListAsync(ct);
        if (rows.Count == 0) return;
        var lines = c.Items.ToDictionary(i => i.Id);
        foreach (var group in rows.GroupBy(r => r.ContractItemId))
        {
            var first = group.First();
            if (!lines.TryGetValue(group.Key, out var line) || line.IsGroup || line.WorkSubItemId != first.WorkSubItemId
                || LineWorkItem(line, lines) != first.WorkItemId)
            {
                Db.RemoveRange(group);
                continue;
            }
            line.EstimatedUnitCost = ContractCostAnalysisService.LineUnitCost(group.ToList(), line.Quantity);
        }
    }

    private static Guid? LineWorkItem(CommercialContractItem line, Dictionary<Guid, CommercialContractItem> lines)
        => line.IsSubItem && line.ParentItemId is Guid pid ? lines.GetValueOrDefault(pid)?.WorkItemId : line.WorkItemId;

    /// <summary>
    /// البنود الفرعية في جدول الكميات: كل سطر فرعي يتبع أقرب سطر رئيسي قبله بالترتيب، والرئيسي الذي له فروع «مجموعة»
    /// سعر وحدته للعرض = مجموع فروعه ÷ كميته. غير عقود المقاولات لا فروع لها.
    /// </summary>
    private static void LinkSubItems(List<CommercialContractItem> ordered, bool boq)
    {
        CommercialContractItem? parent = null;
        foreach (var i in ordered)
        {
            if (i.Id == Guid.Empty) i.Id = Guid.NewGuid(); // معرّف الأب قبل الحفظ لربط فروعه
            i.IsGroup = false;
            if (!boq) i.IsSubItem = false;
            if (!i.IsSubItem)
            {
                i.ParentItemId = null;
                parent = i;
                continue;
            }
            if (parent == null) throw new ValidationFailedException(Messages.BoqSubItemWithoutParent);
            i.ParentItemId = parent.Id;
            parent.IsGroup = true;
        }
        foreach (var group in ordered.Where(i => i.IsGroup))
        {
            var sum = ordered.Where(x => x.ParentItemId == group.Id).Sum(x => DocumentPricing.Round(x.Quantity * x.UnitPrice));
            group.UnitPrice = group.Quantity > 0 ? Math.Round(sum / group.Quantity, 4) : 0;
            var tender = ordered.Where(x => x.ParentItemId == group.Id).Sum(x => DocumentPricing.Round(x.Quantity * x.TenderUnitPrice));
            group.TenderUnitPrice = group.Quantity > 0 ? Math.Round(tender / group.Quantity, 4) : 0;
        }
    }

    /// <summary>
    /// حالة الفوترة وفاتورتها لا تأتي من الطلب: تضبطها الفوترة وحدها. يُقبل «مستحق» كتنبيه، وأي حالة أخرى تصبح «قيد الانتظار».
    /// </summary>
    private static void ResetBillingFields(ContractMilestone m)
    {
        if (m.Status != ContractMilestoneStatus.Due) m.Status = ContractMilestoneStatus.Pending;
        m.InvoiceId = null; m.InvoiceNumber = null; m.InvoicedAt = null;
    }

    /// <summary>
    /// النسبة هي الأصل متى وُجدت (المبلغ = قيمة العقد × النسبة، فيتبع تغيّر القيمة)، والنسبة صفر تعني دفعة بمبلغ ثابت كما أُدخل.
    /// مجموع مبالغ الدفعات لا يتجاوز قيمة العقد (مع سماح هللة لكل دفعة لفروق التقريب).
    /// </summary>
    private static void Recalculate(CommercialContract c)
    {
        c.VatAmount = DocumentPricing.Round(c.ContractValue * c.VatRate / 100m);
        c.TotalValueWithVat = c.ContractValue + c.VatAmount;
        c.RetentionAmount = DocumentPricing.Round(c.ContractValue * c.RetentionPercent / 100m);
        foreach (var i in c.Items.Where(i => !i.IsGroup))
            i.TotalWithVat = DocumentPricing.Round(i.Quantity * i.UnitPrice * (1 + i.VatRate / 100m));
        // إجمالي السطر الرئيسي = مجموع فروعه بالضبط (لا كمية × سعر مقرّب)
        foreach (var g in c.Items.Where(i => i.IsGroup))
            g.TotalWithVat = c.Items.Where(x => x.ParentItemId == g.Id).Sum(x => x.TotalWithVat);
        var n = 1;
        foreach (var m in c.Milestones.OrderBy(m => m.MilestoneNumber == 0 ? int.MaxValue : m.MilestoneNumber))
        {
            if (m.MilestoneNumber == 0) m.MilestoneNumber = n;
            n = Math.Max(n, m.MilestoneNumber) + 1;
            if (m.Status is ContractMilestoneStatus.Invoiced or ContractMilestoneStatus.Paid) continue; // لا تُعاد حسابات المفوتر
            if (m.Percentage > 0) m.Amount = DocumentPricing.Round(c.ContractValue * m.Percentage / 100m);
            m.VatAmount = DocumentPricing.Round(m.Amount * c.VatRate / 100m);
            m.TotalWithVat = m.Amount + m.VatAmount;
            m.RetentionDeductionAmount = DocumentPricing.Round(m.Amount * m.RetentionDeductionPercent / 100m);
            m.NetPayableAmount = m.TotalWithVat - m.RetentionDeductionAmount;
        }
        if (c.Milestones.Sum(m => m.Amount) > c.ContractValue + 0.01m * c.Milestones.Count)
            throw new ValidationFailedException(Messages.MilestoneAmountsExceedContractValue);
        c.RemainingBalance = c.TotalValueWithVat - c.TotalInvoiced;
    }

    /// <summary>
    /// فاتورة المستخلص عبر محرك الفوترة المركزي (بند خدمة بلا مخزون) وربطها بالمستخلص والعقد:
    /// عقد العميل فاتورة مبيعات ضريبية، وعقد المورد فاتورة مشتريات داخلية (قيد ذمم المورد فقط، لا إرسال للهيئة)
    /// بعد التحقق من بطاقة المورد.
    /// </summary>
    public async Task<InvoiceDto> BillMilestoneAsync(Guid contractId, Guid milestoneId, DateTime? date = null, CancellationToken ct = default)
        => await new TransactionRunner(Db).RunAsync(async token =>
        {
            var c = await Db.Set<CommercialContract>().Include(x => x.Milestones).FirstOrDefaultAsync(x => x.Id == contractId, token)
                ?? throw new NotFoundException(Messages.ContractNotFound);
            var m = c.Milestones.FirstOrDefault(x => x.Id == milestoneId) ?? throw new NotFoundException(Messages.MilestoneNotFound);

            if (m.Status is ContractMilestoneStatus.Invoiced or ContractMilestoneStatus.Paid) throw new ConflictException(Messages.MilestoneAlreadyBilled);
            if (await IsBoqContractAsync(Db, c.ContractType, token)) throw new ConflictException(Messages.BoqContractBilledByCertificates);
            if (c.Stage is not (ContractStageDefinition.Active or ContractStageDefinition.InitialAcceptance))
                throw new ConflictException(Messages.MilestonesBilledOnlyWhenActive);
            var isSupplier = string.Equals(c.PartyType, "supplier", StringComparison.OrdinalIgnoreCase);
            if (c.PartyId == null)
                throw new ValidationFailedException(isSupplier ? Messages.LinkContractToSupplierBeforeBilling : Messages.LinkContractToCustomerBeforeBilling);
            if (m.Amount <= 0) throw new ValidationFailedException(Messages.MilestoneAmountMustBePositive);

            var request = new CreateInvoiceDto
            {
                SystemGenerated = true, // مستند ينشئه النظام: ترقيمه تلقائي دائماً
                Kind = InvoiceKind.Sales, InvoiceType = TradeHelper.InvoiceTypeFor(c.PartyVatNumber),
                IssueDate = date?.Date ?? default,
                PartyId = c.PartyId, PartyName = c.PartyName, PartyVatNumber = c.PartyVatNumber, PartyPhone = c.PartyPhone, PartyEmail = c.PartyEmail,
                PaymentMethod = PaymentMethod.Credit, Status = "posted",
                Notes = $"مستخلص رقم {m.MilestoneNumber} - عقد {c.ContractNumber}",
                ReferenceType = "commercial_contract", ReferenceId = c.Id, ReferenceNumber = c.ContractNumber,
                CostCenterId = c.CostCenterId, CostCenterFromSource = true, // مركز العقد على إيراد/مصروف المستخلص وقيده
                Items = new List<InvoiceItemDto>
                {
                    new() { ItemId = Guid.Empty, ItemName = m.Title, Unit = "خدمة", Quantity = 1, UnitPrice = m.Amount, VatRate = c.VatRate },
                },
            };

            InvoiceDto invoice;
            if (isSupplier)
            {
                // بيانات الطرف من بطاقة المورد (مصدر الحقيقة في الذمم) بعد التحقق منها
                var s = await RequireBillableSupplierAsync(Db, c.PartyId.Value, token);
                request.Kind = InvoiceKind.Purchase;
                request.InvoiceType = TradeHelper.InvoiceTypeFor(s.VatNumber);
                request.PartyName = s.NameAr; request.PartyVatNumber = s.VatNumber; request.PartyPhone = s.Phone; request.PartyEmail = s.Email;
                request.SendToZatca = false; // فاتورة مشتريات: قيد داخلي في النظام المحاسبي فقط
                invoice = await _purchases.CreateAsync(request, token);
            }
            else
            {
                request.RevenueAccountCode = string.IsNullOrWhiteSpace(c.RevenueAccountCode) ? null : c.RevenueAccountCode;
                invoice = await _invoices.CreateAsync(request, token);
            }

            m.Status = ContractMilestoneStatus.Invoiced;
            m.InvoicedAt = DateTime.UtcNow; m.InvoiceId = invoice.Id; m.InvoiceNumber = invoice.InvoiceNumber;
            Db.Add(ContractInvoiceFor(c.Id, "progress", invoice));
            c.TotalInvoiced += invoice.GrandTotal;
            c.RemainingBalance = c.TotalValueWithVat - c.TotalInvoiced;
            // الفوترة مستند داخل «ساري» وليست مرحلة: لا تغيّر حالة العقد
            await Db.SaveChangesAsync(token);
            return invoice;
        }, ct);

    /// <summary>عقد بجدول كميات: يُستحق بالمستخلصات وحدها، فلا تُفوتر له دفعات ولا بيانات تسليم.</summary>
    internal static Task<bool> IsBoqContractAsync(ErpDbContext db, string contractType, CancellationToken ct)
        => db.Set<ContractTypeDefinition>().AsNoTracking().AnyAsync(t => t.Code == contractType && t.Billing == ContractBilling.BoqCertificate, ct);

    /// <summary>
    /// سطر سجل فواتير العقد لفاتورة صدرت من مستند العقد (دفعة أو بيان تسليم). استقطاع الدفعة المقدمة والضمان صفر
    /// حتى تُخصم فعلاً في الفاتورة (المرحلة 3)، فلا يُطلب الإفراج عن ضمان لم يُحتجز.
    /// </summary>
    internal static ContractInvoice ContractInvoiceFor(Guid contractId, string kind, InvoiceDto invoice, Guid? deliveryNoteId = null) => new()
    {
        ContractId = contractId, Kind = kind, InvoiceId = invoice.Id, InvoiceNumber = invoice.InvoiceNumber, DeliveryNoteId = deliveryNoteId,
        GrossAmount = invoice.GrandTotal - invoice.VatTotal, VatAmount = invoice.VatTotal, TotalAmount = invoice.GrandTotal,
        IssuedAt = invoice.IssueDate,
    };

    /// <summary>المورد موجود ونشط، وله حساب ذمم في شجرة الحسابات، ورقمه الضريبي (إن وُجد) صالح.</summary>
    internal static async Task<Supplier> RequireBillableSupplierAsync(ErpDbContext db, Guid supplierId, CancellationToken ct)
    {
        var s = await db.Set<Supplier>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == supplierId, ct)
            ?? throw new ValidationFailedException(Messages.SupplierNotFound);
        var errors = new List<string>();
        if (s.Status != "active") errors.Add(Messages.SupplierInactive);
        if (string.IsNullOrWhiteSpace(s.AccountCode)) errors.Add(Messages.SupplierAccountMissing);
        if (!string.IsNullOrWhiteSpace(s.VatNumber) && !SaudiVat.IsValid(s.VatNumber)) errors.Add(Messages.SupplierVatNumberInvalid);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
        return s;
    }
}
