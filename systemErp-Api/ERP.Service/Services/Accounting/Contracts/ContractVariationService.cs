using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using CertSt = ERP.Core.Models.Accounting.ContractCertificateStatuses;
using St = ERP.Core.Models.Accounting.ContractVariationStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// أوامر التغيير على عقود جدول الكميات السارية. القواعد:
/// <list type="bullet">
/// <item>التسجيل والتقديم لا يغيّران العقد؛ قيمة العقد تتغير بالاعتماد وحده (ويمر بسياسة الاعتماد إن وُجدت).</item>
/// <item>سطر البند القائم: فرق كمية (موجب أو سالب) بسعر البند وتكلفته التقديرية الحاليين؛ يُضاف إلى الكمية المعدلة
/// (<see cref="CommercialContractItem.RevisedQuantity"/>) ولا يمس الكمية الأصلية. الكمية بعد النقص لا تقل عن المقاس في المستخلصات غير الملغاة.</item>
/// <item>البند الجديد: سطر عقد جديد في آخر الجدول، كميته الأصلية صفر وكميته المعدلة من الأمر، ويُعلَّم بالأمر (<see cref="CommercialContractItem.VariationId"/>).</item>
/// <item>الاعتماد يعيد التحقق من القاعدة، ويحدّث قيمة العقد وضريبتها والمتبقي والتكلفة التقديرية وتاريخ النهاية، ويثبّت ما قبلها وبعدها على الأمر.</item>
/// </list>
/// احتياجات المواد المعتمدة لا تتبع الأمر تلقائياً (المعتمدة ثابتة)؛ تُراجع بدورتها.
/// </summary>
public class ContractVariationService : IContractVariationService
{
    private const decimal Epsilon = 0.00005m;
    private readonly ErpDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly ICurrentUser _user;

    public ContractVariationService(ErpDbContext db, INumberSequenceService numbers, ICurrentUser user)
    {
        _db = db; _numbers = numbers; _user = user;
    }

    // ---------- القراءة ----------

    public async Task<ContractVariationSummaryDto> SummaryAsync(Guid contractId, CancellationToken ct = default)
    {
        var contract = await LoadContractAsync(contractId, track: false, ct);
        var list = await _db.Set<ContractVariation>().AsNoTracking().Include(v => v.Lines)
            .Where(v => v.ContractId == contractId).OrderByDescending(v => v.SequenceNo).ToListAsync(ct);
        var measured = await MeasuredQuantitiesAsync(contractId, ct);
        var approved = list.Where(v => v.Status == St.Approved).OrderBy(v => v.DecidedAt).ToList();
        var approvedChange = approved.Sum(v => v.ValueChange);
        return new ContractVariationSummaryDto
        {
            OriginalValue = contract.ContractValue - approvedChange,
            ApprovedChange = approvedChange,
            PendingChange = list.Where(v => St.IsOpen(v.Status)).Sum(v => v.ValueChange),
            CurrentValue = contract.ContractValue,
            OriginalEstimatedCost = contract.EstimatedCost - approved.Sum(v => v.CostChange),
            CurrentEstimatedCost = contract.EstimatedCost,
            OriginalEndDate = approved.Count > 0 ? approved[0].EndDateBefore : contract.EndDate,
            CurrentEndDate = contract.EndDate,
            ApprovedExtensionDays = approved.Sum(v => v.ExtensionDays),
            ApprovedCount = approved.Count,
            OpenCount = list.Count(v => St.IsOpen(v.Status)),
            RejectedCount = list.Count(v => v.Status == St.Rejected),
            Variations = list.Select(v => ToDto(v, contract, measured)).ToList(),
        };
    }

    public async Task<ContractVariationDto> GetAsync(Guid contractId, Guid variationId, CancellationToken ct = default)
    {
        var contract = await LoadContractAsync(contractId, track: false, ct);
        var v = await LoadVariationAsync(contractId, variationId, track: false, ct);
        return ToDto(v, contract, await MeasuredQuantitiesAsync(contractId, ct));
    }

    public async Task<ContractVariationDto> GetAsync(Guid variationId, CancellationToken ct = default)
        => await GetAsync(await ContractIdOfAsync(variationId, ct), variationId, ct);

