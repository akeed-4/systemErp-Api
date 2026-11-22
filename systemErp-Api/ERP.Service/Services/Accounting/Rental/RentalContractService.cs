using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using BSt = ERP.Core.Models.Accounting.RentalBookingStatuses;
using LSt = ERP.Core.Models.Accounting.RentalLineStatuses;
using St = ERP.Core.Models.Accounting.RentalContractStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// عقود تأجير المعدات للعملاء (D8). قواعد:
/// <list type="bullet">
/// <item>كل سطر معدة قابلة للتأجير بفترة وأساس؛ سعره وشروطه لقطة من القائمة السارية في بداية فترته (<see cref="RentalPricing"/>)، والسعر
/// اليدوي يتطلب «خصم يدوي» على شاشة العقود. المشغّل موظف نشط، وسعر المشغّل من القائمة.</item>
/// <item>المسودة والساري يحجزان فترات أسطرهما: الحفظ والاعتماد والتمديد تقفل صفوف المعدات ثم ترفض أي تداخل (حجز آخر، عقد آخر،
/// إسناد مقاولات، صيانة، حالة، خارج عقد المورد) أو تداخل سطرين للمعدة نفسها.</item>
/// <item>التعديل للمسودة؛ الساري يُمدَّد (نهاية جديدة بسبب) ولا يُعدَّل. الإلغاء بسبب ما لم تُسلَّم معدة؛ الحذف للمسودة.</item>
/// </list>
/// </summary>
public class RentalContractService : IRentalContractService
{
    private const int MaxLines = 100;
    private const string Screen = PermissionCatalog.RentalContractsScreenId;

    private readonly ErpDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;
    private readonly IAuditService _audit;

    public RentalContractService(ErpDbContext db, INumberSequenceService numbers, ITransactionRunner tx, ICurrentUser user, IPermissionService permissions,
        IAuditService audit)
    {
        _db = db; _numbers = numbers; _tx = tx; _user = user; _permissions = permissions; _audit = audit;
    }

