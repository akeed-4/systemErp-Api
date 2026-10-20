using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

/// <summary>
/// تكلفة السيارة المباعة لا تُعدَّل إلا بقيد تعديل موثق: مصروف فعلي يصلها بعد البيع (أو تعديله) يتطلب صلاحية مستقلة وسبباً،
/// ويُحمَّل على تكلفة المبيعات مباشرة لا على المخزون، ويُسجَّل له قيد تعديل برقمه ومن اعتمده.
/// السيارة المشطوبة أو الملغى شراؤها لا تُمسّ تكلفتها إطلاقاً.
/// </summary>
internal static class SoldVehicleAdjustments
{
    /// <summary>
    /// يتحقق من جواز المساس بتكلفة هذه السيارات ويُرجع المباعة منها. <paramref name="system"/> = حدث من النظام (نجاح عملية تم)
    /// يُوثَّق تلقائياً دون صلاحية مستخدم.
    /// </summary>
    public static async Task<List<Guid>> EnsureAllowedAsync(ErpDbContext db, IPermissionService permissions, IReadOnlyCollection<Guid> vehicleIds,
        string? reason, bool system, CancellationToken ct)
    {
        if (vehicleIds.Count == 0) return new();
        var restricted = await db.Set<Vehicle>().AsNoTracking()
            .Where(v => vehicleIds.Contains(v.Id) && (v.Status == VehicleStatus.Sold || v.Status == VehicleStatus.WrittenOff || v.Status == VehicleStatus.Cancelled))
            .Select(v => new { v.Id, v.ChassisNumber, v.Status }).ToListAsync(ct);
        var locked = restricted.FirstOrDefault(v => v.Status != VehicleStatus.Sold);
        if (locked != null) throw new ConflictException(string.Format(Messages.CannotChangeSoldVehicleCost, locked.ChassisNumber));
        if (restricted.Count == 0 || system) return restricted.Select(v => v.Id).ToList();

        if (!await permissions.HasPermissionAsync(PermissionService.SoldVehicleAdjustmentScreenId, ScreenAction.Approve, ct))
            throw new ForbiddenException(string.Format(Messages.SoldVehicleAdjustmentForbidden, restricted[0].ChassisNumber));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ValidationFailedException(string.Format(Messages.SoldVehicleAdjustmentReasonRequired, restricted[0].ChassisNumber));
        return restricted.Select(v => v.Id).ToList();
    }

    /// <summary>يسجّل قيد تعديل لكل سيارة مباعة تغيّرت تكلفتها (الفرق بين ما كان عليها وما صار).</summary>
    public static async Task RecordAsync(ErpDbContext db, INumberSequenceService numbers, string? approvedBy, IEnumerable<KeyValuePair<Guid, decimal>> deltas,
        string reason, string source, Guid? chargeLineId, Guid? journalEntryId, CancellationToken ct)
    {
        var changed = deltas.Where(d => d.Value != 0).ToList();
        if (changed.Count == 0) return;
        foreach (var (vehicleId, amount) in changed)
        {
            var contractId = await db.Set<CarSalesContract>().Where(c => c.VehicleId == vehicleId && c.Status == SalesContractStatus.Invoiced)
                .OrderByDescending(c => c.Date).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
            db.Add(new VehicleCostAdjustment
            {
                AdjustmentNumber = await numbers.NextAsync("vehicle_cost_adjustment", "VCA-", ct),
                VehicleId = vehicleId, ContractId = contractId, ChargeLineId = chargeLineId, Date = SaudiTime.Today, Amount = amount,
                Reason = reason.Trim(), Source = source, ApprovedBy = approvedBy, JournalEntryId = journalEntryId,
            });
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// مرتجع البيع: مصاريف وصلت بعد البيع حُمّلت على تكلفة المبيعات فتبقى مصروفاً هناك، فتخرج من تكلفة السيارة العائدة للمخزون
    /// (وإلا حُسبت مرة ثانية عند بيعها من جديد). لا قيد هنا: أثرها المحاسبي على تكلفة المبيعات قائم. يعيد المتصل اشتقاق التكلفة بعد الحفظ.
    /// </summary>
    public static async Task ExpenseLateChargesAsync(ErpDbContext db, IReadOnlyCollection<Guid> vehicleIds, CancellationToken ct)
    {
        var lateLines = (await db.Set<AdditionalChargeAllocation>().Include(a => a.Line)
            .Where(a => vehicleIds.Contains(a.VehicleId) && a.PostedToCogs && a.Line.Status == ChargeStatus.Actual && a.Line.Treatment == ChargeTreatment.Capitalize)
            .ToListAsync(ct)).Select(a => a.Line).Distinct().ToList();
        foreach (var line in lateLines)
        {
            line.Treatment = ChargeTreatment.Expense;
            line.Notes = string.IsNullOrWhiteSpace(line.Notes) ? "مصروف بعد البيع: بقي مصروفاً عند مرتجع البيع" : $"{line.Notes} - بقي مصروفاً عند مرتجع البيع";
        }
    }

    /// <summary>فرق ما يدخل تكلفة كل سيارة بين حالتين (قبل/بعد) للسيارات المباعة فقط.</summary>
    public static Dictionary<Guid, decimal> Deltas(IReadOnlyDictionary<Guid, decimal> before, IReadOnlyDictionary<Guid, decimal> after, IEnumerable<Guid> soldIds)
        => soldIds.Distinct().ToDictionary(id => id, id => after.GetValueOrDefault(id) - before.GetValueOrDefault(id));
}
