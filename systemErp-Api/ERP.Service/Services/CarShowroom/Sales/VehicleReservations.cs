using ERP.Service.Data;

namespace ERP.Service.Services.CarShowroom;

/// <summary>
/// سجل حجز السيارات: حجز فعّال واحد لكل سيارة يمسكه عقد معتمد أو عربون مفتوح أو حجز يدوي. تستدعيه دورة البيع وسند العربون
/// عند الحجز والفك، فتبقى حالة السيارة (Reserved) وسجل الحجز متطابقين.
/// </summary>
internal static class VehicleReservations
{
    public static Task<VehicleReservation?> ActiveAsync(ErpDbContext db, Guid vehicleId, CancellationToken ct)
        => db.Set<VehicleReservation>().FirstOrDefaultAsync(r => r.VehicleId == vehicleId && r.Status == VehicleReservationStatus.Active, ct);

    /// <summary>يسجّل أن العقد أو العربون يمسك السيارة: ينشئ الحجز أو يربطه بالقائم، ويعلّم السيارة Reserved إن كانت متاحة.</summary>
    public static async Task HoldAsync(ErpDbContext db, Guid vehicleId, Guid? customerId, string? customerName, Guid? contractId, Guid? depositVoucherId, CancellationToken ct)
    {
        var reservation = await ActiveAsync(db, vehicleId, ct);
        if (reservation == null)
        {
            var days = Math.Max(1, (await SalesCharges.SettingsAsync(db, ct)).ReservationDays);
            reservation = new VehicleReservation
            {
                VehicleId = vehicleId, CustomerId = customerId, CustomerName = customerName ?? string.Empty,
                ReservedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(days),
            };
            db.Add(reservation);
        }
        reservation.CustomerId ??= customerId;
        if (string.IsNullOrWhiteSpace(reservation.CustomerName)) reservation.CustomerName = customerName ?? string.Empty;
        if (contractId.HasValue) reservation.ContractId = contractId;
        if (depositVoucherId.HasValue) reservation.DepositVoucherId = depositVoucherId;

        var vehicle = await db.Set<Vehicle>().FirstOrDefaultAsync(v => v.Id == vehicleId, ct);
        if (vehicle is { Status: VehicleStatus.Available }) vehicle.Status = VehicleStatus.Reserved;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// العقد أُلغي أو العربون حُذف: يُفك ارتباطه بالحجز. الحجز الذي لم يبقَ ما يمسكه (وليس يدوياً) يُغلق.
    /// يُرجع true إن بقي حجز فعّال يمسك السيارة.
    /// </summary>
    public static async Task<bool> UnholdAsync(ErpDbContext db, Guid vehicleId, Guid? contractId, Guid? depositVoucherId, string reason, CancellationToken ct)
    {
        var reservation = await ActiveAsync(db, vehicleId, ct);
        if (reservation == null) return false;
        if (contractId.HasValue && reservation.ContractId == contractId) reservation.ContractId = null;
        if (depositVoucherId.HasValue && reservation.DepositVoucherId == depositVoucherId) reservation.DepositVoucherId = null;
        var stillHeld = reservation.ContractId.HasValue || reservation.DepositVoucherId.HasValue || reservation.IsManual;
        if (!stillHeld) Close(reservation, VehicleReservationStatus.Released, reason);
        await db.SaveChangesAsync(ct);
        return stillHeld;
    }

    /// <summary>بيعت السيارة: الحجز تحوّل إلى بيع.</summary>
    public static async Task ConvertAsync(ErpDbContext db, Guid vehicleId, CancellationToken ct)
    {
        var reservation = await ActiveAsync(db, vehicleId, ct);
        if (reservation == null) return;
        Close(reservation, VehicleReservationStatus.Converted, "بيعت السيارة");
        await db.SaveChangesAsync(ct);
    }

    public static void Close(VehicleReservation reservation, VehicleReservationStatus status, string? reason)
    {
        reservation.Status = status; reservation.ClosedAt = DateTime.UtcNow; reservation.CloseReason = reason;
    }

    /// <summary>
    /// يفك الحجوزات التي انتهت مدتها ولا يمسكها عربون مفتوح ولا عقد قائم، ويعيد سياراتها للعرض.
    /// (لا مُجدوِل في النظام: يُستدعى عند عرض السيارات المتاحة والحجوزات وعند اختيار سيارة لعقد.)
    /// </summary>
    public static async Task ExpireDueAsync(ErpDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var due = await db.Set<VehicleReservation>()
            .Where(r => r.Status == VehicleReservationStatus.Active && r.ExpiresAt < now && r.ContractId == null && r.DepositVoucherId == null).ToListAsync(ct);
        if (due.Count == 0) return;
        var vehicleIds = due.Select(r => r.VehicleId).ToList();
        var vehicles = await db.Set<Vehicle>().Where(v => vehicleIds.Contains(v.Id) && v.Status == VehicleStatus.Reserved).ToListAsync(ct);
        foreach (var reservation in due) Close(reservation, VehicleReservationStatus.Expired, "انتهت مدة الحجز");
        foreach (var vehicle in vehicles) vehicle.Status = VehicleStatus.Available;
        await db.SaveChangesAsync(ct);
    }
}
