using ERP.Service.Data;

namespace ERP.Service.Services.CarShowroom;

/// <summary>
/// مصاريف أمر الشراء في سجل المصاريف: رسوم حقول الأمر (جمارك، أرضيات ميناء) تُمثَّل بأسطر يديرها الأمر،
/// وأسطر الأمر كلها تُوزَّع على سياراته (في الطريق ثم المستلمة). الحصص مجموعها يساوي المبلغ تماماً، وفرق التقريب على آخر سيارة.
/// </summary>
internal static class ProcurementCharges
{
    private static readonly (string Code, Func<CarProcurementOrder, decimal?> Fee)[] FeeFields =
    {
        ("CUSTOMS_DUTY", o => o.CustomsDutyFee),
        ("PORT_STORAGE", o => o.PortStorageFee),
    };

    public static IQueryable<AdditionalChargeLine> OrderLines(ErpDbContext db, Guid orderId)
        => db.Set<AdditionalChargeLine>().Where(l => l.DocumentType == ChargeDocumentTypes.ProcurementOrder && l.DocumentId == orderId);

    /// <summary>
    /// يطابق أسطر رسوم الأمر مع حقليه: تقديرية قبل الاستلام وفعلية بعده. سطر استبدله سند تكاليف لا يُمسّ.
    /// <paramref name="createMissing"/> = false لأمر استُلم قبل وجود سجل المصاريف: رسومه محمّلة على مركباته أصلاً فلا تُكرَّر.
    /// </summary>
    public static async Task SyncOrderFeesAsync(ErpDbContext db, CarProcurementOrder order, bool actual, bool createMissing, CancellationToken ct)
    {
        var existing = await OrderLines(db, order.Id).Include(l => l.Allocations).Where(l => l.Source == ChargeSource.OrderFields).ToListAsync(ct);
        Dictionary<string, ExpenseType>? types = null;
        foreach (var (code, fee) in FeeFields)
        {
            var amount = fee(order) ?? 0;
            var line = existing.FirstOrDefault(l => l.ExpenseTypeCode == code);
            if (line is { Status: ChargeStatus.Replaced or ChargeStatus.Cancelled }) continue;
            if (amount <= 0)
            {
                if (line == null) continue;
                db.RemoveRange(line.Allocations);
                db.Remove(line);
                continue;
            }
            if (line == null)
            {
                if (!createMissing) continue;
                types ??= await FeeTypesAsync(db, ct);
                var type = types[code];
                line = new AdditionalChargeLine
                {
                    DocumentType = ChargeDocumentTypes.ProcurementOrder, DocumentId = order.Id,
                    ExpenseTypeId = type.Id, ExpenseTypeCode = type.Code, ExpenseTypeNameAr = type.NameAr, Stage = type.Stage,
                    VatCategory = type.VatCategory, Treatment = ChargeTreatment.Capitalize, Source = ChargeSource.OrderFields,
                    AllocationMethod = ChargeAllocationMethod.Equal, ExchangeRate = 1,
                    CurrencyCode = await db.Set<Currency>().Where(c => c.IsBaseCurrency).Select(c => c.Code).FirstOrDefaultAsync(ct) ?? "SAR",
                };
                db.Add(line);
            }
            // رسوم الحقلين بالعملة الأساسية كما كانت تُحمَّل على المركبات
            line.DocumentNumber = order.OrderNumber; line.Date = order.Date;
            line.Amount = amount; line.BaseAmount = amount; line.CostAmount = amount;
            line.Status = actual ? ChargeStatus.Actual : ChargeStatus.Estimated;
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task<Dictionary<string, ExpenseType>> FeeTypesAsync(ErpDbContext db, CancellationToken ct)
    {
        var codes = FeeFields.Select(f => f.Code).ToArray();
        var types = await db.Set<ExpenseType>().AsNoTracking().Where(t => codes.Contains(t.Code)).ToDictionaryAsync(t => t.Code, ct);
        if (types.Count == codes.Length) return types;
        await ExpenseTypeSeed.EnsureAsync(db, ct);
        return await db.Set<ExpenseType>().AsNoTracking().Where(t => codes.Contains(t.Code)).ToDictionaryAsync(t => t.Code, ct);
    }

    /// <summary>ترتيب ثابت لسيارات الأمر، فيقع فرق التقريب دائماً على السيارة نفسها (الأخيرة).</summary>
    public static Task<List<Guid>> VehicleIdsAsync(ErpDbContext db, Guid orderId, CancellationToken ct)
        => db.Set<Vehicle>().Where(v => v.ProcurementOrderId == orderId && v.Status != VehicleStatus.Cancelled)
            .OrderBy(v => v.CreatedAt).ThenBy(v => v.ChassisNumber).Select(v => v.Id).ToListAsync(ct);

    /// <summary>
    /// يعيد توزيع أسطر الأمر القائمة على سياراته ثم يشتق تكلفتها: رسوم الحقلين على كل السيارات بالتساوي،
    /// والسطر الموزَّع على بعضها يبقى عليها بطريقته، وغير الموزَّع يُوزَّع على الكل. التوزيع اليدوي يبقى كما أدخله المستخدم.
    /// </summary>
    public static async Task ReallocateAsync(ErpDbContext db, Guid orderId, CancellationToken ct)
    {
        var vehicleIds = await VehicleIdsAsync(db, orderId, ct);
        if (vehicleIds.Count == 0) return;
        var weights = await ChargeShares.LoadAsync(db, vehicleIds, ct);
        var lines = await OrderLines(db, orderId).Include(l => l.Allocations)
            .Where(l => l.Status == ChargeStatus.Estimated || l.Status == ChargeStatus.Actual).ToListAsync(ct);

        foreach (var line in lines)
        {
            if (line.AllocationMethod == ChargeAllocationMethod.Manual && line.Source != ChargeSource.OrderFields) continue;
            var current = line.Allocations.Select(a => a.VehicleId).ToHashSet();
            var targets = line.Source == ChargeSource.OrderFields || current.Count == 0 ? vehicleIds : vehicleIds.Where(current.Contains).ToList();
            if (targets.Count == 0) continue;

            decimal[] shares;
            try { shares = ChargeShares.Compute(line.AllocationMethod, line.CostAmount, targets.Select(id => weights[id]).ToList()); }
            catch (ValidationFailedException) { continue; } // تنقص بيانات التوزيع (حجم/وزن/قيمة): يبقى السطر كما هو حتى تُستكمل

            // الحصص القائمة تُحدَّث في مكانها (تحفظ ما سُجِّل عليها من ترحيل)، ويُضاف الناقص ويُحذف الزائد
            var byVehicle = line.Allocations.ToDictionary(a => a.VehicleId);
            for (var i = 0; i < targets.Count; i++)
            {
                if (byVehicle.Remove(targets[i], out var existing)) { existing.Amount = shares[i]; continue; }
                var allocation = new AdditionalChargeAllocation { VehicleId = targets[i], Amount = shares[i] };
                line.Allocations.Add(allocation);
                db.Add(allocation);
            }
            foreach (var stale in byVehicle.Values)
            {
                line.Allocations.Remove(stale);
                db.Remove(stale);
            }
        }
        await db.SaveChangesAsync(ct);
        await VehicleCostLedger.RecomputeAsync(db, vehicleIds, ct);
        await db.SaveChangesAsync(ct);
    }
}
