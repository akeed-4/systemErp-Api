using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using K = ERP.Core.Models.Accounting.RentalTransportKinds;
using M = ERP.Core.Models.Accounting.RentalTransportModes;
using St = ERP.Core.Models.Accounting.RentalTransportStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// مهام نقل معدات عقود التأجير (سجل تشغيلي بلا قيود). القواعد:
/// <list type="bullet">
/// <item>العقد مسودة أو ساري، والأسطر المنقولة من أسطره (غير الملغاة)، والمحضر المرتبط من محاضره المرحّلة.</item>
/// <item>النقل الداخلي: السائق موظف نشط، ومركبة النقل من سجل المعدات وليست من المعدات المنقولة (أو وصف حر). الخارجي: مورد النقل إلزامي.</item>
/// <item>التكلفة والمبلغ على العميل غير سالبين. التعديل والحذف للمجدولة؛ التنفيذ بلحظة لا تكون في المستقبل وتكلفة فعلية؛ الإلغاء بسبب.</item>
/// <item>تكلفة المورد الخارجي تُقيَّد من فاتورة شرائه (على مركز تكلفة المعدة)، والمبلغ على العميل يُفوتر مع رسوم العقد.</item>
/// </list>
/// </summary>
public class RentalTransportService : IRentalTransportService
{
    private const int MaxLines = 100;
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(15);

    private readonly ErpDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;

    public RentalTransportService(ErpDbContext db, INumberSequenceService numbers, ITransactionRunner tx, ICurrentUser user, IAuditService audit)
    {
        _db = db; _numbers = numbers; _tx = tx; _user = user; _audit = audit;
    }

    public async Task<List<RentalTransportJobDto>> ListAsync(Guid? contractId, string? status, CancellationToken ct = default)
    {
        var q = _db.Set<RentalTransportJob>().AsNoTracking().Include(j => j.Lines).AsQueryable();
        if (contractId is Guid cid) q = q.Where(j => j.ContractId == cid);
        if (!string.IsNullOrWhiteSpace(status)) { var s = status.Trim().ToLowerInvariant(); q = q.Where(j => j.Status == s); }
        return await DtosAsync(await q.OrderByDescending(j => j.ScheduledAt).ThenByDescending(j => j.CreatedAt).Take(1000).ToListAsync(ct), ct);
    }

    public async Task<RentalTransportJobDto> GetAsync(Guid id, CancellationToken ct = default)
        => (await DtosAsync(new List<RentalTransportJob> { await FindAsync(id, track: false, ct) }, ct)).Single();

