using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using St = ERP.Core.Models.Accounting.ContractCertificateStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// مستخلصات عقود جدول الكميات. القاعدة: الكمية السابقة لكل بند = مجموع الكميات الحالية في المستخلصات المعتمدة/المفوترة،
/// تُعاد قراءتها من القاعدة عند كل حفظ وتقديم واعتماد (لا تُقبل من الطلب)؛ التراكمي لا يتجاوز الكمية المعتمدة × (1 + السماح)؛
/// مستخلص مفتوح واحد لكل عقد (وفهرس فريد مفلتر يمنع السباق)؛ لا كميات حالية سالبة (التصحيح بإشعار دائن لاحقاً).
/// الصلاحيات (الاستلام للإعداد، الاعتماد للاعتماد والإعادة) ونطاق السجل يفحصها المتحكم على سجل العقد.
/// مواد المستخلص: كميات من أذونات تسليم مرحّلة على بنود يقيسها المستخلص، بتكلفتها الفعلية؛ المتاح يُعاد حسابه من القاعدة عند كل
/// حفظ وتقديم واعتماد (وعند الفوترة)، فلا تُختار الكمية نفسها في مستخلصين غير ملغيين.
/// </summary>
public class ContractCertificateService : IContractCertificateService
{
    private readonly ErpDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly ICurrentUser _user;

    public ContractCertificateService(ErpDbContext db, INumberSequenceService numbers, ICurrentUser user)
    {
        _db = db; _numbers = numbers; _user = user;
    }

    // ---------- القراءة ----------

    public async Task<List<ContractCertificateDto>> ListAsync(Guid contractId, CancellationToken ct = default)
    {
        await LoadContractAsync(contractId, track: false, ct);
        var list = await _db.Set<ContractCertificate>().AsNoTracking().Include(c => c.Lines).Include(c => c.Deductions).Include(c => c.Materials).AsSplitQuery()
            .Where(c => c.ContractId == contractId).OrderByDescending(c => c.SequenceNo).ToListAsync(ct);
        var dtos = list.Select(ToDto).ToList();
        await ApplyCollectionsAsync(dtos, ct);
        await ApplySiteAsync(contractId, dtos, ct);
        return dtos;
    }

    public async Task<ContractCertificateDto> GetAsync(Guid contractId, Guid certificateId, CancellationToken ct = default)
    {
        var dto = ToDto(await LoadCertificateAsync(contractId, certificateId, track: false, ct));
        await ApplyCollectionsAsync(new List<ContractCertificateDto> { dto }, ct);
        await ApplySiteAsync(contractId, new List<ContractCertificateDto> { dto }, ct);
        return dto;
    }

