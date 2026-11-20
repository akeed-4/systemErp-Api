using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using ESt = ERP.Core.Models.Accounting.EquipmentStatuses;
using HSt = ERP.Core.Models.Accounting.RentalHandoverStatuses;
using K = ERP.Core.Models.Accounting.RentalHandoverKinds;
using LSt = ERP.Core.Models.Accounting.RentalLineStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// محاضر تسليم المعدات للعميل واستلامها (عقد ساري). القواعد:
/// <list type="bullet">
/// <item>التسليم لسطر بانتظار التسليم قبل نهايته المخططة، والمعدة «متاحة»: يُخرج السطر بلحظته وعدّاده وتصبح المعدة «مؤجَّرة». التسليم
/// قبل البداية المخططة يفحص توفر الفترة المبكرة. سطر عقد مجدِّد ومعدته ما زالت خارجة على العقد السابق: يُستلم سطر السابق في اللحظة
/// والعدّاد نفسيهما (انتقال بلا حركة فعلية) وتبقى «مؤجَّرة».</item>
/// <item>الاستلام لسطر خارج بلحظة لا تسبق تسليمه: يعيد السطر وتعود المعدة «متاحة» (أو تبقى في الصيانة إن بدأت عليها)؛ التأخر عن
/// النهاية المخططة وفرق الوقود والضرر تظهر في المحضر وتُبنى عليها الرسوم في الفوترة.</item>
/// <item>العدّاد لا يتراجع عن آخر قراءة للمعدة، ويحدّث عدّادها. اللحظة لا تكون في المستقبل.</item>
/// <item>الإلغاء بسبب يعكس الأثر (السطر وحالة المعدة وعدّادها) ما دام المحضر آخر حركة على أسطره، ولم تُفوتر فترة من تسليمه، ولم
/// تُشغل فترة المعدة بعد استلامها.</item>
/// </list>
/// </summary>
public class RentalHandoverService : IRentalHandoverService
{
    private const int MaxLines = 100;
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(15);

    private readonly ErpDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;

    public RentalHandoverService(ErpDbContext db, INumberSequenceService numbers, ITransactionRunner tx, ICurrentUser user, IAuditService audit)
    {
        _db = db; _numbers = numbers; _tx = tx; _user = user; _audit = audit;
    }

    public async Task<List<RentalHandoverDto>> ListAsync(Guid? contractId, string? kind, CancellationToken ct = default)
    {
        var q = _db.Set<RentalHandover>().AsNoTracking().Include(h => h.Lines).AsQueryable();
        if (contractId is Guid cid) q = q.Where(h => h.ContractId == cid);
        if (!string.IsNullOrWhiteSpace(kind)) { var k = kind.Trim().ToLowerInvariant(); q = q.Where(h => h.Kind == k); }
        return await DtosAsync(await q.OrderByDescending(h => h.HandoverAt).ThenByDescending(h => h.CreatedAt).Take(1000).ToListAsync(ct), ct);
    }