    public Task<RentalTransportJobDto> CreateAsync(SaveRentalTransportJobDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var j = new RentalTransportJob { Status = St.Planned, CreatedBy = _user.Name ?? _user.Email, CreatedByUserId = _user.UserId };
            var contract = await ApplyAsync(j, request, token);
            j.JobNumber = await _numbers.AssignAsync(new DocumentNumberRequest
            {
                Key = "rental_transport", DefaultPrefix = "RTJ-", WarehouseId = contract.WarehouseId, Date = j.ScheduledAt.Date,
                Exists = (n, tk) => _db.Set<RentalTransportJob>().AnyAsync(x => x.JobNumber == n, tk),
            }, token);
            _db.Add(j);
            await _db.SaveChangesAsync(token);
            await LogAsync("RENTAL_TRANSPORT_CREATED", j, contract.ContractNumber, token);
            return await GetAsync(j.Id, token);
        }, ct);

    public Task<RentalTransportJobDto> UpdateAsync(Guid id, SaveRentalTransportJobDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var j = await FindAsync(id, track: true, token);
            if (j.Status != St.Planned) throw new ConflictException(Messages.RentalTransportNotPlanned);
            if (request.ContractId != j.ContractId) throw new ValidationFailedException(Messages.RentalTransportContractLocked);
            await ApplyAsync(j, request, token);
            await _db.SaveChangesAsync(token);
            await LogAsync("RENTAL_TRANSPORT_UPDATED", j, null, token);
            return await GetAsync(id, token);
        }, ct);

    public async Task<RentalTransportJobDto> CompleteAsync(Guid id, CompleteRentalTransportJobDto request, CancellationToken ct = default)
    {
        var j = await FindAsync(id, track: true, ct);
        if (j.Status != St.Planned) throw new ConflictException(Messages.RentalTransportNotPlanned);
        var now = SaudiTime.Now;
        var at = request.CompletedAt is DateTime t ? Local(t) : now;
        if (at > now + FutureTolerance) throw new ValidationFailedException(Messages.RentalTransportCompletedInFuture);
        if (request.Cost is < 0) throw new ValidationFailedException(Messages.RentalTransportAmountNegative);
        if (request.HandoverId is Guid hid) j.HandoverId = await EnsureHandoverAsync(j.ContractId, hid, ct);
        if (request.Cost is decimal cost) j.Cost = decimal.Round(cost, 2);
        j.Status = St.Completed; j.CompletedAt = at; j.CompletedBy = _user.Name ?? _user.Email;
        await _db.SaveChangesAsync(ct);
        await LogAsync("RENTAL_TRANSPORT_COMPLETED", j, $"{j.Cost:0.##}", ct);
        return await GetAsync(id, ct);
    }

    public async Task<RentalTransportJobDto> CancelAsync(Guid id, RentalReasonDto request, CancellationToken ct = default)
    {
        var j = await FindAsync(id, track: true, ct);
        if (j.Status != St.Planned) throw new ConflictException(Messages.RentalTransportNotPlanned);
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason)) throw new ValidationFailedException(Messages.RentalCancelReasonRequired);
        j.Status = St.Cancelled; j.CancelReason = reason.Length > 500 ? reason[..500] : reason;
        await _db.SaveChangesAsync(ct);
        await LogAsync("RENTAL_TRANSPORT_CANCELLED", j, j.CancelReason, ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var j = await FindAsync(id, track: true, ct);
        if (j.Status != St.Planned) throw new ConflictException(Messages.RentalTransportNotPlanned);
        _db.RemoveRange(j.Lines);
        _db.Remove(j);
        await _db.SaveChangesAsync(ct);
        await LogAsync("RENTAL_TRANSPORT_DELETED", j, null, ct);
    }

    // ---------- التحقق ----------

    private async Task<RentalContract> ApplyAsync(RentalTransportJob j, SaveRentalTransportJobDto r, CancellationToken ct)
    {
        var kind = r.Kind?.Trim().ToLowerInvariant();
        if (!K.IsValid(kind)) throw new ValidationFailedException(Messages.RentalTransportKindInvalid);
        var mode = r.Mode?.Trim().ToLowerInvariant();
        if (!M.IsValid(mode)) throw new ValidationFailedException(Messages.RentalTransportModeInvalid);
        var c = await _db.Set<RentalContract>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == r.ContractId, ct)
            ?? throw new NotFoundException(Messages.RentalContractNotFound);
        if (c.Status is not (RentalContractStatuses.Draft or RentalContractStatuses.Active)) throw new ConflictException(Messages.RentalContractNotEditable);
        var lineIds = r.ContractLineIds.Distinct().ToList();
        if (lineIds.Count == 0) throw new ValidationFailedException(Messages.RentalTransportLinesRequired);
        if (lineIds.Count > MaxLines) throw new ValidationFailedException(string.Format(Messages.RentalBookingTooManyLines, MaxLines));
        var lines = lineIds.Select(id => c.Lines.FirstOrDefault(l => l.Id == id && l.Status != RentalLineStatuses.Cancelled)
            ?? throw new ValidationFailedException(Messages.RentalHandoverLineNotFound)).ToList();
        if (r.Cost < 0 || r.ChargeAmount < 0) throw new ValidationFailedException(Messages.RentalTransportAmountNegative);

        Guid? driver = null, vehicle = null, supplier = null;
        if (mode == M.External)
        {
            supplier = r.SupplierId ?? throw new ValidationFailedException(Messages.RentalTransportSupplierRequired);
            if (!await _db.Set<Supplier>().AnyAsync(s => s.Id == supplier, ct)) throw new ValidationFailedException(Messages.RentalTransportSupplierRequired);
        }
        else
        {
            if (r.DriverEmployeeId is Guid d)
            {
                if (!await _db.Set<Employee>().AnyAsync(e => e.Id == d && e.Status == EmployeeStatuses.Active, ct))
                    throw new ValidationFailedException(Messages.RentalTransportDriverNotFound);
                driver = d;
            }
            if (r.TransportEquipmentId is Guid v)
            {
                if (lines.Any(l => l.EquipmentId == v)) throw new ValidationFailedException(Messages.RentalTransportVehicleIsCargo);
                if (!await _db.Set<Equipment>().AnyAsync(e => e.Id == v, ct)) throw new ValidationFailedException(Messages.RentalTransportVehicleNotFound);
                vehicle = v;
            }
        }

        j.ContractId = c.Id; j.Kind = kind!; j.Mode = mode!; j.ScheduledAt = Local(r.ScheduledAt);
        j.HandoverId = r.HandoverId is Guid hid ? await EnsureHandoverAsync(c.Id, hid, ct) : null;
        j.FromLocation = Trim(r.FromLocation, 300); j.ToLocation = Trim(r.ToLocation, 300);
        j.DriverEmployeeId = driver; j.TransportEquipmentId = vehicle; j.SupplierId = supplier;
        j.VehicleDescription = mode == M.Internal ? Trim(r.VehicleDescription, 200) : null;
        j.Cost = decimal.Round(r.Cost, 2); j.ChargeAmount = decimal.Round(r.ChargeAmount, 2); j.Notes = Trim(r.Notes, 1000);
        if (j.Lines.Count > 0) _db.RemoveRange(j.Lines.ToList());
        j.Lines.Clear();
        foreach (var l in lines) j.Lines.Add(new RentalTransportJobLine { ContractLineId = l.Id, EquipmentId = l.EquipmentId });
        return c;
    }

    private async Task<Guid> EnsureHandoverAsync(Guid contractId, Guid handoverId, CancellationToken ct)
    {
        if (!await _db.Set<RentalHandover>().AnyAsync(h => h.Id == handoverId && h.ContractId == contractId && h.Status == RentalHandoverStatuses.Posted, ct))
            throw new ValidationFailedException(Messages.RentalTransportHandoverInvalid);
        return handoverId;
    }

    private async Task<RentalTransportJob> FindAsync(Guid id, bool track, CancellationToken ct)
    {
        var q = _db.Set<RentalTransportJob>().Include(j => j.Lines).AsQueryable();
        if (!track) q = q.AsNoTracking();
        return await q.FirstOrDefaultAsync(j => j.Id == id, ct) ?? throw new NotFoundException(Messages.RentalTransportNotFound);
    }

    // ---------- العرض ----------

    private async Task<List<RentalTransportJobDto>> DtosAsync(List<RentalTransportJob> list, CancellationToken ct)
    {
        if (list.Count == 0) return new();
        var contractIds = list.Select(j => j.ContractId).Distinct().ToList();
        var contracts = await _db.Set<RentalContract>().AsNoTracking().Where(c => contractIds.Contains(c.Id))
            .Select(c => new { c.Id, c.ContractNumber, c.CustomerId }).ToDictionaryAsync(c => c.Id, ct);
        var customerIds = contracts.Values.Select(c => c.CustomerId).Distinct().ToList();
        var customers = await _db.Set<Customer>().AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.NameAr, ct);
        var handoverIds = list.Where(j => j.HandoverId != null).Select(j => j.HandoverId!.Value).Distinct().ToList();
        var handovers = await _db.Set<RentalHandover>().AsNoTracking().Where(h => handoverIds.Contains(h.Id)).ToDictionaryAsync(h => h.Id, h => h.HandoverNumber, ct);
        var driverIds = list.Where(j => j.DriverEmployeeId != null).Select(j => j.DriverEmployeeId!.Value).Distinct().ToList();
        var drivers = await _db.Set<Employee>().AsNoTracking().Where(e => driverIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.NameAr, ct);
        var supplierIds = list.Where(j => j.SupplierId != null).Select(j => j.SupplierId!.Value).Distinct().ToList();
        var suppliers = await _db.Set<Supplier>().AsNoTracking().Where(s => supplierIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.NameAr, ct);
        var eqIds = list.SelectMany(j => j.Lines.Select(l => l.EquipmentId))
            .Concat(list.Where(j => j.TransportEquipmentId != null).Select(j => j.TransportEquipmentId!.Value)).Distinct().ToList();
        var equipment = await _db.Set<Equipment>().AsNoTracking().Where(e => eqIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => new { e.Code, e.NameAr }, ct);

        return list.Select(j =>
        {
            var c = contracts.GetValueOrDefault(j.ContractId);
            return new RentalTransportJobDto
            {
                Id = j.Id, JobNumber = j.JobNumber, ContractId = j.ContractId, ContractNumber = c?.ContractNumber,
                CustomerName = c == null ? null : customers.GetValueOrDefault(c.CustomerId), HandoverId = j.HandoverId,
                HandoverNumber = j.HandoverId is Guid h ? handovers.GetValueOrDefault(h) : null, Kind = j.Kind, Mode = j.Mode, Status = j.Status,
                ScheduledAt = j.ScheduledAt, CompletedAt = j.CompletedAt, FromLocation = j.FromLocation, ToLocation = j.ToLocation,
                DriverEmployeeId = j.DriverEmployeeId, DriverName = j.DriverEmployeeId is Guid d ? drivers.GetValueOrDefault(d) : null,
                TransportEquipmentId = j.TransportEquipmentId, TransportEquipmentCode = j.TransportEquipmentId is Guid v ? equipment.GetValueOrDefault(v)?.Code : null,
                VehicleDescription = j.VehicleDescription, SupplierId = j.SupplierId, SupplierName = j.SupplierId is Guid s ? suppliers.GetValueOrDefault(s) : null,
                Cost = j.Cost, ChargeAmount = j.ChargeAmount, Notes = j.Notes, CancelReason = j.CancelReason, CompletedBy = j.CompletedBy,
                CreatedBy = j.CreatedBy, CreatedAt = j.CreatedAt,
                Lines = j.Lines.Select(l => new RentalTransportJobLineDto
                {
                    Id = l.Id, ContractLineId = l.ContractLineId, EquipmentId = l.EquipmentId,
                    EquipmentCode = equipment.GetValueOrDefault(l.EquipmentId)?.Code, EquipmentName = equipment.GetValueOrDefault(l.EquipmentId)?.NameAr,
                }).OrderBy(l => l.EquipmentCode).ToList(),
            };
        }).ToList();
    }

    private Task LogAsync(string action, RentalTransportJob j, string? details, CancellationToken ct)
        => _audit.LogAsync(action, nameof(RentalTransportJob), j.Id.ToString(), $"{j.JobNumber} • {j.Kind}{(details == null ? string.Empty : " • " + details)}", ct);

    private static DateTime Local(DateTime value) => value.Kind == DateTimeKind.Utc
        ? SaudiTime.FromUtc(value)
        : DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

    private static string? Trim(string? value, int max)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length > max ? v[..max] : v;
    }
}
