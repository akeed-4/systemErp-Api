using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.CarShowroom;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

public class VehicleReservationService : IVehicleReservationService
{
    private readonly ErpDbContext _db;
    private readonly ITransactionRunner _tx;
    private readonly IAuditService _audit;
    private readonly IAccountingPostingService _posting;

    public VehicleReservationService(ErpDbContext db, ITransactionRunner tx, IAuditService audit, IAccountingPostingService posting)
    {
        _db = db; _tx = tx; _audit = audit; _posting = posting;
    }

    public async Task<PagedResult<VehicleReservationDto>> ListAsync(PaginationParams p, Guid? vehicleId = null, CancellationToken ct = default)
    {
        await VehicleReservations.ExpireDueAsync(_db, ct);
        var q = _db.Set<VehicleReservation>().AsNoTracking().AsQueryable();
        if (vehicleId.HasValue) q = q.Where(r => r.VehicleId == vehicleId);
        if (Enum.TryParse<VehicleReservationStatus>(p.Status, true, out var status)) q = q.Where(r => r.Status == status);
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var t = p.SearchTerm.Trim();
            q = q.Where(r => r.CustomerName.Contains(t));
        }
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(r => r.ReservedAt).Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);
        return new PagedResult<VehicleReservationDto>
        {
            Items = await ToDtosAsync(items, ct), TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    public Task<VehicleReservationDto> CreateAsync(CreateVehicleReservationDto d, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            await VehicleReservations.ExpireDueAsync(_db, token);
            var vehicle = await _db.Set<Vehicle>().FirstOrDefaultAsync(v => v.Id == d.VehicleId, token) ?? throw new ValidationFailedException(Messages.VehicleNotFound);
            var existing = await VehicleReservations.ActiveAsync(_db, vehicle.Id, token);
            if (existing != null) throw new ConflictException(string.Format(Messages.VehicleAlreadyReservedFor, existing.CustomerName, existing.ExpiresAt));
            if (vehicle.Status != VehicleStatus.Available) throw new ConflictException(string.Format(Messages.VehicleNotAvailableForReservation, vehicle.ChassisNumber));

            var name = d.CustomerName?.Trim();
            if (d.CustomerId.HasValue)
                name = await _db.Set<Customer>().AsNoTracking().Where(c => c.Id == d.CustomerId).Select(c => c.NameAr).FirstOrDefaultAsync(token)
                    ?? throw new ValidationFailedException(Messages.CustomerNotFound);
            if (string.IsNullOrWhiteSpace(name)) throw new ValidationFailedException(Messages.ReservationCustomerRequired);
            var days = Math.Max(1, (await SalesCharges.SettingsAsync(_db, token)).ReservationDays);
            var expires = d.ExpiresAt ?? DateTime.UtcNow.AddDays(days);
            if (expires <= DateTime.UtcNow) throw new ValidationFailedException(Messages.ReservationExpiryMustBeFuture);

            var reservation = new VehicleReservation
            {
                VehicleId = vehicle.Id, CustomerId = d.CustomerId, CustomerName = name!, ReservedAt = DateTime.UtcNow, ExpiresAt = expires,
                IsManual = true, Notes = string.IsNullOrWhiteSpace(d.Notes) ? null : d.Notes.Trim(),
            };
            _db.Add(reservation);
            vehicle.Status = VehicleStatus.Reserved;
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("VEHICLE_RESERVED", nameof(VehicleReservation), reservation.Id.ToString(), $"حجز {vehicle.ChassisNumber} للعميل {name} حتى {expires:yyyy-MM-dd}", token);
            return (await ToDtosAsync(new List<VehicleReservation> { reservation }, token))[0];
        }, ct);

    public async Task<VehicleReservationDto> ExtendAsync(Guid id, ExtendVehicleReservationDto d, CancellationToken ct = default)
    {
        var reservation = await _db.Set<VehicleReservation>().FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException(Messages.VehicleReservationNotFound);
        if (reservation.Status != VehicleReservationStatus.Active) throw new ConflictException(Messages.ReservationNotActive);
        if (d.ExpiresAt <= DateTime.UtcNow) throw new ValidationFailedException(Messages.ReservationExpiryMustBeFuture);
        reservation.ExpiresAt = d.ExpiresAt;
        await _db.SaveChangesAsync(ct);
        return (await ToDtosAsync(new List<VehicleReservation> { reservation }, ct))[0];
    }

    public Task<VehicleReservationDto> CancelAsync(Guid id, CancelVehicleReservationDto? request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var reason = request?.Reason;
            var reservation = await _db.Set<VehicleReservation>().FirstOrDefaultAsync(r => r.Id == id, token) ?? throw new NotFoundException(Messages.VehicleReservationNotFound);
            if (reservation.Status != VehicleReservationStatus.Active) throw new ConflictException(Messages.ReservationNotActive);
            // العقد القائم يُلغى من مصدره أولاً
            if (reservation.ContractId.HasValue) throw new ConflictException(Messages.ReservationHeldByContract);
            if (reservation.DepositVoucherId.HasValue) await SettleDepositAsync(reservation, request, token);

            VehicleReservations.Close(reservation, VehicleReservationStatus.Released, string.IsNullOrWhiteSpace(reason) ? "إلغاء يدوي" : reason.Trim());
            var vehicle = await _db.Set<Vehicle>().FirstOrDefaultAsync(v => v.Id == reservation.VehicleId, token);
            if (vehicle is { Status: VehicleStatus.Reserved }) vehicle.Status = VehicleStatus.Available;
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("VEHICLE_RESERVATION_CANCELLED", nameof(VehicleReservation), reservation.Id.ToString(), reservation.CloseReason ?? string.Empty, token);
            return (await ToDtosAsync(new List<VehicleReservation> { reservation }, token))[0];
        }, ct);

    /// <summary>
    /// عربون مفتوح على حجز يُلغى: يُرد للعميل أو يُصادَر إيراداً، حسب اختيار الإلغاء وإلا سياسة إعدادات المعرض.
    /// (معاملة العربون المصادَر ضريبياً يحددها محاسب المنشأة؛ لا ضريبة تُحتسب هنا.)
    /// </summary>
    private async Task SettleDepositAsync(VehicleReservation reservation, CancelVehicleReservationDto? request, CancellationToken ct)
    {
        var voucher = await _db.Set<Voucher>().FirstOrDefaultAsync(v => v.Id == reservation.DepositVoucherId, ct);
        reservation.DepositVoucherId = null;
        if (voucher == null || voucher.DepositStatus != "open") return;

        var settings = await SalesCharges.SettingsAsync(_db, ct);
        var action = string.IsNullOrWhiteSpace(request?.DepositAction)
            ? (settings.DepositCancellationPolicy == DepositCancellationPolicy.Forfeit ? "forfeit" : "refund")
            : request!.DepositAction!.Trim().ToLowerInvariant();
        if (action is not ("refund" or "forfeit")) throw new ValidationFailedException(Messages.DepositActionInvalid);
        await ChargePosting.EnsureAccountsAsync(_db, settings, ct);

        var refund = action == "refund";
        var counter = settings.ForfeitedDepositsAccount;
        if (refund)
        {
            counter = string.IsNullOrWhiteSpace(request?.RefundTreasuryAccountCode) ? voucher.TreasuryAccountCode : request!.RefundTreasuryAccountCode!.Trim();
            if (string.IsNullOrWhiteSpace(counter)) throw new ValidationFailedException(Messages.TreasuryAccountRequired);
        }
        await _posting.PostAsync(new GenericPostingRequest
        {
            Date = DateTime.UtcNow,
            Description = (refund ? "رد عربون " : "مصادرة عربون ") + $"{voucher.VoucherNumber} - {voucher.PartyName} (إلغاء حجز)",
            SourceType = refund ? "car_deposit_refund" : "car_deposit_forfeit", SourceId = voucher.Id, SourceNumber = voucher.VoucherNumber,
            Lines = new List<PostingLine> { new(settings.CustomerDepositsAccount, voucher.Amount, 0), new(counter, 0, voucher.Amount) },
        }, ct);
        voucher.DepositStatus = refund ? "refunded" : "forfeited";
        await _audit.LogAsync(refund ? "DEPOSIT_REFUNDED" : "DEPOSIT_FORFEITED", nameof(Voucher), voucher.Id.ToString(), $"{voucher.VoucherNumber}: {voucher.Amount:0.00}", ct);
    }

    private async Task<List<VehicleReservationDto>> ToDtosAsync(List<VehicleReservation> items, CancellationToken ct)
    {
        var ids = items.Select(r => r.VehicleId).Distinct().ToList();
        var vehicles = await _db.Set<Vehicle>().AsNoTracking().Where(v => ids.Contains(v.Id))
            .Select(v => new { v.Id, v.ChassisNumber, v.BrandNameAr, v.ModelNameAr, v.Year }).ToDictionaryAsync(v => v.Id, ct);
        var now = DateTime.UtcNow;
        return items.Select(r =>
        {
            var dto = Mapper.Map<VehicleReservationDto>(r);
            if (vehicles.TryGetValue(r.VehicleId, out var v))
            {
                dto.ChassisNumber = v.ChassisNumber;
                dto.VehicleDescription = $"{v.BrandNameAr} {v.ModelNameAr} {v.Year}";
            }
            dto.IsOverdue = r.Status == VehicleReservationStatus.Active && r.ExpiresAt < now;
            return dto;
        }).ToList();
    }
}