    /// <summary>المحصّل والمتبقي على فواتير المستخلصات المفوترة (السندات الموزّعة؛ المتبقي من InvoiceBalances = الصافي المستحق − التحصيل).</summary>
    internal async Task ApplyCollectionsAsync(List<ContractCertificateDto> certificates, CancellationToken ct)
    {
        var ids = certificates.Where(c => c.InvoiceId != null).Select(c => c.InvoiceId!.Value).Distinct().ToList();
        if (ids.Count == 0) return;
        var collected = await _db.Set<VoucherAllocation>().AsNoTracking().Where(a => ids.Contains(a.InvoiceId))
            .GroupBy(a => a.InvoiceId).Select(g => new { g.Key, Sum = g.Sum(a => a.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        var balances = await InvoiceBalances.ForAsync(_db, ids, null, ct);
        foreach (var c in certificates.Where(c => c.InvoiceId != null))
        {
            c.Collected = collected.GetValueOrDefault(c.InvoiceId!.Value);
            c.AmountDue = balances.TryGetValue(c.InvoiceId.Value, out var b) ? b.AmountDue : 0;
        }
    }

    public async Task<ContractCertificateDto> TemplateAsync(Guid contractId, CancellationToken ct = default)
    {
        var contract = await LoadContractAsync(contractId, track: false, ct);
        var cert = new ContractCertificate
        {
            ContractId = contractId, Kind = ContractCertificateKinds.Interim, Status = St.Draft, CertificateDate = SaudiTime.Today,
            SequenceNo = await NextSequenceAsync(contractId, ct),
        };
        // نوع المستخلص التالي وفترته من جدولة العقد (الدفعة الأخيرة ختامية، والدوري بفترته المحسوبة)
        var plan = ContractCertificateScheduler.Build(contract, await ScheduledAsync(contractId, null, ct), SaudiTime.Today);
        if (plan.NextMustBeFinal) cert.Kind = ContractCertificateKinds.Final;
        if (plan.Mode == ContractCertificateSchedules.Periodic) { cert.PeriodFrom = plan.NextPeriodFrom; cert.PeriodTo = plan.NextPeriodTo; }
        var previous = await CertifiedQuantitiesAsync(contractId, ct);
        // القالب للعرض فقط: لا يُرفض لتجاوز (لا كميات حالية فيه)
        BuildLines(cert, contract, previous, new Dictionary<Guid, decimal>(), validate: false);
        ApplyDeductions(cert, contract, await PriorDeductionsAsync(contractId, ct), penalty: 0, validate: false);
        var dto = ToDto(cert);
        await ApplySiteAsync(contractId, new List<ContractCertificateDto> { dto }, ct);
        return dto;
    }

    /// <summary>
    /// المنفذ في الموقع لكل بند: مجموع الكميات الفعلية في التقارير اليومية المعتمدة حتى نهاية فترة المستخلص (أو تاريخه)، وتنبيه حين
    /// يتجاوزه التراكمي المطالب به. مشروع بلا تقارير معتمدة لا يُقارن (فارغ). عقد الباطن يُقاس على تقارير مشروعه لا تقاريره.
    /// </summary>
    internal async Task ApplySiteAsync(Guid contractId, List<ContractCertificateDto> certificates, CancellationToken ct)
    {
        if (certificates.Count == 0) return;
        var rows = await (from h in _db.Set<SiteHourEntry>().AsNoTracking()
                          join r in _db.Set<SiteDailyReport>().AsNoTracking() on h.ReportId equals r.Id
                          where r.ContractId == contractId && r.Status == SiteDailyReportStatuses.Approved
                          select new { h.ContractItemId, r.ReportDate, h.ActualQuantity }).ToListAsync(ct);
        if (rows.Count == 0) return;
        foreach (var c in certificates)
        {
            var cutoff = (c.PeriodTo ?? c.CertificateDate).Date;
            var executed = rows.Where(x => x.ReportDate.Date <= cutoff).GroupBy(x => x.ContractItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.ActualQuantity));
            foreach (var l in c.Lines)
            {
                var done = executed.GetValueOrDefault(l.ContractItemId);
                l.SiteExecutedQuantity = done;
                l.ExceedsSite = l.CumulativeQuantity > done + 0.00005m && l.CurrentQuantity > 0;
            }
        }
    }

    public async Task<BoqProgressDto> ProgressAsync(Guid contractId, CancellationToken ct = default)
    {
        var contract = await LoadContractAsync(contractId, track: false, ct);
        var certified = await CertifiedQuantitiesAsync(contractId, ct);
        var certs = await _db.Set<ContractCertificate>().AsNoTracking().Where(c => c.ContractId == contractId)
            .Select(c => new { c.Id, c.Status, c.Kind, c.AdvanceRecovery, c.RetentionAmount, c.PenaltyAmount, c.OtherDeductions, c.NetPayable })
            .ToListAsync(ct);
        var counted = certs.Where(c => c.Status is St.Approved or St.Invoiced).ToList();
        var advance = DocumentPricing.Round(contract.ContractValue * contract.AdvancePercent / 100m);
        var recovered = counted.Sum(c => c.AdvanceRecovery);
        var invoices = await _db.Set<ContractInvoice>().AsNoTracking().Where(i => i.ContractId == contractId && i.Kind != "credit_note")
            .Select(i => new { i.Kind, i.InvoiceId, i.InvoiceNumber, i.TotalAmount, i.RetentionAmount, i.GrossAmount }).ToListAsync(ct);
        // فواتير مستخلصات أُلغيت بإشعار: ضمانها وإجماليها يُطرحان (الإشعار يحمل مبالغ ما ألغاه)
        var cancelled = await _db.Set<ContractInvoice>().AsNoTracking().Where(i => i.ContractId == contractId && i.Kind == "credit_note")
            .GroupBy(i => 1).Select(g => new { Retention = g.Sum(i => i.RetentionAmount), Total = g.Sum(i => i.TotalAmount) }).FirstOrDefaultAsync(ct);
        var advanceInvoice = invoices.FirstOrDefault(i => i.Kind == "advance");
        var advanceBilled = advanceInvoice?.GrossAmount ?? 0;
        var retentionInvoiced = invoices.Where(i => i.Kind != "retention_release").Sum(i => i.RetentionAmount) - (cancelled?.Retention ?? 0);
        var retentionReleased = invoices.Where(i => i.Kind == "retention_release").Sum(i => i.GrossAmount);
        var invoiceIds = invoices.Where(i => i.InvoiceId != null).Select(i => i.InvoiceId!.Value).ToList();
        var collected = invoiceIds.Count == 0 ? 0
            : await _db.Set<VoucherAllocation>().AsNoTracking().Where(a => invoiceIds.Contains(a.InvoiceId)).SumAsync(a => a.Amount, ct);

        var lines = Sorted(contract).Select(i =>
        {
            var qty = certified.GetValueOrDefault(i.Id);
            return new BoqProgressLineDto
            {
                ContractItemId = i.Id, LineCode = i.LineCode, SectionName = i.SectionName, Description = i.Description, Unit = i.Unit,
                LineKind = i.LineKind, SortOrder = i.SortOrder, UnitPrice = i.UnitPrice,
                ContractQuantity = i.RevisedQuantity, OverrunPercent = i.OverrunPercent, MaxQuantity = MaxQuantity(i),
                CertifiedQuantity = qty, RemainingQuantity = Math.Max(0, i.RevisedQuantity - qty),
                CertifiedAmount = DocumentPricing.Round(qty * i.UnitPrice), CompletionPercent = Percent(qty, i.RevisedQuantity),
            };
        }).ToList();

        var certifiedValue = lines.Sum(l => l.CertifiedAmount);
        var advanceDue = advanceInvoice?.InvoiceId is Guid advanceId
            && (await InvoiceBalances.ForAsync(_db, new List<Guid> { advanceId }, null, ct)).TryGetValue(advanceId, out var advanceBalance)
                ? advanceBalance.AmountDue : 0;
        return new BoqProgressDto
        {
            ContractId = contractId, ContractValue = contract.ContractValue, CertifiedValue = certifiedValue,
            RemainingValue = Math.Max(0, contract.ContractValue - certifiedValue), CompletionPercent = Percent(certifiedValue, contract.ContractValue),
            ApprovedCertificates = counted.Count,
            AdvancePercent = contract.AdvancePercent, RetentionPercent = contract.RetentionPercent, VatRate = contract.VatRate,
            MaxPenaltyPercent = contract.MaxPenaltyPercent,
            AdvanceAmount = advance, AdvanceRecovered = recovered, AdvanceOutstanding = Math.Max(0, advanceBilled - recovered),
            AdvanceBilled = advanceBilled, AdvanceInvoiceNumber = advanceInvoice?.InvoiceNumber,
            AdvanceInvoiceId = advanceInvoice?.InvoiceId, AdvanceAmountDue = advanceDue,
            RetentionInvoiced = retentionInvoiced, RetentionReleased = retentionReleased,
            RetentionOutstanding = Math.Max(0, retentionInvoiced - retentionReleased),
            InvoicedTotal = invoices.Where(i => i.InvoiceId != null).Sum(i => i.TotalAmount) - (cancelled?.Total ?? 0), CollectedTotal = collected,
            RetentionHeld = counted.Sum(c => c.RetentionAmount), PenaltiesTotal = counted.Sum(c => c.PenaltyAmount),
            PenaltyCap = PenaltyCap(contract), OtherDeductionsTotal = counted.Sum(c => c.OtherDeductions),
            NetPayableTotal = counted.Sum(c => c.NetPayable),
            HasFinalCertificate = certs.Any(c => c.Kind == ContractCertificateKinds.Final && c.Status is St.Approved or St.Invoiced),
            OpenCertificateId = certs.FirstOrDefault(c => St.IsOpen(c.Status))?.Id,
            Schedule = ContractCertificateScheduler.Build(contract, await ScheduledAsync(contractId, null, ct), SaudiTime.Today),
            Lines = lines,
        };
    }

    // ---------- الإعداد (مسودة) ----------

    public Task<ContractCertificateDto> CreateAsync(Guid contractId, SaveContractCertificateDto request, CancellationToken ct = default)
        => new TransactionRunner(_db).RunAsync(async token =>
        {
            var contract = await LoadContractAsync(contractId, track: true, token);
            RequireActive(contract);
            ValidateHeader(request);
            var open = await _db.Set<ContractCertificate>().AsNoTracking()
                .Where(c => c.ContractId == contractId && (c.Status == St.Draft || c.Status == St.Submitted))
                .Select(c => c.CertificateNumber).FirstOrDefaultAsync(token);
            if (open != null) throw new ConflictException(string.Format(Messages.CertificateOpenExists, open));
            if (await _db.Set<ContractCertificate>().AnyAsync(c => c.ContractId == contractId && c.Kind == ContractCertificateKinds.Final
                    && (c.Status == St.Approved || c.Status == St.Invoiced), token))
                throw new ConflictException(Messages.CertificateAfterFinal);
            await ApplyScheduleAsync(contract, request, null, token);

            var date = request.CertificateDate?.Date ?? SaudiTime.Today;
            var cert = new ContractCertificate
            {
                ContractId = contractId, Status = St.Draft, SequenceNo = await NextSequenceAsync(contractId, token),
                CertificateNumber = await _numbers.AssignAsync(new DocumentNumberRequest
                {
                    Key = "contract_certificate", DefaultPrefix = "CRT-", WarehouseId = contract.WarehouseId, Date = date,
                    Exists = (n, c) => _db.Set<ContractCertificate>().AnyAsync(x => x.CertificateNumber == n, c),
                }, token),
            };
            ApplyHeader(cert, request, date);
            BuildLines(cert, contract, await CertifiedQuantitiesAsync(contractId, token), CurrentQuantities(request, contract), validate: true);
            await ApplyMaterialsAsync(cert, contract, request.Materials ?? new(), token);
            SetDeductions(cert, request.Deductions);
            ApplyDeductions(cert, contract, await PriorDeductionsAsync(contractId, token), request.PenaltyAmount, validate: true);
            _db.Add(cert);
            await SaveAsync(token);
            return ToDto(cert);
        }, ct);

    public Task<ContractCertificateDto> UpdateAsync(Guid contractId, Guid certificateId, SaveContractCertificateDto request, CancellationToken ct = default)
        => new TransactionRunner(_db).RunAsync(async token =>
        {
            var contract = await LoadContractAsync(contractId, track: false, token);
            RequireActive(contract);
            ValidateHeader(request);
            var cert = await LoadCertificateAsync(contractId, certificateId, track: true, token);
            RequireStatus(cert, St.Draft);
            await ApplyScheduleAsync(contract, request, cert.Id, token);
            ApplyHeader(cert, request, request.CertificateDate?.Date ?? cert.CertificateDate);
            BuildLines(cert, contract, await CertifiedQuantitiesAsync(contractId, token), CurrentQuantities(request, contract), validate: true);
            await ApplyMaterialsAsync(cert, contract, request.Materials, token);
            SetDeductions(cert, request.Deductions);
            ApplyDeductions(cert, contract, await PriorDeductionsAsync(contractId, token), request.PenaltyAmount, validate: true);
            await SaveAsync(token);
            return ToDto(cert);
        }, ct);

    // ---------- الدورة ----------

    public Task<ContractCertificateDto> SubmitAsync(Guid contractId, Guid certificateId, CancellationToken ct = default)
        => new TransactionRunner(_db).RunAsync(async token =>
        {
            var (contract, cert) = await LoadForActionAsync(contractId, certificateId, token);
            RequireStatus(cert, St.Draft);
            await RemeasureAsync(cert, contract, token);
            if (cert.Kind == ContractCertificateKinds.Interim && cert.CurrentWorkValue <= 0) throw new ValidationFailedException(Messages.CertificateEmpty);
            // الدوري: المنجز يُحسب بنهاية الفترة، فالمستخلص الجاري لا يُقدَّم قبلها؛ الفترة موعد تقديم لا قيد على الإنجاز:
            // الختامي، أو مستخلص يُكمل كل البنود 100%، يُقدَّم متى انتهت الأعمال
            if (contract.CertificateSchedule == ContractCertificateSchedules.Periodic && cert.Kind == ContractCertificateKinds.Interim
                && cert.PeriodTo is { } periodEnd && SaudiTime.Today < periodEnd.Date && !CompletesContract(cert))
                throw new ConflictException(string.Format(Messages.CertificatePeriodNotEnded, periodEnd.ToString("yyyy-MM-dd")));
            cert.Status = St.Submitted;
            cert.SubmittedAt = DateTime.UtcNow; cert.SubmittedBy = _user.Name ?? "system";
            await SaveAsync(token);
            return ToDto(cert);
        }, ct);

    public Task<ContractCertificateDto> ApproveAsync(Guid contractId, Guid certificateId, CancellationToken ct = default)
        => new TransactionRunner(_db).RunAsync(async token =>
        {
            var (contract, cert) = await LoadForActionAsync(contractId, certificateId, token);
            RequireStatus(cert, St.Submitted);
            // السابق (كميات واسترداد وغرامات) يُعاد من القاعدة لحظة الاعتماد: لو اعتُمد غيره في الأثناء يُرفض التجاوز هنا
            await RemeasureAsync(cert, contract, token);
            cert.Status = St.Approved;
            cert.ApprovedAt = DateTime.UtcNow; cert.ApprovedBy = _user.Name ?? "system"; cert.ApprovedByUserId = _user.UserId;
            await SaveAsync(token);
            return ToDto(cert);
        }, ct);

    public Task<ContractCertificateDto> ReturnAsync(Guid contractId, Guid certificateId, ContractCertificateActionDto request, CancellationToken ct = default)
        => new TransactionRunner(_db).RunAsync(async token =>
        {
            var note = RequireNote(request);
            var (_, cert) = await LoadForActionAsync(contractId, certificateId, token);
            RequireStatus(cert, St.Submitted);
            cert.Status = St.Draft; cert.ReturnNote = note;
            cert.SubmittedAt = null; cert.SubmittedBy = null;
            await SaveAsync(token);
            return ToDto(cert);
        }, ct);

    public Task<ContractCertificateDto> CancelAsync(Guid contractId, Guid certificateId, ContractCertificateActionDto request, CancellationToken ct = default)
        => new TransactionRunner(_db).RunAsync(async token =>
        {
            var note = RequireNote(request);
            var cert = await LoadCertificateAsync(contractId, certificateId, track: true, token);
            if (!St.IsOpen(cert.Status)) throw new ConflictException(string.Format(Messages.CertificateStatusInvalid, cert.Status));
            cert.Status = St.Cancelled; cert.ReturnNote = note;
            await SaveAsync(token);
            return ToDto(cert);
        }, ct);

    // ---------- الحساب ----------

    /// <summary>
    /// أسطر المستخلص لكل بنود جدول الكميات بترتيبها: السابق من المعتمد، الحالي من الطلب، التراكمي = السابق + الحالي،
    /// القيمة الحالية = قيمة التراكمي − قيمة السابق (فلا تتراكم فروق التقريب). الأسطر القائمة تُحدَّث بمفتاح البند.
    /// </summary>
    private void BuildLines(ContractCertificate cert, CommercialContract contract, Dictionary<Guid, decimal> previous,
        Dictionary<Guid, decimal> current, bool validate)
    {
        var errors = new List<string>();
        var existing = cert.Lines.ToDictionary(l => l.ContractItemId);
        var keep = new HashSet<Guid>();
        foreach (var item in Sorted(contract))
        {
            var line = existing.GetValueOrDefault(item.Id);
            if (line == null)
            {
                line = new ContractCertificateLine { CertificateId = cert.Id, ContractItemId = item.Id };
                cert.Lines.Add(line);
            }
            keep.Add(item.Id);
            line.LineCode = item.LineCode; line.SectionName = item.SectionName; line.Description = item.Description; line.Unit = item.Unit;
            line.LineKind = item.LineKind; line.UnitPrice = item.UnitPrice; line.ContractQuantity = item.RevisedQuantity;
            line.OverrunPercent = item.OverrunPercent; line.SortOrder = item.SortOrder;
            line.PreviousQuantity = previous.GetValueOrDefault(item.Id);
            line.CurrentQuantity = current.GetValueOrDefault(item.Id);
            line.CumulativeQuantity = line.PreviousQuantity + line.CurrentQuantity;
            line.PreviousAmount = DocumentPricing.Round(line.PreviousQuantity * line.UnitPrice);
            line.CumulativeAmount = DocumentPricing.Round(line.CumulativeQuantity * line.UnitPrice);
            line.CurrentAmount = line.CumulativeAmount - line.PreviousAmount;
            line.CompletionPercent = Percent(line.CumulativeQuantity, line.ContractQuantity);

            var max = MaxQuantity(item);
            if (validate && line.CurrentQuantity > 0 && line.CumulativeQuantity > max + 0.00005m)
                errors.Add(string.Format(Messages.CertificateExceedsQuantity, Label(item), line.CumulativeQuantity.ToString("0.####"), max.ToString("0.####")));
        }
        foreach (var stale in cert.Lines.Where(l => !keep.Contains(l.ContractItemId)).ToList())
        {
            cert.Lines.Remove(stale);
            if (_db.Entry(stale).State != EntityState.Detached) _db.Remove(stale);
        }
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        cert.PreviousWorkValue = cert.Lines.Sum(l => l.PreviousAmount);
        cert.CumulativeWorkValue = cert.Lines.Sum(l => l.CumulativeAmount);
        cert.CurrentWorkValue = cert.CumulativeWorkValue - cert.PreviousWorkValue;
    }

    /// <summary>إعادة القياس بالكميات والغرامة والخصومات المحفوظة نفسها، وسابق محدَّث من القاعدة (عند التقديم والاعتماد).</summary>
    private async Task RemeasureAsync(ContractCertificate cert, CommercialContract contract, CancellationToken ct)
    {
        BuildLines(cert, contract, await CertifiedQuantitiesAsync(contract.Id, ct),
            cert.Lines.ToDictionary(l => l.ContractItemId, l => l.CurrentQuantity), validate: true);
        await ApplyMaterialsAsync(cert, contract, null, ct);
        ApplyDeductions(cert, contract, await PriorDeductionsAsync(contract.Id, ct), cert.PenaltyAmount, validate: true);
    }

    // ---------- المواد المسلَّمة ----------

    /// <summary>
    /// كميات أذونات التسليم المختارة (تحل محل السابقة، أو تُعاد كما هي حين <paramref name="requested"/> فارغ) بعد التحقق من القاعدة:
    /// الإذن من هذا العقد ومرحّل (لا ملغى ولا مسودة)، والسطر على بند يقيس المستخلص أعماله حتى الآن (تراكمي موجب)، والكمية موجبة
    /// ولا تتجاوز المتبقي = المسلَّم − المختار في مستخلصات أخرى غير ملغاة. التكلفة = الكمية × تكلفة الصرف الفعلية، والجزء الأخير
    /// يأخذ باقي تكلفة السطر فلا يبقى كسر. عقد المورد (مقاول باطن) لا مواد فيه: المواد تُصرف لمشروعنا مع العميل.
    /// </summary>
    private async Task ApplyMaterialsAsync(ContractCertificate cert, CommercialContract contract, List<SaveContractCertificateMaterialDto>? requested, CancellationToken ct)
    {
        var wanted = requested ?? cert.Materials.Select(m => new SaveContractCertificateMaterialDto { IssueLineId = m.IssueLineId, Quantity = m.Quantity }).ToList();
        var errors = new List<string>();
        if (wanted.Any(m => m.Quantity < 0)) errors.Add("كمية المادة في المستخلص لا تكون سالبة.");
        wanted = wanted.Where(m => m.Quantity > 0).ToList();
        if (wanted.GroupBy(m => m.IssueLineId).Any(g => g.Count() > 1)) errors.Add("سطر إذن التسليم مكرر في مواد المستخلص.");
        if (wanted.Count > 0 && string.Equals(contract.PartyType, "supplier", StringComparison.OrdinalIgnoreCase))
            errors.Add("مواد المشروع المسلَّمة تُختار في مستخلصات عقود العملاء فقط.");
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        foreach (var old in cert.Materials.ToList())
        {
            cert.Materials.Remove(old);
            if (_db.Entry(old).State != EntityState.Detached) _db.Remove(old);
        }
        cert.MaterialCost = 0;
        if (wanted.Count == 0) return;

        var ids = wanted.Select(m => m.IssueLineId).ToList();
        // متتبَّعة: يُلمس رمز تزامنها فيتعارض الاختيار مع إلغاء الإذن (أو اختيار آخر) في اللحظة نفسها
        var lines = await _db.Set<ProjectMaterialIssueLine>().Include(l => l.Issue).Where(l => ids.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        var others = await CertifiedMaterials.ByIssueLineAsync(_db, contract.Id, cert.Id, ct);
        var withdrawn = await CertifiedMaterials.WithdrawnByLineAsync(_db, contract.Id, null, ct);
        var measured = cert.Lines.ToDictionary(l => l.ContractItemId, l => l.CumulativeQuantity);
        var labels = contract.Items.ToDictionary(i => i.Id, Label);
        var order = 0;
        foreach (var w in wanted)
        {
            if (!lines.TryGetValue(w.IssueLineId, out var line) || line.Issue.ContractId != contract.Id)
            {
                errors.Add("سطر إذن التسليم المختار ليس من أذونات هذا المشروع.");
                continue;
            }
            var issue = line.Issue;
            if (issue.Status != ProjectMaterialIssueStatuses.Posted || issue.Kind != ProjectMaterialIssueKinds.Issue)
            {
                errors.Add($"الإذن {issue.IssueNumber} غير مرحّل أو ملغى: لا تُختار منه مواد.");
                continue;
            }
            if (line.BoqItemId is not { } boqItemId)
            {
                errors.Add($"«{line.ItemName}» في الإذن {issue.IssueNumber} غير مربوط ببند في جدول الكميات.");
                continue;
            }
            if (measured.GetValueOrDefault(boqItemId) <= 0)
            {
                errors.Add($"«{line.ItemName}» ({issue.IssueNumber}): البند {labels.GetValueOrDefault(boqItemId, "?")} بلا أعمال مقيسة حتى هذا المستخلص؛ تُعترف تكلفة مواده مع إيراده.");
                continue;
            }
            var (otherQty, otherCost) = others.GetValueOrDefault(line.Id);
            // المُرجَع للمستودع والمهدر في الموقع سُحبا من السطر (الهدر اعتُرفت تكلفته عند ترحيله): كالمختار في مستخلص آخر
            var (wQty, wCost) = withdrawn.GetValueOrDefault(line.Id);
            otherQty += wQty; otherCost += wCost;
            var available = line.Quantity - otherQty;
            if (w.Quantity > available + 0.00005m)
            {
                errors.Add($"«{line.ItemName}» ({issue.IssueNumber}): الكمية {w.Quantity:0.####} تتجاوز المتبقي غير المختار {Math.Max(0, available):0.####} (المسلَّم {line.Quantity:0.####} − المختار في مستخلصات أخرى {otherQty:0.####}).");
                continue;
            }
            var all = Math.Abs(w.Quantity - available) <= 0.00005m;
            var cost = all ? line.TotalCost - otherCost : DocumentPricing.Round(w.Quantity * line.UnitCost);
            cert.Materials.Add(new ContractCertificateMaterial
            {
                CertificateId = cert.Id, IssueId = issue.Id, IssueLineId = line.Id, ContractItemId = boqItemId, ProductId = line.ProductId,
                IssueNumber = issue.IssueNumber, IssueDate = issue.Date, Sku = line.Sku, ItemName = line.ItemName, Unit = line.Unit,
                Quantity = all ? available : w.Quantity, UnitCost = line.UnitCost, TotalCost = cost, SortOrder = ++order,
            });
            _db.Entry(line).State = EntityState.Modified;
        }
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
        cert.MaterialCost = cert.Materials.Sum(m => m.TotalCost);
    }

    /// <summary>
    /// أسطر الأذونات المرحّلة المؤهَّلة للاختيار (على بنود جدول الكميات)، بالمسلَّم وتكلفته والمختار في مستخلصات أخرى غير ملغاة والمتبقي
    /// والمختار في <paramref name="certificateId"/>. تُعرض الأسطر ذات المتبقي أو المختارة في هذا المستخلص فقط.
    /// </summary>
    public async Task<List<CertificateMaterialOptionDto>> MaterialOptionsAsync(Guid contractId, Guid? certificateId, CancellationToken ct = default)
    {
        var contract = await LoadContractAsync(contractId, track: false, ct);
        var items = contract.Items.ToDictionary(i => i.Id);
        var rows = await _db.Set<ProjectMaterialIssueLine>().AsNoTracking()
            .Where(l => l.Issue.ContractId == contractId && l.Issue.Status == ProjectMaterialIssueStatuses.Posted
                && l.Issue.Kind == ProjectMaterialIssueKinds.Issue && l.BoqItemId != null)
            .Select(l => new
            {
                l.Id, l.IssueId, l.Issue.IssueNumber, l.Issue.Date, l.Issue.DebitAccountCode, l.Issue.JournalEntryId, BoqItemId = l.BoqItemId!.Value,
                l.ProductId, l.Sku, l.ItemName, l.Unit, l.Quantity, l.UnitCost, l.TotalCost, l.SortOrder,
            })
            .ToListAsync(ct);
        var others = await CertifiedMaterials.ByIssueLineAsync(_db, contractId, certificateId, ct);
        var withdrawn = await CertifiedMaterials.WithdrawnByLineAsync(_db, contractId, null, ct);
        var selected = certificateId is { } cid
            ? await _db.Set<ContractCertificateMaterial>().AsNoTracking().Where(m => m.CertificateId == cid)
                .ToDictionaryAsync(m => m.IssueLineId, m => m.Quantity, ct)
            : new Dictionary<Guid, decimal>();
        return rows.Where(r => items.ContainsKey(r.BoqItemId))
            .Select(r =>
            {
                var (q, c) = others.GetValueOrDefault(r.Id);
                var (wq, wc) = withdrawn.GetValueOrDefault(r.Id);
                q += wq; c += wc;
                var item = items[r.BoqItemId];
                return new CertificateMaterialOptionDto
                {
                    IssueId = r.IssueId, IssueLineId = r.Id, IssueNumber = r.IssueNumber, IssueDate = r.Date,
                    ContractItemId = r.BoqItemId, LineCode = item.LineCode, BoqDescription = item.Description,
                    ProductId = r.ProductId, Sku = r.Sku, ItemName = r.ItemName, Unit = r.Unit,
                    DeliveredQuantity = r.Quantity, UnitCost = r.UnitCost, TotalCost = r.TotalCost,
                    BilledQuantity = q, BilledCost = c, RemainingQuantity = Math.Max(0, r.Quantity - q), RemainingCost = Math.Max(0, r.TotalCost - c),
                    SelectedQuantity = selected.GetValueOrDefault(r.Id),
                    CostRecognizedAtDelivery = r.DebitAccountCode == null,
                };
            })
            .Where(o => o.RemainingQuantity > 0 || o.SelectedQuantity > 0)
            .OrderBy(o => items[o.ContractItemId].SortOrder).ThenBy(o => o.IssueDate).ThenBy(o => o.IssueNumber).ThenBy(o => o.Sku)
            .ToList();
    }

    private readonly record struct PriorDeductions(decimal AdvanceBilled, decimal AdvanceRecovered, decimal Penalties);

    /// <summary>
    /// الدفعة المقدمة المفوترة فعلاً (لا يُسترد ما لم يُفوتر)، وما استُرد منها وما فُرض من غرامات في المستخلصات المعتمدة/المفوترة.
    /// </summary>
    private async Task<PriorDeductions> PriorDeductionsAsync(Guid contractId, CancellationToken ct)
    {
        var counted = _db.Set<ContractCertificate>().AsNoTracking()
            .Where(c => c.ContractId == contractId && (c.Status == St.Approved || c.Status == St.Invoiced));
        var billed = await AdvanceBilledAsync(_db, contractId, ct);
        return new(billed, await counted.SumAsync(c => c.AdvanceRecovery, ct), await counted.SumAsync(c => c.PenaltyAmount, ct));
    }

    /// <summary>صافي فاتورة الدفعة المقدمة للعقد (قبل الضريبة).</summary>
    internal static Task<decimal> AdvanceBilledAsync(ErpDbContext db, Guid contractId, CancellationToken ct)
        => db.Set<ContractInvoice>().AsNoTracking().Where(i => i.ContractId == contractId && i.Kind == "advance").SumAsync(i => i.GrossAmount, ct);

    /// <summary>
    /// الخصومات والضريبة على الأعمال الحالية:
    /// استرداد الدفعة = الحالي × نسبة الدفعة (الختامي يسترد الباقي كله)، بحد المتبقي من الدفعة المفوترة ولا يتجاوز الحالي؛
    /// الأساس الضريبي = الحالي − الاسترداد (الدفعة ضُرِّبت في فاتورتها)، والضريبة عليه؛ الضمان = الحالي × نسبة الضمان؛
    /// الغرامة بحد سقف العقد التراكمي؛ الصافي = الإجمالي بالضريبة − الضمان − الغرامة − الخصومات الأخرى، ولا يكون سالباً.
    /// </summary>
    private static void ApplyDeductions(ContractCertificate cert, CommercialContract contract, PriorDeductions prior, decimal penalty, bool validate)
    {
        var errors = new List<string>();
        var work = Math.Max(0, cert.CurrentWorkValue);

        var outstanding = Math.Max(0, prior.AdvanceBilled - prior.AdvanceRecovered);
        var recovery = cert.Kind == ContractCertificateKinds.Final ? outstanding : DocumentPricing.Round(work * contract.AdvancePercent / 100m);
        cert.AdvanceRecovery = Math.Min(Math.Min(recovery, outstanding), work);

        cert.TaxableAmount = work - cert.AdvanceRecovery;
        cert.VatRate = contract.VatRate;
        cert.VatAmount = DocumentPricing.Round(cert.TaxableAmount * cert.VatRate / 100m);
        cert.TotalWithVat = cert.TaxableAmount + cert.VatAmount;
        cert.RetentionAmount = DocumentPricing.Round(work * contract.RetentionPercent / 100m);

        if (penalty < 0) errors.Add(Messages.CertificatePenaltyNegative);
        var cap = PenaltyCap(contract);
        if (validate && cap > 0 && prior.Penalties + penalty > cap + 0.005m)
            errors.Add(string.Format(Messages.CertificatePenaltyExceedsCap, Math.Max(0, cap - prior.Penalties).ToString("0.00")));
        cert.PenaltyAmount = Math.Max(0, penalty);

        cert.OtherDeductions = cert.Deductions.Sum(d => d.Amount);
        cert.NetPayable = cert.TotalWithVat - cert.RetentionAmount - cert.PenaltyAmount - cert.OtherDeductions;
        if (validate && cert.NetPayable < -0.005m) errors.Add(Messages.CertificateNetNegative);

        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
    }

    /// <summary>سقف الغرامات التراكمي = قيمة العقد × أقصى نسبة غرامة؛ 0 = بلا سقف.</summary>
    private static decimal PenaltyCap(CommercialContract c) => DocumentPricing.Round(c.ContractValue * c.MaxPenaltyPercent / 100m);

    /// <summary>الخصومات الأخرى كما أرسلها المُعِد (وصف ومبلغ موجب لكل خصم) تحل محل السابقة.</summary>
    private void SetDeductions(ContractCertificate cert, List<SaveContractCertificateDeductionDto> deductions)
    {
        if (deductions.Any(d => string.IsNullOrWhiteSpace(d.Description) || d.Amount <= 0))
            throw new ValidationFailedException(Messages.CertificateDeductionInvalid);
        foreach (var old in cert.Deductions.ToList())
        {
            cert.Deductions.Remove(old);
            if (_db.Entry(old).State != EntityState.Detached) _db.Remove(old);
        }
        var order = 0;
        foreach (var d in deductions)
            cert.Deductions.Add(new ContractCertificateDeduction
            {
                CertificateId = cert.Id, Description = d.Description.Trim(), Amount = DocumentPricing.Round(d.Amount), SortOrder = ++order,
            });
    }

    /// <summary>الكميات الحالية من الطلب: بنود العقد فقط، بلا تكرار، ولا سالب.</summary>
    private static Dictionary<Guid, decimal> CurrentQuantities(SaveContractCertificateDto request, CommercialContract contract)
    {
        var itemIds = contract.Items.Where(i => !i.IsGroup).Select(i => i.Id).ToHashSet();
        var errors = new List<string>();
        if (request.Lines.Any(l => !itemIds.Contains(l.ContractItemId))) errors.Add(Messages.CertificateLineUnknownItem);
        if (request.Lines.GroupBy(l => l.ContractItemId).Any(g => g.Count() > 1)) errors.Add(Messages.CertificateDuplicateItem);
        if (request.Lines.Any(l => l.CurrentQuantity < 0)) errors.Add(Messages.CertificateNegativeQuantity);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
        return request.Lines.ToDictionary(l => l.ContractItemId, l => l.CurrentQuantity);
    }

    /// <summary>الكمية المعتمدة (المنفذة) لكل بند = مجموع الحالي في المستخلصات المعتمدة والمفوترة.</summary>
    private Task<Dictionary<Guid, decimal>> CertifiedQuantitiesAsync(Guid contractId, CancellationToken ct)
        => _db.Set<ContractCertificateLine>().AsNoTracking()
            .Where(l => l.Certificate.ContractId == contractId && (l.Certificate.Status == St.Approved || l.Certificate.Status == St.Invoiced))
            .GroupBy(l => l.ContractItemId)
            .Select(g => new { g.Key, Qty = g.Sum(l => l.CurrentQuantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);

    private static decimal MaxQuantity(CommercialContractItem i) => i.RevisedQuantity * (1 + i.OverrunPercent / 100m);

    /// <summary>المستخلص يُكمل المشروع: تراكمي كل بند بكمية ≥ كمية العقد المعدّلة (كقاعدة الاستلام الأولي في سير العقد).</summary>
    private static bool CompletesContract(ContractCertificate cert)
    {
        var lines = cert.Lines.Where(l => l.ContractQuantity > 0).ToList();
        return lines.Count > 0 && lines.All(l => l.CumulativeQuantity >= l.ContractQuantity - 0.00005m);
    }

    private static decimal Percent(decimal part, decimal whole) => whole > 0 ? Math.Round(part / whole * 100m, 2) : 0;

    private static string Label(CommercialContractItem i) => string.IsNullOrWhiteSpace(i.LineCode) ? i.Description : $"{i.LineCode} {i.Description}";

    /// <summary>أسطر القياس بترتيبها: السطر الرئيسي ذو الفروع (مجموعة) لا يُقاس؛ فروعه هي التي تُقاس وتُسعَّر.</summary>
    private static IEnumerable<CommercialContractItem> Sorted(CommercialContract c) => c.Items.Where(i => !i.IsGroup).OrderBy(i => i.SortOrder).ThenBy(i => i.CreatedAt);

    // ---------- التحقق والتحميل ----------

    private static void ValidateHeader(SaveContractCertificateDto r)
    {
        var errors = new List<string>();
        if (!ContractCertificateKinds.IsValid(r.Kind)) errors.Add(Messages.CertificateKindInvalid);
        if (r.PeriodFrom.HasValue && r.PeriodTo.HasValue && r.PeriodTo < r.PeriodFrom) errors.Add(Messages.CertificatePeriodInvalid);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
    }

    private static void ApplyHeader(ContractCertificate cert, SaveContractCertificateDto r, DateTime date)
    {
        cert.Kind = r.Kind;
        cert.CertificateDate = date;
        cert.PeriodFrom = r.PeriodFrom?.Date; cert.PeriodTo = r.PeriodTo?.Date;
        cert.Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim();
    }

    /// <summary>
    /// يفرض جدولة العقد على المستخلص (الجديد، أو المسودة <paramref name="certificateId"/> في خانتها): لا مستخلص بعد اكتمال
    /// الدفعات، والدفعة الأخيرة ختامية، والدوري يأخذ فترته المحسوبة لا ما أُرسل.
    /// </summary>
    private async Task ApplyScheduleAsync(CommercialContract contract, SaveContractCertificateDto request, Guid? certificateId, CancellationToken ct)
    {
        if (contract.CertificateSchedule == ContractCertificateSchedules.OnDemand) return;
        var plan = ContractCertificateScheduler.Build(contract, await ScheduledAsync(contract.Id, certificateId, ct), SaudiTime.Today);
        if (contract.CertificateSchedule == ContractCertificateSchedules.Installments && plan.Complete)
            throw new ConflictException(string.Format(Messages.CertificateScheduleComplete, contract.CertificateCount));
        if (plan.NextMustBeFinal) request.Kind = ContractCertificateKinds.Final;
        if (contract.CertificateSchedule == ContractCertificateSchedules.Periodic)
        {
            request.PeriodFrom = plan.NextPeriodFrom;
            request.PeriodTo = plan.NextPeriodTo;
        }
    }

    /// <summary>المستخلصات غير الملغاة بترتيبها (عدا <paramref name="excludeId"/>): تشغل خانات الجدولة.</summary>
    private async Task<List<ScheduledCertificate>> ScheduledAsync(Guid contractId, Guid? excludeId, CancellationToken ct)
        => (await _db.Set<ContractCertificate>().AsNoTracking()
                .Where(c => c.ContractId == contractId && c.Status != St.Cancelled && (excludeId == null || c.Id != excludeId))
                .OrderBy(c => c.SequenceNo)
                .Select(c => new { c.Id, c.CertificateNumber, c.Status, c.Kind, c.PeriodFrom, c.PeriodTo, c.CurrentWorkValue, c.NetPayable })
                .ToListAsync(ct))
            .Select(c => new ScheduledCertificate(c.Id, c.CertificateNumber, c.Status, c.Kind, c.PeriodFrom, c.PeriodTo, c.CurrentWorkValue, c.NetPayable))
            .ToList();

    private static void RequireActive(CommercialContract c)
    {
        if (c.Stage is not (ContractStageDefinition.Active or ContractStageDefinition.InitialAcceptance))
            throw new ConflictException(Messages.CertificateContractNotActive);
    }

    private static void RequireStatus(ContractCertificate cert, string status)
    {
        if (cert.Status != status) throw new ConflictException(string.Format(Messages.CertificateStatusInvalid, cert.Status));
    }

    private static string RequireNote(ContractCertificateActionDto r)
        => string.IsNullOrWhiteSpace(r.Note) ? throw new ValidationFailedException(Messages.CertificateNoteRequired) : r.Note.Trim();

    private async Task<(CommercialContract, ContractCertificate)> LoadForActionAsync(Guid contractId, Guid certificateId, CancellationToken ct)
    {
        var contract = await LoadContractAsync(contractId, track: false, ct);
        RequireActive(contract);
        return (contract, await LoadCertificateAsync(contractId, certificateId, track: true, ct));
    }

    /// <summary>العقد ببنوده، بشرط أن تكون فوترته بالمستخلصات.</summary>
    private async Task<CommercialContract> LoadContractAsync(Guid contractId, bool track, CancellationToken ct)
    {
        var q = _db.Set<CommercialContract>().Include(c => c.Items);
        var contract = await (track ? q : q.AsNoTracking()).FirstOrDefaultAsync(c => c.Id == contractId, ct)
            ?? throw new NotFoundException(Messages.ContractNotFound);
        if (!await CommercialContractService.IsBoqContractAsync(_db, contract.ContractType, ct))
            throw new ConflictException(Messages.BoqCertificatesOnlyForBoq);
        return contract;
    }

    private async Task<ContractCertificate> LoadCertificateAsync(Guid contractId, Guid certificateId, bool track, CancellationToken ct)
    {
        var q = _db.Set<ContractCertificate>().Include(c => c.Lines).Include(c => c.Deductions).Include(c => c.Materials).AsSplitQuery();
        return await (track ? q : q.AsNoTracking()).FirstOrDefaultAsync(c => c.Id == certificateId && c.ContractId == contractId, ct)
            ?? throw new NotFoundException(Messages.CertificateNotFound);
    }

    /// <summary>الرقم التالي داخل العقد (الملغى يحتفظ برقمه فلا يتكرر).</summary>
    private async Task<int> NextSequenceAsync(Guid contractId, CancellationToken ct)
        => (await _db.Set<ContractCertificate>().Where(c => c.ContractId == contractId).MaxAsync(c => (int?)c.SequenceNo, ct) ?? 0) + 1;

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when ((ex.InnerException?.Message ?? ex.Message).Contains("IX_ContractCertificate_TenantId_ContractId_Open", StringComparison.OrdinalIgnoreCase))
        {
            // سباق على الفهرس الفريد للمستخلص المفتوح
            throw new ConflictException(string.Format(Messages.CertificateOpenExists, string.Empty));
        }
    }

    private static ContractCertificateDto ToDto(ContractCertificate c)
    {
        var dto = Mapper.Map<ContractCertificateDto>(c);
        dto.Lines = c.Lines.OrderBy(l => l.SortOrder).Select(l =>
        {
            var line = Mapper.Map<ContractCertificateLineDto>(l);
            line.CurrentCompletionPercent = Percent(l.CurrentQuantity, l.ContractQuantity);
            return line;
        }).ToList();
        dto.Deductions = c.Deductions.OrderBy(d => d.SortOrder).Select(d => Mapper.Map<ContractCertificateDeductionDto>(d)).ToList();
        dto.Materials = c.Materials.OrderBy(m => m.SortOrder).Select(m => Mapper.Map<ContractCertificateMaterialDto>(m)).ToList();
        return dto;
    }
}