    public async Task<Guid> ContractIdOfAsync(Guid variationId, CancellationToken ct = default)
        => await _db.Set<ContractVariation>().AsNoTracking().Where(v => v.Id == variationId).Select(v => (Guid?)v.ContractId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("أمر التغيير غير موجود.");

    // ---------- الإعداد (مسودة) ----------

    public Task<ContractVariationDto> CreateAsync(Guid contractId, SaveContractVariationDto request, CancellationToken ct = default)
        => new TransactionRunner(_db).RunAsync(async token =>
        {
            var contract = await LoadContractAsync(contractId, track: false, token);
            RequireActive(contract);
            var date = request.VariationDate?.Date ?? SaudiTime.Today;
            var v = new ContractVariation
            {
                ContractId = contractId, Status = St.Draft,
                SequenceNo = (await _db.Set<ContractVariation>().Where(x => x.ContractId == contractId).MaxAsync(x => (int?)x.SequenceNo, token) ?? 0) + 1,
                VariationNumber = await _numbers.AssignAsync(new DocumentNumberRequest
                {
                    Key = "contract_variation", DefaultPrefix = "VO-", WarehouseId = contract.WarehouseId, Date = date,
                    Exists = (n, c) => _db.Set<ContractVariation>().AnyAsync(x => x.VariationNumber == n, c),
                }, token),
            };
            Apply(v, request, contract, date);
            _db.Add(v);
            await _db.SaveChangesAsync(token);
            return ToDto(v, contract, await MeasuredQuantitiesAsync(contractId, token));
        }, ct);

    public Task<ContractVariationDto> UpdateAsync(Guid contractId, Guid variationId, SaveContractVariationDto request, CancellationToken ct = default)
        => new TransactionRunner(_db).RunAsync(async token =>
        {
            var contract = await LoadContractAsync(contractId, track: false, token);
            RequireActive(contract);
            var v = await LoadVariationAsync(contractId, variationId, track: true, token);
            RequireStatus(v, St.Draft);
            foreach (var old in v.Lines.ToList())
            {
                v.Lines.Remove(old);
                _db.Remove(old);
            }
            Apply(v, request, contract, request.VariationDate?.Date ?? v.VariationDate);
            await _db.SaveChangesAsync(token);
            return ToDto(v, contract, await MeasuredQuantitiesAsync(contractId, token));
        }, ct);

    /// <summary>حذف مسودة لم تُقدَّم قط؛ ما قُدِّم يُلغى بسبب ليبقى أثره.</summary>
    public Task DeleteAsync(Guid contractId, Guid variationId, CancellationToken ct = default)
        => new TransactionRunner(_db).RunAsync(async token =>
        {
            var v = await LoadVariationAsync(contractId, variationId, track: true, token);
            RequireStatus(v, St.Draft);
            if (v.ReturnNote != null) throw new ConflictException("أمر التغيير قُدِّم سابقاً: يُلغى بسبب ولا يُحذف.");
            _db.RemoveRange(v.Lines);
            _db.Remove(v);
            await _db.SaveChangesAsync(token);
        }, ct);

    // ---------- الدورة ----------

    public Task<ContractVariationDto> SubmitAsync(Guid contractId, Guid variationId, CancellationToken ct = default)
        => new TransactionRunner(_db).RunAsync(async token =>
        {
            var contract = await LoadContractAsync(contractId, track: false, token);
            RequireActive(contract);
            var v = await LoadVariationAsync(contractId, variationId, track: true, token);
            RequireStatus(v, St.Draft);
            var measured = await MeasuredQuantitiesAsync(contractId, token);
            Validate(v, contract, measured); // تنبيه مبكر؛ الاعتماد يعيد الفحص
            v.Status = St.Submitted;
            v.SubmittedAt = DateTime.UtcNow; v.SubmittedBy = _user.Name ?? "system";
            await _db.SaveChangesAsync(token);
            return ToDto(v, contract, measured);
        }, ct);

    public Task<ContractVariationDto> ApproveAsync(Guid variationId, CancellationToken ct = default)
        => new TransactionRunner(_db).RunAsync(async token =>
        {
            var contractId = await ContractIdOfAsync(variationId, token);
            var contract = await LoadContractAsync(contractId, track: true, token);
            RequireActive(contract);
            var v = await LoadVariationAsync(contractId, variationId, track: true, token);
            RequireStatus(v, St.Submitted);

            // الفحص من القاعدة لحظة الاعتماد (قد يكون اعتُمد أمر أو مستخلص آخر في الأثناء)
            var measured = await MeasuredQuantitiesAsync(contractId, token);
            Validate(v, contract, measured);
            var items = contract.Items.ToDictionary(i => i.Id);
            var created = new List<(ContractVariationLine Line, CommercialContractItem Item)>();
            var nextSort = contract.Items.Count == 0 ? 0 : contract.Items.Max(i => i.SortOrder);
            var touchedGroups = new HashSet<Guid>();

            foreach (var line in v.Lines.OrderBy(l => l.SortOrder))
            {
                if (line.ContractItemId is Guid itemId)
                {
                    var item = items[itemId];
                    Snapshot(line, item);
                    line.QuantityBefore = item.RevisedQuantity;
                    item.RevisedQuantity = Math.Round(item.RevisedQuantity + line.QuantityChange, 4);
                    line.QuantityAfter = item.RevisedQuantity;
                    item.TotalWithVat = LineTotal(item);
                    if (item.ParentItemId is Guid parent) touchedGroups.Add(parent);
                }
                else
                {
                    var item = new CommercialContractItem
                    {
                        ContractId = contract.Id, Description = line.Description, Unit = line.Unit, LineCode = line.LineCode,
                        SectionName = line.SectionName, LineKind = line.LineKind, Quantity = 0, RevisedQuantity = line.QuantityChange,
                        UnitPrice = line.UnitPrice, TenderUnitPrice = line.UnitPrice, EstimatedUnitCost = line.EstimatedUnitCost,
                        VatRate = contract.VatRate, SortOrder = ++nextSort, VariationId = v.Id,
                    };
                    item.TotalWithVat = LineTotal(item);
                    contract.Items.Add(item);
                    line.QuantityBefore = 0;
                    line.QuantityAfter = line.QuantityChange;
                    created.Add((line, item));
                }
            }
            foreach (var groupId in touchedGroups)
                if (items.TryGetValue(groupId, out var group))
                    group.TotalWithVat = contract.Items.Where(i => i.ParentItemId == groupId).Sum(i => i.TotalWithVat);

            Totals(v);
            v.ContractValueBefore = contract.ContractValue;
            v.EndDateBefore = contract.EndDate;
            contract.ContractValue = DocumentPricing.Round(contract.ContractValue + v.ValueChange);
            if (contract.ContractValue <= 0) throw new ValidationFailedException("قيمة العقد بعد أمر التغيير يجب أن تكون موجبة.");
            contract.EstimatedCost = DocumentPricing.Round(Math.Max(0, contract.EstimatedCost + v.CostChange));
            contract.VatAmount = DocumentPricing.Round(contract.ContractValue * contract.VatRate / 100m);
            contract.TotalValueWithVat = contract.ContractValue + contract.VatAmount;
            contract.RemainingBalance = contract.TotalValueWithVat - contract.TotalInvoiced;
            if (v.ExtensionDays != 0 && contract.EndDate is DateTime end)
            {
                contract.EndDate = end.AddDays(v.ExtensionDays);
                if (contract.StartDate is DateTime start && contract.EndDate < start)
                    throw new ValidationFailedException("تقليص المدة يجعل نهاية العقد قبل بدايته.");
            }
            v.ContractValueAfter = contract.ContractValue;
            v.EndDateAfter = contract.EndDate;

            v.Status = St.Approved;
            v.DecidedAt = DateTime.UtcNow; v.DecidedBy = _user.Name ?? "system"; v.DecidedByUserId = _user.UserId;
            await _db.SaveChangesAsync(token);
            // معرّف البند الجديد يُولَّد عند الحفظ؛ يُثبَّت على سطر الأمر بعده في المعاملة نفسها
            if (created.Count > 0)
            {
                foreach (var (line, item) in created) line.CreatedItemId = item.Id;
                await _db.SaveChangesAsync(token);
            }
            return ToDto(v, contract, measured);
        }, ct);

    public Task<ContractVariationDto> ReturnAsync(Guid contractId, Guid variationId, ContractVariationActionDto request, CancellationToken ct = default)
        => Decide(contractId, variationId, request, St.Submitted, v => { v.Status = St.Draft; v.SubmittedAt = null; v.SubmittedBy = null; }, ct);

    public Task<ContractVariationDto> RejectAsync(Guid contractId, Guid variationId, ContractVariationActionDto request, CancellationToken ct = default)
        => Decide(contractId, variationId, request, St.Submitted, v =>
        {
            v.Status = St.Rejected;
            v.DecidedAt = DateTime.UtcNow; v.DecidedBy = _user.Name ?? "system"; v.DecidedByUserId = _user.UserId;
        }, ct);

    public Task<ContractVariationDto> CancelAsync(Guid contractId, Guid variationId, ContractVariationActionDto request, CancellationToken ct = default)
        => Decide(contractId, variationId, request, null, v => v.Status = St.Cancelled, ct);

    private Task<ContractVariationDto> Decide(Guid contractId, Guid variationId, ContractVariationActionDto request, string? requiredStatus,
        Action<ContractVariation> apply, CancellationToken ct)
        => new TransactionRunner(_db).RunAsync(async token =>
        {
            var note = string.IsNullOrWhiteSpace(request.Note) ? throw new ValidationFailedException("السبب مطلوب.") : request.Note.Trim();
            var contract = await LoadContractAsync(contractId, track: false, token);
            var v = await LoadVariationAsync(contractId, variationId, track: true, token);
            if (requiredStatus != null) RequireStatus(v, requiredStatus);
            else if (!St.IsOpen(v.Status)) throw new ConflictException(StatusMessage(v.Status));
            apply(v);
            v.ReturnNote = note;
            await _db.SaveChangesAsync(token);
            return ToDto(v, contract, await MeasuredQuantitiesAsync(contractId, token));
        }, ct);

    // ---------- البناء والتحقق ----------

    /// <summary>رأس الأمر وأسطره من الطلب (تحل محل السابقة) مع التحقق من البنود ولقطة البند القائم.</summary>
    private void Apply(ContractVariation v, SaveContractVariationDto r, CommercialContract contract, DateTime date)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(r.Title)) errors.Add("عنوان أمر التغيير مطلوب.");
        if (r.ExtensionDays is < -3650 or > 3650) errors.Add("أيام التمديد خارج النطاق.");
        if (r.Lines.Count == 0 && r.ExtensionDays == 0) errors.Add("أمر التغيير بلا بنود ولا تمديد مدة.");
        var items = contract.Items.ToDictionary(i => i.Id);
        var existing = r.Lines.Where(l => l.ContractItemId.HasValue).ToList();
        if (existing.GroupBy(l => l.ContractItemId).Any(g => g.Count() > 1)) errors.Add("البند مكرر في أمر التغيير.");
        foreach (var l in existing)
        {
            if (!items.TryGetValue(l.ContractItemId!.Value, out var item)) { errors.Add("بند غير موجود في جدول كميات العقد."); continue; }
            if (item.IsGroup) errors.Add($"البند «{Label(item)}» مجموعة: التغيير على بنوده الفرعية.");
            if (l.QuantityChange == 0) errors.Add($"فرق كمية البند «{Label(item)}» صفر.");
        }
        foreach (var l in r.Lines.Where(l => !l.ContractItemId.HasValue))
        {
            if (string.IsNullOrWhiteSpace(l.Description) || string.IsNullOrWhiteSpace(l.Unit)) errors.Add("البند الجديد يحتاج وصفاً ووحدة.");
            if (l.QuantityChange <= 0) errors.Add("كمية البند الجديد يجب أن تكون موجبة.");
            if (l.UnitPrice < 0 || l.EstimatedUnitCost < 0) errors.Add("سعر البند الجديد وتكلفته لا يكونان سالبين.");
            if (l.LineKind != null && !ContractBoqLineKinds.IsValid(l.LineKind)) errors.Add("نوع البند غير صحيح.");
        }
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        v.VariationDate = date;
        v.Title = r.Title.Trim();
        v.Reason = Clean(r.Reason);
        v.ClientReference = Clean(r.ClientReference);
        v.Notes = Clean(r.Notes);
        v.ExtensionDays = r.ExtensionDays;
        var order = 0;
        foreach (var l in r.Lines)
        {
            var line = new ContractVariationLine
            {
                VariationId = v.Id, ContractItemId = l.ContractItemId, QuantityChange = Math.Round(l.QuantityChange, 4),
                Notes = Clean(l.Notes), SortOrder = ++order,
            };
            if (l.ContractItemId is Guid id) Snapshot(line, items[id]);
            else
            {
                line.Description = l.Description!.Trim(); line.Unit = l.Unit!.Trim();
                line.LineCode = Clean(l.LineCode); line.SectionName = Clean(l.SectionName);
                line.LineKind = l.LineKind ?? ContractBoqLineKinds.Measured;
                line.UnitPrice = Math.Round(l.UnitPrice, 4); line.EstimatedUnitCost = Math.Round(l.EstimatedUnitCost, 4);
            }
            v.Lines.Add(line);
        }
        Totals(v);
    }

    /// <summary>
    /// الكمية بعد الأمر لكل بند قائم: لا سالبة، ولا أقل من المقاس في المستخلصات غير الملغاة (المعتمدة والمفتوحة).
    /// </summary>
    private static void Validate(ContractVariation v, CommercialContract contract, Dictionary<Guid, decimal> measured)
    {
        var errors = new List<string>();
        var items = contract.Items.ToDictionary(i => i.Id);
        foreach (var line in v.Lines.Where(l => l.ContractItemId.HasValue))
        {
            if (!items.TryGetValue(line.ContractItemId!.Value, out var item)) { errors.Add("بند الأمر لم يعد في جدول كميات العقد."); continue; }
            if (item.IsGroup) { errors.Add($"البند «{Label(item)}» مجموعة: التغيير على بنوده الفرعية."); continue; }
            var after = item.RevisedQuantity + line.QuantityChange;
            var done = measured.GetValueOrDefault(item.Id);
            if (after < -Epsilon) errors.Add($"الكمية المعدلة للبند «{Label(item)}» تصير سالبة.");
            else if (after < done - Epsilon)
                errors.Add($"الكمية المعدلة للبند «{Label(item)}» ({after:0.####}) أقل من المقاس في المستخلصات ({done:0.####}).");
        }
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
    }

    /// <summary>وصف البند القائم وسعره وتكلفته الحالية على سطر الأمر (يُعاد عند الاعتماد).</summary>
    private static void Snapshot(ContractVariationLine line, CommercialContractItem item)
    {
        line.Description = item.Description; line.Unit = item.Unit; line.LineCode = item.LineCode; line.SectionName = item.SectionName;
        line.LineKind = item.LineKind; line.UnitPrice = item.UnitPrice; line.EstimatedUnitCost = item.EstimatedUnitCost;
    }

    private static void Totals(ContractVariation v)
    {
        foreach (var l in v.Lines)
        {
            l.Amount = DocumentPricing.Round(l.QuantityChange * l.UnitPrice);
            l.CostAmount = DocumentPricing.Round(l.QuantityChange * l.EstimatedUnitCost);
        }
        v.ValueChange = v.Lines.Sum(l => l.Amount);
        v.CostChange = v.Lines.Sum(l => l.CostAmount);
    }

    private static decimal LineTotal(CommercialContractItem i) => DocumentPricing.Round(i.RevisedQuantity * i.UnitPrice * (1 + i.VatRate / 100m));

    /// <summary>المقاس لكل بند في المستخلصات غير الملغاة (المعتمدة والمفوترة والمفتوحة).</summary>
    private Task<Dictionary<Guid, decimal>> MeasuredQuantitiesAsync(Guid contractId, CancellationToken ct)
        => _db.Set<ContractCertificateLine>().AsNoTracking()
            .Where(l => l.Certificate.ContractId == contractId && l.Certificate.Status != CertSt.Cancelled)
            .GroupBy(l => l.ContractItemId)
            .Select(g => new { g.Key, Qty = g.Sum(l => l.CurrentQuantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);

    // ---------- التحميل ----------

    private async Task<CommercialContract> LoadContractAsync(Guid contractId, bool track, CancellationToken ct)
    {
        var q = _db.Set<CommercialContract>().Include(c => c.Items);
        var contract = await (track ? q : q.AsNoTracking()).FirstOrDefaultAsync(c => c.Id == contractId, ct)
            ?? throw new NotFoundException(Messages.ContractNotFound);
        if (!await CommercialContractService.IsBoqContractAsync(_db, contract.ContractType, ct))
            throw new ConflictException("أوامر التغيير لعقود جدول الكميات وحدها.");
        return contract;
    }

    private async Task<ContractVariation> LoadVariationAsync(Guid contractId, Guid variationId, bool track, CancellationToken ct)
    {
        var q = _db.Set<ContractVariation>().Include(v => v.Lines);
        return await (track ? q : q.AsNoTracking()).FirstOrDefaultAsync(v => v.Id == variationId && v.ContractId == contractId, ct)
            ?? throw new NotFoundException("أمر التغيير غير موجود.");
    }

    private static void RequireActive(CommercialContract c)
    {
        if (c.Stage != ContractStageDefinition.Active)
            throw new ConflictException("أوامر التغيير على العقد الساري وحده.");
    }

    private static void RequireStatus(ContractVariation v, string status)
    {
        if (v.Status != status) throw new ConflictException(StatusMessage(v.Status));
    }

    private static string StatusMessage(string status) => status switch
    {
        St.Draft => "أمر التغيير مسودة.",
        St.Submitted => "أمر التغيير مقدَّم للاعتماد.",
        St.Approved => "أمر التغيير معتمد: لا يُعدَّل.",
        St.Rejected => "أمر التغيير مرفوض.",
        _ => "أمر التغيير ملغى.",
    };

    // ---------- العرض ----------

    private static ContractVariationDto ToDto(ContractVariation v, CommercialContract contract, Dictionary<Guid, decimal> measured)
    {
        var items = contract.Items.ToDictionary(i => i.Id);
        var open = St.IsOpen(v.Status);
        return new ContractVariationDto
        {
            Id = v.Id, ContractId = v.ContractId, SequenceNo = v.SequenceNo, VariationNumber = v.VariationNumber, VariationDate = v.VariationDate,
            Title = v.Title, Reason = v.Reason, ClientReference = v.ClientReference, ExtensionDays = v.ExtensionDays,
            ValueChange = v.ValueChange, CostChange = v.CostChange, VatChange = DocumentPricing.Round(v.ValueChange * contract.VatRate / 100m),
            Status = v.Status, ReturnNote = v.ReturnNote, Notes = v.Notes, SubmittedAt = v.SubmittedAt, SubmittedBy = v.SubmittedBy,
            DecidedAt = v.DecidedAt, DecidedBy = v.DecidedBy, ContractValueBefore = v.ContractValueBefore, ContractValueAfter = v.ContractValueAfter,
            EndDateBefore = v.EndDateBefore, EndDateAfter = v.EndDateAfter, CreatedAt = v.CreatedAt,
            Lines = v.Lines.OrderBy(l => l.SortOrder).Select(l =>
            {
                // المفتوح يعرض الأثر المتوقع على الكمية الحالية؛ المعتمد ما ثُبِّت لحظة اعتماده
                var item = l.ContractItemId is Guid id ? items.GetValueOrDefault(id) : null;
                var before = open ? item?.RevisedQuantity ?? 0 : l.QuantityBefore;
                return new ContractVariationLineDto
                {
                    Id = l.Id, ContractItemId = l.ContractItemId, CreatedItemId = l.CreatedItemId, IsNew = l.ContractItemId == null,
                    Description = l.Description, Unit = l.Unit, LineCode = l.LineCode, SectionName = l.SectionName, LineKind = l.LineKind,
                    QuantityChange = l.QuantityChange, UnitPrice = l.UnitPrice, EstimatedUnitCost = l.EstimatedUnitCost,
                    Amount = l.Amount, CostAmount = l.CostAmount,
                    QuantityBefore = before, QuantityAfter = open ? before + l.QuantityChange : l.QuantityAfter,
                    CertifiedQuantity = measured.GetValueOrDefault(l.ContractItemId ?? l.CreatedItemId ?? Guid.Empty),
                    SortOrder = l.SortOrder, Notes = l.Notes,
                };
            }).ToList(),
        };
    }

    private static string Label(CommercialContractItem i) => string.IsNullOrWhiteSpace(i.LineCode) ? i.Description : $"{i.LineCode} {i.Description}";
    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
