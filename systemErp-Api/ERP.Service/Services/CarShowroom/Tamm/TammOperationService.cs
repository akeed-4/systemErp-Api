using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.CarShowroom;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

/// <summary>
/// عمليات «تم»: سجل العملية ونقطة تحديث حالتها. عند النجاح يُنشأ مصروفها الفعلي من النوع المقابل مرتبطاً بالسيارة والعملية،
/// مرة واحدة فقط (فحص قبل الإنشاء + فهرس فريد على TammOperationId). الفشل لا ينشئ شيئاً، والمجهول لا ينشئ شيئاً حتى يُحسم يدوياً.
/// </summary>
public class TammOperationService : ITammOperationService
{
    private readonly ErpDbContext _db;
    private readonly ITransactionRunner _tx;
    private readonly INumberSequenceService _numbers;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;
    private readonly IAccountingPostingService _posting;

    public TammOperationService(ErpDbContext db, ITransactionRunner tx, INumberSequenceService numbers, ICurrentUser user, IAuditService audit, IAccountingPostingService posting)
    {
        _db = db; _tx = tx; _numbers = numbers; _user = user; _audit = audit; _posting = posting;
    }

    public async Task<PagedResult<TammOperationDto>> ListAsync(PaginationParams p, Guid? vehicleId = null, CancellationToken ct = default)
    {
        var q = _db.Set<TammOperation>().AsNoTracking().AsQueryable();
        if (vehicleId.HasValue) q = q.Where(o => o.VehicleId == vehicleId);
        if (Enum.TryParse<TammOperationStatus>(p.Status, true, out var status)) q = q.Where(o => o.Status == status);
        if (p.StartDate.HasValue) q = q.Where(o => o.RequestedAt >= p.StartDate);
        if (p.EndDate.HasValue) q = q.Where(o => o.RequestedAt <= p.EndDate);
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var t = p.SearchTerm.Trim();
            q = q.Where(o => o.OperationNumber.Contains(t) || (o.Vin != null && o.Vin.Contains(t)) || (o.Reference != null && o.Reference.Contains(t)));
        }
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(o => o.RequestedAt).ThenByDescending(o => o.OperationNumber)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);
        return new PagedResult<TammOperationDto>
        {
            Items = await ToDtosAsync(items, ct), TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    public async Task<TammOperationDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var op = await _db.Set<TammOperation>().AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct) ?? throw new NotFoundException(Messages.TammOperationNotFound);
        return (await ToDtosAsync(new List<TammOperation> { op }, ct))[0];
    }

    public Task<TammOperationDto> CreateAsync(CreateTammOperationDto d, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            if (!Enum.IsDefined(d.Type)) throw new ValidationFailedException(Messages.ExpenseTypeValuesInvalid);
            if (d.Fee < 0) throw new ValidationFailedException(Messages.TammOperationFeeInvalid);
            var vin = string.IsNullOrWhiteSpace(d.Vin) ? null : d.Vin.Trim().ToUpperInvariant();
            Guid? vehicleId = d.VehicleId;
            if (vehicleId.HasValue)
                vin = await _db.Set<Vehicle>().AsNoTracking().Where(v => v.Id == vehicleId).Select(v => v.ChassisNumber).FirstOrDefaultAsync(token)
                    ?? throw new ValidationFailedException(Messages.VehicleNotFound);
            else if (vin != null)
                vehicleId = await _db.Set<Vehicle>().AsNoTracking().Where(v => v.ChassisNumber == vin).Select(v => (Guid?)v.Id).FirstOrDefaultAsync(token);
            else throw new ValidationFailedException(Messages.TammOperationNeedsVehicleOrVin);

            if (d.ContractId.HasValue)
            {
                var contractVehicle = await _db.Set<CarSalesContract>().AsNoTracking().Where(c => c.Id == d.ContractId).Select(c => (Guid?)c.VehicleId).FirstOrDefaultAsync(token)
                    ?? throw new ValidationFailedException(Messages.SalesContractNotFound);
                if (contractVehicle != vehicleId) throw new ValidationFailedException(Messages.TammOperationContractMismatch);
            }

            var op = new TammOperation
            {
                OperationNumber = await _numbers.NextAsync("tamm_operation", "TAMM-", token),
                Type = d.Type, Status = TammOperationStatus.Pending, VehicleId = vehicleId, Vin = vin, ContractId = d.ContractId, Fee = d.Fee,
                Reference = string.IsNullOrWhiteSpace(d.Reference) ? null : d.Reference.Trim(), RequestedAt = d.RequestedAt ?? SaudiTime.Now,
                Notes = string.IsNullOrWhiteSpace(d.Notes) ? null : d.Notes.Trim(),
            };
            _db.Add(op);
            await _db.SaveChangesAsync(token);
            return await GetAsync(op.Id, token);
        }, ct);

    public Task<TammOperationDto> UpdateStatusAsync(Guid id, UpdateTammOperationStatusDto d, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var op = await _db.Set<TammOperation>().FirstOrDefaultAsync(o => o.Id == id, token) ?? throw new NotFoundException(Messages.TammOperationNotFound);
            if (d.Status is not (TammOperationStatus.Success or TammOperationStatus.Failed or TammOperationStatus.Unknown))
                throw new ValidationFailedException(Messages.TammOperationStatusInvalid);
            if (op.Status is TammOperationStatus.Success or TammOperationStatus.Failed)
            {
                // إعادة إرسال النتيجة نفسها (تكرار إشعار) لا تغيّر شيئاً ولا تكرر المصروف
                if (d.Status == op.Status) return await GetAsync(id, token);
                throw new ConflictException(string.Format(Messages.TammOperationFinal, op.Status));
            }
            if (d.Fee < 0) throw new ValidationFailedException(Messages.TammOperationFeeInvalid);

            var wasUnknown = op.Status == TammOperationStatus.Unknown;
            op.Status = d.Status;
            if (d.Fee.HasValue) op.Fee = d.Fee.Value;
            if (!string.IsNullOrWhiteSpace(d.Reference)) op.Reference = d.Reference.Trim();
            if (d.Status != TammOperationStatus.Unknown)
            {
                op.CompletedAt = d.CompletedAt ?? SaudiTime.Now;
                if (wasUnknown) { op.ResolvedManually = true; op.ResolvedBy = _user.Name; }
            }
            op.FailureReason = d.Status == TammOperationStatus.Failed ? d.FailureReason?.Trim() : null;
            await _db.SaveChangesAsync(token);

            if (op.Status == TammOperationStatus.Success) await TammCharges.CreateAsync(_db, _posting, _numbers, op, token);
            await _audit.LogAsync("TAMM_STATUS_UPDATED", nameof(TammOperation), op.Id.ToString(),
                $"{op.OperationNumber} {op.Type}: {op.Status}" + (op.ResolvedManually ? " (حسم يدوي)" : string.Empty), token);
            return await GetAsync(id, token);
        }, ct);

    private async Task<List<TammOperationDto>> ToDtosAsync(List<TammOperation> items, CancellationToken ct)
    {
        var ids = items.Select(o => (Guid?)o.Id).ToList();
        var charges = await _db.Set<AdditionalChargeLine>().AsNoTracking().Where(l => ids.Contains(l.TammOperationId))
            .Select(l => new { l.TammOperationId, l.Id, l.Treatment }).ToDictionaryAsync(l => l.TammOperationId!.Value, ct);
        return items.Select(o =>
        {
            var dto = Mapper.Map<TammOperationDto>(o);
            if (charges.TryGetValue(o.Id, out var charge)) { dto.ChargeLineId = charge.Id; dto.ChargeTreatment = charge.Treatment; }
            return dto;
        }).ToList();
    }
}