    public async Task<List<RentalContractDto>> ListAsync(string? status, Guid? customerId, CancellationToken ct = default)
    {
        var q = _db.Set<RentalContract>().AsNoTracking().Include(c => c.Lines).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) { var s = status.Trim().ToLowerInvariant(); q = q.Where(c => c.Status == s); }
        if (customerId is Guid cid) q = q.Where(c => c.CustomerId == cid);
        return await DtosAsync(await q.OrderByDescending(c => c.CreatedAt).Take(1000).ToListAsync(ct), withChanges: false, ct);
    }

    public async Task<RentalContractDto> GetAsync(Guid id, CancellationToken ct = default)
        => (await DtosAsync(new List<RentalContract> { await FindAsync(id, track: false, ct) }, withChanges: true, ct)).Single();

    public Task<RentalContractDto> CreateAsync(SaveRentalContractDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var c = await NewAsync(request, trustRates: false, excludeBookingId: null, token);
            await LogAsync("RENTAL_CONTRACT_CREATED", c, null, token);
            return await GetAsync(c.Id, token);
        }, ct);

    public Task<RentalContractDto> ConvertFromBookingAsync(Guid bookingId, string term, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var b = await _db.Set<RentalBooking>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == bookingId, token)
                ?? throw new NotFoundException(Messages.RentalBookingNotFound);
            var holding = b.Status == BSt.Confirmed || (b.Status == BSt.Held && b.HoldUntil > SaudiTime.Now);
            if (!holding) throw new ConflictException(Messages.RentalBookingNotConvertible);
            if (b.Lines.Any(l => l.EquipmentId == null)) throw new ValidationFailedException(Messages.RentalBookingNeedsEquipment);

            var request = new SaveRentalContractDto
            {
                CustomerId = b.CustomerId, WarehouseId = b.WarehouseId, Term = term, Notes = b.Notes,
                Lines = b.Lines.OrderBy(l => l.SortOrder).Select(l => new SaveRentalContractLineDto
                {
                    EquipmentId = l.EquipmentId!.Value, From = l.From, To = l.To, Basis = l.Basis, WithOperator = l.WithOperator, Rate = l.Rate, Notes = l.Notes,
                }).ToList(),
            };
            // أسعار لقطة الحجز تُحترم بلا صلاحية الخصم، والحجز نفسه لا يُحتسب تعارضاً
            var c = await NewAsync(request, trustRates: true, excludeBookingId: b.Id, token);
            c.BookingId = b.Id;
            b.Status = BSt.Converted; b.ContractId = c.Id;
            await _db.SaveChangesAsync(token);
            await LogAsync("RENTAL_CONTRACT_CREATED", c, $"من الحجز {b.BookingNumber}", token);
            return await GetAsync(c.Id, token);
        }, ct);

    public Task<RentalContractDto> UpdateAsync(Guid id, SaveRentalContractDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var c = await FindAsync(id, track: true, token);
            if (c.Status != St.Draft) throw new ConflictException(Messages.RentalContractNotEditable);
            await ApplyAsync(c, request, trustRates: false, token);
            await EnsureAvailableAsync(c, null, token);
            await _db.SaveChangesAsync(token);
            await LogAsync("RENTAL_CONTRACT_UPDATED", c, null, token);
            return await GetAsync(id, token);
        }, ct);

    public Task<RentalContractDto> ActivateAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var c = await FindAsync(id, track: true, token);
            if (c.Status != St.Draft) throw new ConflictException(Messages.RentalContractNotEditable);
            await EnsureCustomerAsync(c.CustomerId, token);
            await EnsureAvailableAsync(c, null, token);
            c.Status = St.Active; c.ActivatedAt = DateTime.UtcNow; c.ActivatedBy = _user.Name ?? _user.Email;
            await _db.SaveChangesAsync(token);
            await LogAsync("RENTAL_CONTRACT_ACTIVATED", c, null, token);
            return await GetAsync(id, token);
        }, ct);

    public Task<RentalContractDto> ExtendAsync(Guid id, ExtendRentalContractDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var c = await FindAsync(id, track: true, token);
            if (c.Status is not (St.Draft or St.Active)) throw new ConflictException(Messages.RentalContractNotEditable);
            var reason = request.Reason?.Trim();
            if (string.IsNullOrEmpty(reason)) throw new ValidationFailedException(Messages.RentalExtendReasonRequired);
            var newEnd = Local(request.NewEnd);
            var lines = c.Lines.Where(l => (l.Status is LSt.Pending or LSt.Out)
                && (request.LineIds == null || request.LineIds.Count == 0 || request.LineIds.Contains(l.Id))).ToList();
            if (lines.Count == 0) throw new ValidationFailedException(Messages.RentalExtendNoLines);
            if (lines.Any(l => newEnd <= l.PlannedTo)) throw new ValidationFailedException(Messages.RentalExtendMustBeLater);

            // الفترة الإضافية وحدها تُفحص (الفترة القائمة محجوزة لهذا العقد)
            var ids = lines.Select(l => l.EquipmentId).Distinct().OrderBy(x => x).ToList();
            await RentalAvailability.LockAsync(_db, ids, token);
            var problems = new List<string>();
            foreach (var l in lines)
                foreach (var b in await RentalAvailability.BlocksAsync(_db, new[] { l.EquipmentId }, l.PlannedTo, newEnd, null, SaudiTime.Now, token,
                             excludeContractId: c.Id, parentContractId: c.ParentContractId))
                    problems.Add(await UnavailableMessageAsync(l.EquipmentId, b, token));
            if (problems.Count > 0) throw new ConflictException(string.Join("\n", problems));

            foreach (var l in lines)
            {
                _db.Add(new RentalContractChange
                {
                    ContractId = c.Id, Kind = RentalContractChangeKinds.Extension, LineId = l.Id, OldEnd = l.PlannedTo, NewEnd = newEnd,
                    Reason = reason.Length > 500 ? reason[..500] : reason, CreatedBy = _user.Name ?? _user.Email,
                });
                l.PlannedTo = newEnd;
                l.EstimatedAmount = await EstimateAsync(l, token);
            }
            c.EndDate = c.Lines.Where(l => l.Status != LSt.Cancelled).Max(l => l.PlannedTo);
            await _db.SaveChangesAsync(token);
            await LogAsync("RENTAL_CONTRACT_EXTENDED", c, $"حتى {newEnd:yyyy-MM-dd HH:mm} • {reason}", token);
            return await GetAsync(id, token);
        }, ct);

    public Task<RentalContractDto> RenewAsync(Guid id, RenewRentalContractDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var parent = await FindAsync(id, track: true, token);
            if (parent.Status != St.Active) throw new ConflictException(Messages.RentalRenewActiveOnly);
            var reason = request.Reason?.Trim();
            if (string.IsNullOrEmpty(reason)) throw new ValidationFailedException(Messages.RentalRenewReasonRequired);
            var existing = await _db.Set<RentalContract>().AsNoTracking()
                .Where(x => x.ParentContractId == id && (x.Status == St.Draft || x.Status == St.Active)).Select(x => x.ContractNumber).FirstOrDefaultAsync(token);
            if (existing != null) throw new ConflictException(string.Format(Messages.RentalContractAlreadyRenewed, existing));
            var newEnd = Local(request.NewEnd);
            var lines = parent.Lines.Where(l => (l.Status is LSt.Pending or LSt.Out)
                && (request.LineIds == null || request.LineIds.Count == 0 || request.LineIds.Contains(l.Id))).OrderBy(l => l.SortOrder).ToList();
            if (lines.Count == 0) throw new ValidationFailedException(Messages.RentalExtendNoLines);
            if (lines.Any(l => newEnd <= l.PlannedTo)) throw new ValidationFailedException(Messages.RentalExtendMustBeLater);

            // العقد اللاحق يبدأ لكل معدة من نهاية سطرها في السابق، بشروط فوترة السابق ما لم تتغير المدة. التأمين يبقى على السابق.
            var term = string.IsNullOrWhiteSpace(request.Term) ? parent.Term : request.Term;
            var sameTerm = string.Equals(term.Trim(), parent.Term, StringComparison.OrdinalIgnoreCase);
            var save = new SaveRentalContractDto
            {
                CustomerId = parent.CustomerId, WarehouseId = parent.WarehouseId, Term = term,
                BillingCycle = sameTerm ? parent.BillingCycle : null, BillInAdvance = sameTerm ? parent.BillInAdvance : null,
                Terms = parent.Terms, Notes = parent.Notes,
                Lines = lines.Select(l => new SaveRentalContractLineDto
                {
                    EquipmentId = l.EquipmentId, From = l.PlannedTo, To = newEnd, Basis = l.Basis, WithOperator = l.WithOperator,
                    OperatorEmployeeId = l.OperatorEmployeeId, Rate = request.KeepRates ? l.Rate : null, Notes = l.Notes,
                }).ToList(),
            };
            var c = await NewAsync(save, trustRates: false, excludeBookingId: null, token, parentContractId: parent.Id);
            var by = _user.Name ?? _user.Email;
            var why = reason.Length > 500 ? reason[..500] : reason;
            foreach (var l in lines)
                _db.Add(new RentalContractChange
                {
                    ContractId = parent.Id, Kind = RentalContractChangeKinds.Renewal, LineId = l.Id, OldEnd = l.PlannedTo, NewEnd = newEnd,
                    NewContractId = c.Id, Reason = why, CreatedBy = by,
                });
            await _db.SaveChangesAsync(token);
            await LogAsync("RENTAL_CONTRACT_RENEWED", parent, $"{c.ContractNumber} حتى {newEnd:yyyy-MM-dd HH:mm} • {why}", token);
            await LogAsync("RENTAL_CONTRACT_CREATED", c, $"تجديد {parent.ContractNumber}", token);
            return await GetAsync(c.Id, token);
        }, ct);

    public Task<RentalContractDto> AssignOperatorAsync(Guid id, Guid lineId, AssignRentalOperatorDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var c = await FindAsync(id, track: true, token);
            if (c.Status is not (St.Draft or St.Active)) throw new ConflictException(Messages.RentalContractNotEditable);
            var line = c.Lines.FirstOrDefault(l => l.Id == lineId) ?? throw new NotFoundException(Messages.RentalHandoverLineNotFound);
            if (!line.WithOperator) throw new ValidationFailedException(Messages.RentalLineWithoutOperator);
            if (line.Status is not (LSt.Pending or LSt.Out)) throw new ConflictException(Messages.RentalOperatorLineClosed);
            if (line.OperatorEmployeeId == request.OperatorEmployeeId) return await GetAsync(id, token);
            var reason = request.Reason?.Trim();
            if (line.Status == LSt.Out && string.IsNullOrEmpty(reason)) throw new ValidationFailedException(Messages.RentalOperatorReasonRequired);

            string? newName = null;
            if (request.OperatorEmployeeId is Guid op)
            {
                newName = await _db.Set<Employee>().Where(e => e.Id == op && e.Status == EmployeeStatuses.Active).Select(e => e.NameAr).FirstOrDefaultAsync(token)
                    ?? throw new ValidationFailedException(Messages.RentalOperatorNotFound);
                // المشغّل يُقفل بقفل معدة السطر حتى لا يُعيَّن في سطرين متزامنين
                await RentalAvailability.LockAsync(_db, new List<Guid> { line.EquipmentId }, token);
                var from = line.Status == LSt.Out ? SaudiTime.Now : line.PlannedFrom;
                if (line.PlannedTo > from && await RentalOperators.ConflictAsync(_db, op, from, line.PlannedTo, line.Id, null, token) is string busy)
                    throw new ConflictException(string.Format(Messages.RentalOperatorBusy, busy));
            }
            var oldName = line.OperatorEmployeeId is Guid old
                ? await _db.Set<Employee>().Where(e => e.Id == old).Select(e => e.NameAr).FirstOrDefaultAsync(token) : null;
            line.OperatorEmployeeId = request.OperatorEmployeeId;
            var note = $"{oldName ?? "—"} ← {newName ?? "—"}{(string.IsNullOrEmpty(reason) ? string.Empty : " • " + reason)}";
            _db.Add(new RentalContractChange
            {
                ContractId = c.Id, Kind = RentalContractChangeKinds.Operator, LineId = line.Id, Reason = note.Length > 500 ? note[..500] : note,
                CreatedBy = _user.Name ?? _user.Email,
            });
            await _db.SaveChangesAsync(token);
            await LogAsync("RENTAL_OPERATOR_ASSIGNED", c, note, token);
            return await GetAsync(id, token);
        }, ct);

    public async Task<RentalContractDto> CancelAsync(Guid id, RentalReasonDto request, CancellationToken ct = default)
    {
        var c = await FindAsync(id, track: true, ct);
        if (c.Status is not (St.Draft or St.Active)) throw new ConflictException(Messages.RentalContractNotEditable);
        if (c.Lines.Any(l => l.Status is LSt.Out or LSt.Returned)) throw new ConflictException(Messages.RentalContractHasHandovers);
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason)) throw new ValidationFailedException(Messages.RentalCancelReasonRequired);
        c.Status = St.Cancelled; c.CancelReason = reason.Length > 500 ? reason[..500] : reason;
        foreach (var l in c.Lines) l.Status = LSt.Cancelled;
        await _db.SaveChangesAsync(ct);
        await LogAsync("RENTAL_CONTRACT_CANCELLED", c, c.CancelReason, ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var c = await FindAsync(id, track: true, ct);
        if (c.Status != St.Draft) throw new ConflictException(Messages.RentalContractDeleteDraftOnly);
        // الحجز المحوَّل منه يعود مؤكداً (فتراته محجوزة كما كانت قبل التحويل)
        if (c.BookingId is Guid bid && await _db.Set<RentalBooking>().FirstOrDefaultAsync(b => b.Id == bid, ct) is { } booking)
        {
            booking.Status = BSt.Confirmed; booking.ContractId = null; booking.HoldUntil = null;
        }
        _db.RemoveRange(await _db.Set<RentalContractChange>().Where(x => x.ContractId == id || x.NewContractId == id).ToListAsync(ct));
        await DocumentAttachmentService.RemoveForOwnersAsync(_db, RentalAttachmentOwnerTypes.Contract, new[] { id }, ct);
        _db.RemoveRange(c.Lines);
        _db.Remove(c);
        await _db.SaveChangesAsync(ct);
        await LogAsync("RENTAL_CONTRACT_DELETED", c, null, ct);
    }

    // ---------- البناء والتحقق ----------

    private async Task<RentalContract> NewAsync(SaveRentalContractDto request, bool trustRates, Guid? excludeBookingId, CancellationToken ct,
        Guid? parentContractId = null)
    {
        var c = new RentalContract { Status = St.Draft, ParentContractId = parentContractId, CreatedBy = _user.Name ?? _user.Email, CreatedByUserId = _user.UserId };
        await ApplyAsync(c, request, trustRates, ct);
        await EnsureAvailableAsync(c, excludeBookingId, ct);
        c.ContractNumber = await _numbers.AssignAsync(new DocumentNumberRequest
        {
            Key = "rental_contract", DefaultPrefix = "RC-", WarehouseId = c.WarehouseId, Date = SaudiTime.Today,
            Exists = (n, token) => _db.Set<RentalContract>().AnyAsync(x => x.ContractNumber == n, token),
        }, ct);
        _db.Add(c);
        await _db.SaveChangesAsync(ct);
        return c;
    }

    private async Task ApplyAsync(RentalContract c, SaveRentalContractDto r, bool trustRates, CancellationToken ct)
    {
        await EnsureCustomerAsync(r.CustomerId, ct);
        if (r.WarehouseId is Guid wh && !await _db.Set<Warehouse>().AnyAsync(w => w.Id == wh, ct)) throw new ValidationFailedException(Messages.RentalWarehouseNotFound);
        var term = r.Term?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!RentalTerms.IsValid(term)) throw new ValidationFailedException(Messages.RentalTermInvalid);
        var cycle = string.IsNullOrWhiteSpace(r.BillingCycle)
            ? (term == RentalTerms.Long ? RentalBillingCycles.Monthly : RentalBillingCycles.EndOfContract)
            : r.BillingCycle.Trim().ToLowerInvariant();
        if (!RentalBillingCycles.IsValid(cycle)) throw new ValidationFailedException(Messages.RentalBillingCycleInvalid);
        if (r.DepositRequired < 0) throw new ValidationFailedException(Messages.RentalDepositNegative);
        if (r.Lines.Count == 0) throw new ValidationFailedException(Messages.RentalBookingLinesRequired);
        if (r.Lines.Count > MaxLines) throw new ValidationFailedException(string.Format(Messages.RentalBookingTooManyLines, MaxLines));

        var canOverride = trustRates || await _permissions.HasPermissionAsync(Screen, ScreenAction.Discount, ct);
        var lines = new List<RentalContractLine>();
        var order = 0;
        foreach (var l in r.Lines)
        {
            var from = Local(l.From); var to = Local(l.To);
            var basis = l.Basis?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!RentalRateBases.IsValid(basis)) throw new ValidationFailedException(Messages.RentalBasisInvalid);
            if (to <= from) throw new ValidationFailedException(Messages.RentalPeriodInvalid);
            await EnsureRentableAsync(l.EquipmentId, ct);
            if (l.WithOperator && l.OperatorEmployeeId is Guid op)
            {
                if (!await _db.Set<Employee>().AnyAsync(e => e.Id == op && e.Status == EmployeeStatuses.Active, ct))
                    throw new ValidationFailedException(Messages.RentalOperatorNotFound);
                // تعارض مع سطر آخر في هذا العقد، ثم مع العقود الأخرى وإسنادات المقاولات
                if (lines.Any(x => x.OperatorEmployeeId == op && x.PlannedFrom < to && x.PlannedTo > from))
                    throw new ConflictException(Messages.RentalOperatorTwiceInContract);
                if (await RentalOperators.ConflictAsync(_db, op, from, to, null, c.Id == Guid.Empty ? null : c.Id, ct) is string busy)
                    throw new ConflictException(string.Format(Messages.RentalOperatorBusy, busy));
            }

            var rates = await RentalPricing.RatesAtAsync(_db, l.EquipmentId, null, from, ct);
            if (!rates.TryGetValue(basis, out var main))
                throw new ValidationFailedException(string.Format(Messages.RentalRateMissing, RentalPricing.BasisLabel(basis)));
            var overridden = l.Rate is decimal manual && manual != main.Rate;
            if (overridden && !canOverride) throw new ForbiddenException(Messages.RentalRateOverrideForbidden);
            if (l.Rate is <= 0) throw new ValidationFailedException(Messages.RentalRateMustBePositive);
            var rate = l.Rate ?? main.Rate;
            if (l.WithOperator && main.OperatorRate == null)
                throw new ValidationFailedException(string.Format(Messages.RentalOperatorRateMissing, RentalPricing.BasisLabel(basis)));

            var line = new RentalContractLine
            {
                EquipmentId = l.EquipmentId, Status = LSt.Pending, PlannedFrom = from, PlannedTo = to, Basis = basis, Rate = decimal.Round(rate, 4),
                RateOverridden = overridden, WithOperator = l.WithOperator, OperatorRate = l.WithOperator ? main.OperatorRate ?? 0 : 0,
                OperatorEmployeeId = l.WithOperator ? l.OperatorEmployeeId : null, IncludedHours = main.IncludedHours, OvertimeRate = main.OvertimeRate,
                MinimumPeriods = main.MinimumPeriods, SortOrder = ++order, Notes = Trim(l.Notes, 500),
            };
            line.EstimatedAmount = Estimate(line, rates);
            lines.Add(line);
        }

        c.CustomerId = r.CustomerId; c.WarehouseId = r.WarehouseId; c.Term = term; c.BillingCycle = cycle;
        c.BillInAdvance = r.BillInAdvance ?? term == RentalTerms.Long;
        c.DepositRequired = decimal.Round(r.DepositRequired, 2); c.Terms = Trim(r.Terms, 4000); c.Notes = Trim(r.Notes, 1000);
        if (c.Lines.Count > 0) _db.RemoveRange(c.Lines.ToList());
        c.Lines.Clear();
        foreach (var line in lines) c.Lines.Add(line);
        c.StartDate = lines.Min(l => l.PlannedFrom); c.EndDate = lines.Max(l => l.PlannedTo);
    }

    /// <summary>تقدير السطر بأسعاره المثبّتة: أساسه بسعره (اليدوي إن وُجد)، والكسور بأسعار القائمة الأصغر.</summary>
    private static decimal Estimate(RentalContractLine l, Dictionary<string, RentalPricedBasis> rates)
    {
        var priced = new Dictionary<string, RentalPricedBasis>(rates)
        {
            [l.Basis] = new(l.Basis, l.Rate, l.WithOperator ? l.OperatorRate : rates.GetValueOrDefault(l.Basis)?.OperatorRate, l.IncludedHours, l.OvertimeRate, l.MinimumPeriods),
        };
        return RentalPricing.Estimate(priced, l.Basis, l.PlannedFrom, l.PlannedTo, l.WithOperator).Total;
    }

    private async Task<decimal> EstimateAsync(RentalContractLine l, CancellationToken ct)
        => Estimate(l, await RentalPricing.RatesAtAsync(_db, l.EquipmentId, null, l.PlannedFrom, ct));

    /// <summary>يقفل صفوف معدات الأسطر ويرفض تداخلها مع غيرها أو فيما بينها (العقد نفسه وحجزه المحوَّل مستثنيان).</summary>
    private async Task EnsureAvailableAsync(RentalContract c, Guid? excludeBookingId, CancellationToken ct)
    {
        var active = c.Lines.Where(l => l.Status is LSt.Pending or LSt.Out).ToList();
        foreach (var group in active.GroupBy(l => l.EquipmentId))
        {
            var ordered = group.OrderBy(l => l.PlannedFrom).ToList();
            for (var i = 1; i < ordered.Count; i++)
                if (ordered[i].PlannedFrom < ordered[i - 1].PlannedTo) throw new ValidationFailedException(Messages.RentalBookingSelfOverlap);
        }
        await RentalAvailability.LockAsync(_db, active.Select(l => l.EquipmentId).Distinct().OrderBy(x => x).ToList(), ct);
        var now = SaudiTime.Now;
        var problems = new List<string>();
        foreach (var l in active)
            foreach (var b in await RentalAvailability.BlocksAsync(_db, new[] { l.EquipmentId }, l.PlannedFrom, l.PlannedTo, excludeBookingId ?? c.BookingId, now, ct,
                         excludeContractId: c.Id == Guid.Empty ? null : c.Id, parentContractId: c.ParentContractId))
                problems.Add(await UnavailableMessageAsync(l.EquipmentId, b, ct));
        if (problems.Count > 0) throw new ConflictException(string.Join("\n", problems));
    }

    private async Task<string> UnavailableMessageAsync(Guid equipmentId, RentalBlockDto b, CancellationToken ct)
    {
        var code = await _db.Set<Equipment>().AsNoTracking().Where(e => e.Id == equipmentId).Select(e => e.Code).FirstAsync(ct);
        var kind = Messages.ResourceManager.GetString("RentalBlock_" + b.Kind, Messages.Culture) ?? b.Kind;
        return string.Format(Messages.RentalEquipmentUnavailable, code, kind, b.Reference ?? string.Empty, b.From, b.To?.ToString("yyyy-MM-dd HH:mm") ?? "…");
    }

    private async Task EnsureRentableAsync(Guid equipmentId, CancellationToken ct)
    {
        var e = await _db.Set<Equipment>().AsNoTracking().Where(x => x.Id == equipmentId).Select(x => new { x.Code, x.IsRentable }).FirstOrDefaultAsync(ct)
            ?? throw new ValidationFailedException(Messages.RentalEquipmentNotFound);
        if (!e.IsRentable) throw new ValidationFailedException(string.Format(Messages.RentalEquipmentNotRentable, e.Code));
    }

    private async Task EnsureCustomerAsync(Guid customerId, CancellationToken ct)
    {
        var status = await _db.Set<Customer>().AsNoTracking().Where(c => c.Id == customerId).Select(c => c.Status).FirstOrDefaultAsync(ct)
            ?? throw new ValidationFailedException(Messages.RentalCustomerNotFound);
        if (status != "active") throw new ValidationFailedException(Messages.RentalCustomerInactive);
    }

    private async Task<RentalContract> FindAsync(Guid id, bool track, CancellationToken ct)
    {
        var q = _db.Set<RentalContract>().Include(c => c.Lines).AsQueryable();
        if (!track) q = q.AsNoTracking();
        return await q.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException(Messages.RentalContractNotFound);
    }

    private async Task<List<RentalContractDto>> DtosAsync(List<RentalContract> list, bool withChanges, CancellationToken ct)
    {
        if (list.Count == 0) return new();
        var now = SaudiTime.Now;
        var ids = list.Select(c => c.Id).ToList();
        var customerIds = list.Select(c => c.CustomerId).Distinct().ToList();
        var customers = await _db.Set<Customer>().AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.NameAr, ct);
        var whIds = list.Where(c => c.WarehouseId != null).Select(c => c.WarehouseId!.Value).Distinct().ToList();
        var warehouses = await _db.Set<Warehouse>().AsNoTracking().Where(w => whIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.NameAr, ct);
        var bookingIds = list.Where(c => c.BookingId != null).Select(c => c.BookingId!.Value).ToList();
        var bookings = await _db.Set<RentalBooking>().AsNoTracking().Where(b => bookingIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.BookingNumber, ct);
        var lines = list.SelectMany(c => c.Lines).ToList();
        var eqIds = lines.Select(l => l.EquipmentId).Distinct().ToList();
        var equipment = await _db.Set<Equipment>().AsNoTracking().Where(e => eqIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => new { e.Code, e.NameAr }, ct);
        var opIds = lines.Where(l => l.OperatorEmployeeId != null).Select(l => l.OperatorEmployeeId!.Value).Distinct().ToList();
        var operators = await _db.Set<Employee>().AsNoTracking().Where(e => opIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.NameAr, ct);
        var changes = withChanges
            ? (await _db.Set<RentalContractChange>().AsNoTracking().Where(x => ids.Contains(x.ContractId)).OrderBy(x => x.CreatedAt).ToListAsync(ct)).ToLookup(x => x.ContractId)
            : null;

        return list.Select(c => new RentalContractDto
        {
            Id = c.Id, ContractNumber = c.ContractNumber, CustomerId = c.CustomerId, CustomerName = customers.GetValueOrDefault(c.CustomerId),
            WarehouseId = c.WarehouseId, WarehouseName = c.WarehouseId is Guid w ? warehouses.GetValueOrDefault(w) : null,
            BookingId = c.BookingId, BookingNumber = c.BookingId is Guid b ? bookings.GetValueOrDefault(b) : null, ParentContractId = c.ParentContractId,
            Status = c.Status, Term = c.Term, BillingCycle = c.BillingCycle, BillInAdvance = c.BillInAdvance, DepositRequired = c.DepositRequired,
            StartDate = c.StartDate, EndDate = c.EndDate, Terms = c.Terms, Notes = c.Notes, CancelReason = c.CancelReason, ActivatedAt = c.ActivatedAt,
            ActivatedBy = c.ActivatedBy, ClosedAt = c.ClosedAt, CreatedBy = c.CreatedBy, CreatedAt = c.CreatedAt,
            EstimatedTotal = c.Lines.Where(l => l.Status != LSt.Cancelled).Sum(l => l.EstimatedAmount),
            Lines = c.Lines.OrderBy(l => l.SortOrder).Select(l => new RentalContractLineDto
            {
                Id = l.Id, EquipmentId = l.EquipmentId, EquipmentCode = equipment.GetValueOrDefault(l.EquipmentId)?.Code,
                EquipmentName = equipment.GetValueOrDefault(l.EquipmentId)?.NameAr, Status = l.Status, PlannedFrom = l.PlannedFrom, PlannedTo = l.PlannedTo,
                ActualOut = l.ActualOut, ActualIn = l.ActualIn, MeterOut = l.MeterOut, MeterIn = l.MeterIn, Basis = l.Basis, Rate = l.Rate,
                RateOverridden = l.RateOverridden, WithOperator = l.WithOperator, OperatorRate = l.OperatorRate, OperatorEmployeeId = l.OperatorEmployeeId,
                OperatorName = l.OperatorEmployeeId is Guid op ? operators.GetValueOrDefault(op) : null, IncludedHours = l.IncludedHours,
                OvertimeRate = l.OvertimeRate, MinimumPeriods = l.MinimumPeriods, EstimatedAmount = l.EstimatedAmount, BilledThrough = l.BilledThrough,
                Notes = l.Notes, IsOverdue = l.Status == LSt.Out && l.PlannedTo < now,
            }).ToList(),
            Changes = changes == null ? new() : changes[c.Id].Select(x => new RentalContractChangeDto
            {
                Id = x.Id, Kind = x.Kind, LineId = x.LineId, OldEnd = x.OldEnd, NewEnd = x.NewEnd, NewContractId = x.NewContractId, Reason = x.Reason,
                CreatedBy = x.CreatedBy, CreatedAt = x.CreatedAt,
            }).ToList(),
        }).ToList();
    }

    private Task LogAsync(string action, RentalContract c, string? details, CancellationToken ct)
        => _audit.LogAsync(action, nameof(RentalContract), c.Id.ToString(), $"{c.ContractNumber} • {c.Status}{(details == null ? string.Empty : " • " + details)}", ct);

    private static DateTime Local(DateTime value) => value.Kind == DateTimeKind.Utc
        ? SaudiTime.FromUtc(value)
        : DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

    private static string? Trim(string? value, int max)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length > max ? v[..max] : v;
    }
}