    public async Task<RentalHandoverDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var h = await _db.Set<RentalHandover>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(Messages.RentalHandoverNotFound);
        return (await DtosAsync(new List<RentalHandover> { h }, ct)).Single();
    }

    public Task<RentalHandoverDto> CreateAsync(SaveRentalHandoverDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var kind = r.Kind?.Trim().ToLowerInvariant();
            if (!K.IsValid(kind)) throw new ValidationFailedException(Messages.RentalHandoverKindInvalid);
            var c = await _db.Set<RentalContract>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == r.ContractId, token)
                ?? throw new NotFoundException(Messages.RentalContractNotFound);
            if (c.Status != RentalContractStatuses.Active) throw new ConflictException(Messages.RentalHandoverContractNotActive);
            if (r.Lines.Count == 0) throw new ValidationFailedException(Messages.RentalHandoverLinesRequired);
            if (r.Lines.Count > MaxLines) throw new ValidationFailedException(string.Format(Messages.RentalBookingTooManyLines, MaxLines));
            if (r.Lines.Select(l => l.ContractLineId).Distinct().Count() != r.Lines.Count) throw new ValidationFailedException(Messages.RentalHandoverDuplicateLine);
            var now = SaudiTime.Now;
            var at = r.HandoverAt is DateTime t ? Local(t) : now;
            if (at > now + FutureTolerance) throw new ValidationFailedException(Messages.RentalHandoverInFuture);
            if (r.WarehouseId is Guid wh && !await _db.Set<Warehouse>().AnyAsync(w => w.Id == wh, token))
                throw new ValidationFailedException(Messages.RentalWarehouseNotFound);

            var lines = r.Lines.Select(l => (Input: l, Line: c.Lines.FirstOrDefault(x => x.Id == l.ContractLineId)
                ?? throw new ValidationFailedException(Messages.RentalHandoverLineNotFound))).ToList();
            var eqIds = lines.Select(x => x.Line.EquipmentId).Distinct().OrderBy(x => x).ToList();
            await RentalAvailability.LockAsync(_db, eqIds, token);
            var equipment = await _db.Set<Equipment>().Where(e => eqIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, token);
            // معدات العقد السابق التي ما زالت عند العميل (تنتقل لسطر التجديد بتسليمه)
            var carried = kind == K.Delivery && c.ParentContractId is Guid parentId
                ? await _db.Set<RentalContractLine>().Where(l => l.ContractId == parentId && l.Status == LSt.Out && eqIds.Contains(l.EquipmentId)).ToListAsync(token)
                : new List<RentalContractLine>();

            var h = new RentalHandover
            {
                ContractId = c.Id, Kind = kind!, Status = HSt.Posted, HandoverAt = at, WarehouseId = r.WarehouseId ?? c.WarehouseId,
                SiteLocation = Trim(r.SiteLocation, 300), CounterpartyName = Trim(r.CounterpartyName, 200), CounterpartyIdNumber = Trim(r.CounterpartyIdNumber, 50),
                CounterpartyPhone = Trim(r.CounterpartyPhone, 30), Notes = Trim(r.Notes, 1000), CreatedBy = _user.Name ?? _user.Email, CreatedByUserId = _user.UserId,
            };
            var order = 0;
            foreach (var (input, line) in lines)
            {
                var e = equipment[line.EquipmentId];
                var condition = string.IsNullOrWhiteSpace(input.Condition) ? RentalConditions.Good : input.Condition.Trim().ToLowerInvariant();
                if (!RentalConditions.IsValid(condition)) throw new ValidationFailedException(Messages.RentalConditionInvalid);
                if (input.FuelPercent is < 0 or > 100) throw new ValidationFailedException(Messages.RentalFuelInvalid);
                if (input.Meter is < 0) throw new ValidationFailedException(Messages.RentalMeterInvalid);
                var meter = input.Meter is decimal m ? decimal.Round(m, 2) : (decimal?)null;
                var hl = new RentalHandoverLine
                {
                    ContractLineId = line.Id, EquipmentId = e.Id, Meter = meter, MeterBefore = e.MeterHours,
                    FuelPercent = input.FuelPercent is decimal f ? decimal.Round(f, 2) : null, Condition = condition,
                    ConditionNotes = Trim(input.ConditionNotes, 500), Accessories = Trim(input.Accessories, 500), SortOrder = ++order,
                };

                if (kind == K.Delivery) await DeliverAsync(c, line, e, hl, at, carried, now, token);
                else
                {
                    if (line.Status != LSt.Out) throw new ConflictException(string.Format(Messages.RentalHandoverLineNotOut, e.Code));
                    if (line.ActualOut is DateTime outAt && at < outAt) throw new ValidationFailedException(string.Format(Messages.RentalHandoverBeforeOut, e.Code));
                    line.Status = LSt.Returned; line.ActualIn = at; line.MeterIn = meter;
                    if (e.Status == ESt.Rented) e.Status = ESt.Available;
                }
                if (meter is decimal reading)
                {
                    var last = Math.Max(e.MeterHours, kind == K.Return ? line.MeterOut ?? 0 : 0);
                    if (reading < last) throw new ValidationFailedException(string.Format(Messages.RentalMeterBackwards, e.Code, reading, last));
                    e.MeterHours = reading;
                }
                h.Lines.Add(hl);
            }

            h.HandoverNumber = await _numbers.AssignAsync(new DocumentNumberRequest
            {
                Key = "rental_handover", DefaultPrefix = "RHO-", WarehouseId = h.WarehouseId, Date = at.Date,
                Exists = (n, tk) => _db.Set<RentalHandover>().AnyAsync(x => x.HandoverNumber == n, tk),
            }, token);
            _db.Add(h);
            await _db.SaveChangesAsync(token);
            await LogAsync(kind == K.Delivery ? "RENTAL_HANDOVER_DELIVERED" : "RENTAL_HANDOVER_RETURNED", h, c.ContractNumber, token);
            return await GetAsync(h.Id, token);
        }, ct);

    private async Task DeliverAsync(RentalContract c, RentalContractLine line, Equipment e, RentalHandoverLine hl, DateTime at,
        List<RentalContractLine> carried, DateTime now, CancellationToken ct)
    {
        if (line.Status != LSt.Pending) throw new ConflictException(string.Format(Messages.RentalHandoverLineNotPending, e.Code));
        if (at >= line.PlannedTo) throw new ValidationFailedException(string.Format(Messages.RentalHandoverAfterPlannedEnd, e.Code));

        var previous = carried.FirstOrDefault(p => p.EquipmentId == e.Id);
        if (previous != null)
        {
            // انتقال من العقد السابق: يُستلم سطره في اللحظة والعدّاد نفسيهما وتبقى المعدة عند العميل
            if (previous.ActualOut is DateTime outAt && at < outAt) throw new ValidationFailedException(string.Format(Messages.RentalHandoverBeforeOut, e.Code));
            if (hl.Meter is decimal m && previous.MeterOut is decimal mo && m < mo)
                throw new ValidationFailedException(string.Format(Messages.RentalMeterBackwards, e.Code, m, mo));
            previous.Status = LSt.Returned; previous.ActualIn = at; previous.MeterIn = hl.Meter;
            hl.CarriedFromLineId = previous.Id;
        }
        else
        {
            if (e.Status != ESt.Available) throw new ConflictException(string.Format(Messages.RentalHandoverEquipmentNotAvailable, e.Code));
            if (at < line.PlannedFrom)
            {
                var problems = new List<string>();
                foreach (var b in await RentalAvailability.BlocksAsync(_db, new[] { e.Id }, at, line.PlannedFrom, null, now, ct,
                             excludeContractId: c.Id, parentContractId: c.ParentContractId))
                    problems.Add(UnavailableMessage(e.Code, b));
                if (problems.Count > 0) throw new ConflictException(string.Join("\n", problems));
            }
            e.Status = ESt.Rented;
        }
        line.Status = LSt.Out; line.ActualOut = at; line.MeterOut = hl.Meter;
    }

    public Task<RentalHandoverDto> CancelAsync(Guid id, RentalReasonDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var h = await _db.Set<RentalHandover>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, token)
                ?? throw new NotFoundException(Messages.RentalHandoverNotFound);
            if (h.Status == HSt.Cancelled) throw new ConflictException(string.Format(Messages.RentalHandoverAlreadyCancelled, h.HandoverNumber));
            var reason = request.Reason?.Trim();
            if (string.IsNullOrEmpty(reason)) throw new ValidationFailedException(Messages.RentalCancelReasonRequired);
            var c = await _db.Set<RentalContract>().Include(x => x.Lines).FirstAsync(x => x.Id == h.ContractId, token);
            if (c.Status != RentalContractStatuses.Active) throw new ConflictException(Messages.RentalContractNotEditable);

            var eqIds = h.Lines.Select(l => l.EquipmentId).Distinct().OrderBy(x => x).ToList();
            await RentalAvailability.LockAsync(_db, eqIds, token);
            var equipment = await _db.Set<Equipment>().Where(e => eqIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, token);
            var carriedIds = h.Lines.Where(l => l.CarriedFromLineId != null).Select(l => l.CarriedFromLineId!.Value).ToList();
            var carried = await _db.Set<RentalContractLine>().Where(l => carriedIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, token);
            var now = SaudiTime.Now;

            foreach (var hl in h.Lines)
            {
                var line = c.Lines.First(l => l.Id == hl.ContractLineId);
                var e = equipment[hl.EquipmentId];
                if (h.Kind == K.Delivery)
                {
                    if (line.Status != LSt.Out || line.ActualOut != h.HandoverAt) throw new ConflictException(string.Format(Messages.RentalHandoverNotLatest, h.HandoverNumber));
                    if (line.BilledThrough != null) throw new ConflictException(string.Format(Messages.RentalHandoverBilled, h.HandoverNumber));
                    line.Status = LSt.Pending; line.ActualOut = null; line.MeterOut = null;
                    if (hl.CarriedFromLineId is Guid pid && carried.TryGetValue(pid, out var previous))
                    {
                        if (previous.Status != LSt.Returned || previous.ActualIn != h.HandoverAt)
                            throw new ConflictException(string.Format(Messages.RentalHandoverNotLatest, h.HandoverNumber));
                        previous.Status = LSt.Out; previous.ActualIn = null; previous.MeterIn = null;
                    }
                    else if (e.Status == ESt.Rented) e.Status = ESt.Available;
                }
                else
                {
                    if (line.Status != LSt.Returned || line.ActualIn != h.HandoverAt) throw new ConflictException(string.Format(Messages.RentalHandoverNotLatest, h.HandoverNumber));
                    // تعود خارجة: لا تكون المعدة قد شُغلت أو تغيرت حالتها بعد الاستلام
                    var until = line.PlannedTo > now ? line.PlannedTo : now.AddYears(50);
                    var blocks = await RentalAvailability.BlocksAsync(_db, new[] { e.Id }, h.HandoverAt, until, null, now, token, excludeContractId: c.Id);
                    if (e.Status != ESt.Available || blocks.Count > 0) throw new ConflictException(string.Format(Messages.RentalHandoverCannotRestore, e.Code));
                    line.Status = LSt.Out; line.ActualIn = null; line.MeterIn = null;
                    e.Status = ESt.Rented;
                }
                // عدّاد المعدة يعود لما قبل المحضر ما لم تُسجَّل قراءة بعده
                if (hl.Meter is decimal m && e.MeterHours == m && hl.MeterBefore is decimal before) e.MeterHours = before;
            }

            h.Status = HSt.Cancelled; h.CancelReason = reason.Length > 500 ? reason[..500] : reason; h.CancelledAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(token);
            await LogAsync("RENTAL_HANDOVER_CANCELLED", h, h.CancelReason, token);
            return await GetAsync(id, token);
        }, ct);

    // ---------- العرض ----------

    private async Task<List<RentalHandoverDto>> DtosAsync(List<RentalHandover> list, CancellationToken ct)
    {
        if (list.Count == 0) return new();
        var ids = list.Select(h => h.Id).ToList();
        var contractIds = list.Select(h => h.ContractId).Distinct().ToList();
        var contracts = await _db.Set<RentalContract>().AsNoTracking().Where(c => contractIds.Contains(c.Id))
            .Select(c => new { c.Id, c.ContractNumber, c.CustomerId }).ToDictionaryAsync(c => c.Id, ct);
        var customerIds = contracts.Values.Select(c => c.CustomerId).Distinct().ToList();
        var customers = await _db.Set<Customer>().AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.NameAr, ct);
        var whIds = list.Where(h => h.WarehouseId != null).Select(h => h.WarehouseId!.Value).Distinct().ToList();
        var warehouses = await _db.Set<Warehouse>().AsNoTracking().Where(w => whIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.NameAr, ct);
        var lines = list.SelectMany(h => h.Lines).ToList();
        var eqIds = lines.Select(l => l.EquipmentId).Distinct().ToList();
        var equipment = await _db.Set<Equipment>().AsNoTracking().Where(e => eqIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => new { e.Code, e.NameAr }, ct);
        var lineIds = lines.Select(l => l.ContractLineId).Distinct().ToList();
        var contractLines = await _db.Set<RentalContractLine>().AsNoTracking().Where(l => lineIds.Contains(l.Id))
            .Select(l => new { l.Id, l.PlannedTo, l.MeterOut }).ToDictionaryAsync(l => l.Id, ct);
        // وقود التسليم الساري لكل سطر (لفرق الاستلام)
        var deliveredFuel = (await (from hl in _db.Set<RentalHandoverLine>().AsNoTracking()
                                    join h in _db.Set<RentalHandover>().AsNoTracking() on hl.HandoverId equals h.Id
                                    where lineIds.Contains(hl.ContractLineId) && h.Kind == K.Delivery && h.Status == HSt.Posted
                                    select new { hl.ContractLineId, hl.FuelPercent, h.HandoverAt }).ToListAsync(ct))
            .GroupBy(x => x.ContractLineId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.HandoverAt).First().FuelPercent);
        var attachments = await _db.Set<DocumentAttachment>().AsNoTracking()
            .Where(a => a.OwnerType == RentalAttachmentOwnerTypes.Handover && ids.Contains(a.OwnerId))
            .GroupBy(a => a.OwnerId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return list.Select(h =>
        {
            var c = contracts.GetValueOrDefault(h.ContractId);
            return new RentalHandoverDto
            {
                Id = h.Id, HandoverNumber = h.HandoverNumber, ContractId = h.ContractId, ContractNumber = c?.ContractNumber, CustomerId = c?.CustomerId ?? Guid.Empty,
                CustomerName = c == null ? null : customers.GetValueOrDefault(c.CustomerId), Kind = h.Kind, Status = h.Status, HandoverAt = h.HandoverAt,
                WarehouseId = h.WarehouseId, WarehouseName = h.WarehouseId is Guid w ? warehouses.GetValueOrDefault(w) : null, SiteLocation = h.SiteLocation,
                CounterpartyName = h.CounterpartyName, CounterpartyIdNumber = h.CounterpartyIdNumber, CounterpartyPhone = h.CounterpartyPhone, Notes = h.Notes,
                CancelReason = h.CancelReason, CancelledAt = h.CancelledAt, CreatedBy = h.CreatedBy, CreatedAt = h.CreatedAt,
                AttachmentCount = attachments.GetValueOrDefault(h.Id), HasDamage = h.Lines.Any(l => l.Condition == RentalConditions.Damaged),
                Lines = h.Lines.OrderBy(l => l.SortOrder).Select(l =>
                {
                    var cl = contractLines.GetValueOrDefault(l.ContractLineId);
                    var isReturn = h.Kind == K.Return;
                    decimal? fuelOut = isReturn ? deliveredFuel.GetValueOrDefault(l.ContractLineId) : null;
                    return new RentalHandoverLineDto
                    {
                        Id = l.Id, ContractLineId = l.ContractLineId, EquipmentId = l.EquipmentId, EquipmentCode = equipment.GetValueOrDefault(l.EquipmentId)?.Code,
                        EquipmentName = equipment.GetValueOrDefault(l.EquipmentId)?.NameAr, Meter = l.Meter, FuelPercent = l.FuelPercent, Condition = l.Condition,
                        ConditionNotes = l.ConditionNotes, Accessories = l.Accessories, CarriedFromLineId = l.CarriedFromLineId,
                        UsedHours = isReturn && l.Meter is decimal mi && cl?.MeterOut is decimal mo ? mi - mo : null,
                        LateHours = isReturn && cl != null && h.HandoverAt > cl.PlannedTo ? decimal.Round((decimal)(h.HandoverAt - cl.PlannedTo).TotalHours, 2) : 0,
                        FuelDifference = isReturn && l.FuelPercent is decimal fi && fuelOut is decimal fo ? fi - fo : null,
                    };
                }).ToList(),
            };
        }).ToList();
    }

    private static string UnavailableMessage(string code, RentalBlockDto b)
    {
        var kind = Messages.ResourceManager.GetString("RentalBlock_" + b.Kind, Messages.Culture) ?? b.Kind;
        return string.Format(Messages.RentalEquipmentUnavailable, code, kind, b.Reference ?? string.Empty, b.From, b.To?.ToString("yyyy-MM-dd HH:mm") ?? "…");
    }

    private Task LogAsync(string action, RentalHandover h, string? details, CancellationToken ct)
        => _audit.LogAsync(action, nameof(RentalHandover), h.Id.ToString(), $"{h.HandoverNumber} • {h.Kind}{(details == null ? string.Empty : " • " + details)}", ct);

    private static DateTime Local(DateTime value) => value.Kind == DateTimeKind.Utc
        ? SaudiTime.FromUtc(value)
        : DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

    private static string? Trim(string? value, int max)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length > max ? v[..max] : v;
    }
}
