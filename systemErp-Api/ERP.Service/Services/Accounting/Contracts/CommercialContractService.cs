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

    public CommercialContractService(ErpDbContext db, INumberSequenceService numbers, ISalesInvoiceService invoices, IPurchaseInvoiceService purchases, ICurrentUser user) : base(db)
    {
        _numbers = numbers; _invoices = invoices; _purchases = purchases; _user = user;
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

    protected override IQueryable<CommercialContract> ApplyFilters(IQueryable<CommercialContract> q, PaginationParams p)
        => string.IsNullOrWhiteSpace(p.Status) ? q : q.Where(c => c.Stage == p.Status);

    protected override Task ValidateAsync(CreateCommercialContractDto d, CommercialContract? existing, CancellationToken ct)
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
        if (d.Items.Any(i => i.Quantity <= 0 || i.UnitPrice < 0 || i.VatRate is < 0 or > 100)) errors.Add(Messages.ContractItemsInvalid);
        if (d.Items.Any(i => !ContractBoqLineKinds.IsValid(i.LineKind))) errors.Add(Messages.BoqLineKindInvalid);
        if (d.Items.Any(i => i.OverrunPercent is < 0 or > 100)) errors.Add(Messages.BoqOverrunRange);
        if (existing != null && existing.Stage is ContractStageDefinition.Closed or ContractStageDefinition.Cancelled) errors.Add(Messages.CannotEditClosedContract);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
        return Task.CompletedTask;
    }

    protected override async Task OnCreatingAsync(CommercialContract e, CreateCommercialContractDto d, CancellationToken ct)
    {
        e.ContractNumber = await _numbers.AssignAsync(new DocumentNumberRequest
        {
            Key = "commercial_contract", DefaultPrefix = "CON-", WarehouseId = e.WarehouseId, Date = e.StartDate,
            ManualNumber = d.ContractNumber, UserEntered = true,
            Exists = (n, c) => Db.Set<CommercialContract>().AnyAsync(x => x.ContractNumber == n, c),
        }, ct);
        var type = await ResolveTypeAsync(e, ct);
        e.ReviewRound = 1;
        e.TotalInvoiced = 0; e.TotalCollected = 0;
        foreach (var m in e.Milestones) ResetBillingFields(m);
        ApplyBoq(e, type);
        Recalculate(e);

        // الحالة يحددها النظام لا المستخدم: كل عقد جديد «مسودة»، والمسار (المبسط من النوع أو لأن القيمة دون حد التبسيط)
        // يُثبَّت هنا ليحدد انتقالاته؛ تفعيل المبسط انتقال عادي عبر move (صلاحية الاعتماد وحد يغطي القيمة).
        e.IsSimplified = ContractTypeCodes.IsSimplified(type, e.ContractValue);
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

    protected override async Task OnUpdatingAsync(CommercialContract e, UpdateCommercialContractDto d, CancellationToken ct)
    {
        // التعديل في المسودة فقط (يفحصه المتحكم مع الصلاحية)؛ تغيّر النوع أو القيمة لا يغيّر المسار المثبت عند الإنشاء
        var o = Db.Entry(e).OriginalValues;
        var type = await ResolveTypeAsync(e, ct, o.GetValue<string>(nameof(CommercialContract.ContractType)));
        e.ContractNumber = o.GetValue<string>(nameof(CommercialContract.ContractNumber));
        e.Stage = o.GetValue<string>(nameof(CommercialContract.Stage));
        e.Status = o.GetValue<ContractStatus>(nameof(CommercialContract.Status));
        e.StageChangedAt = o.GetValue<DateTime?>(nameof(CommercialContract.StageChangedAt));
        e.ReviewRound = o.GetValue<int>(nameof(CommercialContract.ReviewRound));
        e.IsSimplified = o.GetValue<bool>(nameof(CommercialContract.IsSimplified));
        e.TotalInvoiced = o.GetValue<decimal>(nameof(CommercialContract.TotalInvoiced));
        e.TotalCollected = o.GetValue<decimal>(nameof(CommercialContract.TotalCollected));

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
        Recalculate(e);
    }

    protected override async Task OnDeletingAsync(CommercialContract e, CancellationToken ct)
    {
        if (e.TotalInvoiced > 0 || await Db.Set<ContractMilestone>().AnyAsync(m => m.ContractId == e.Id && m.InvoiceId != null, ct))
            throw new ConflictException(Messages.CannotDeleteContractWithBilledMilestones);
        if (await Db.Set<DeliveryNote>().AnyAsync(n => n.ContractId == e.Id, ct))
            throw new ConflictException(Messages.CannotDeleteContractWithDeliveryNotes);
    }

    /// <summary>
    /// بنود العقد كجدول كميات: الكمية المعتمدة = الكمية (حتى تُعتمد ملاحق)، والترتيب يُكمل تلقائياً. وفي عقد بجدول كميات
    /// قيمة العقد يحسبها الخادم من البنود (الكمية × السعر)، والاستحقاق بالمستخلصات فلا جدول دفعات.
    /// </summary>
    private static void ApplyBoq(CommercialContract c, ContractTypeDefinition type)
    {
        var order = 0;
        foreach (var i in c.Items.OrderBy(i => i.SortOrder == 0 ? int.MaxValue : i.SortOrder).ToList())
        {
            i.SortOrder = ++order;
            i.LineCode = string.IsNullOrWhiteSpace(i.LineCode) ? null : i.LineCode.Trim();
            i.SectionName = string.IsNullOrWhiteSpace(i.SectionName) ? null : i.SectionName.Trim();
            i.RevisedQuantity = i.Quantity;
        }
        if (type.Billing != ContractBilling.BoqCertificate) return;

        if (c.Milestones.Count > 0) throw new ValidationFailedException(Messages.BoqContractMilestonesNotAllowed);
        c.ContractValue = DocumentPricing.Round(c.Items.Sum(i => i.Quantity * i.UnitPrice));
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
        foreach (var i in c.Items)
            i.TotalWithVat = DocumentPricing.Round(i.Quantity * i.UnitPrice * (1 + i.VatRate / 100m));
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
