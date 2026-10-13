using ERP.Service.Data;

namespace ERP.Service.Services.CarShowroom;

/// <summary>
/// تكلفة السيارة مشتقة من سجل المصاريف: التكاليف الإضافية وتكلفة التجهيز = مجموع حصصها من الأسطر الفعلية المُرسمَلة
/// (مرحلة الاحتفاظ ← التجهيز، وما عداها ← التكاليف الإضافية). ثمن الشراء يبقى حقلاً تحدده دورة الشراء.
/// </summary>
internal static class VehicleCostLedger
{
    /// <summary>حصص تدخل تكلفة السيارة فعلاً.</summary>
    private static IQueryable<AdditionalChargeAllocation> Capitalized(ErpDbContext db)
        => db.Set<AdditionalChargeAllocation>().Where(a => a.Line.Status == ChargeStatus.Actual && a.Line.Treatment == ChargeTreatment.Capitalize);

    private static async Task<Dictionary<(Guid VehicleId, bool Preparation), decimal>> SumsAsync(ErpDbContext db, IReadOnlyCollection<Guid>? vehicleIds, CancellationToken ct)
    {
        var q = Capitalized(db);
        if (vehicleIds != null) q = q.Where(a => vehicleIds.Contains(a.VehicleId));
        var rows = await q.GroupBy(a => new { a.VehicleId, a.Line.Stage })
            .Select(g => new { g.Key.VehicleId, g.Key.Stage, Sum = g.Sum(a => a.Amount) }).ToListAsync(ct);
        return rows.GroupBy(r => (r.VehicleId, r.Stage == ExpenseStage.Holding)).ToDictionary(g => g.Key, g => g.Sum(r => r.Sum));
    }

    /// <summary>
    /// يغطّي بأسطر «رصيد افتتاحي» ما في حقلي التكلفة ولا تقابله أسطر (بيانات سابقة للسجل، أو تكلفة أُدخلت عند إنشاء المركبة)،
    /// فلا تتغير تكلفة أي مركبة حين تصير مشتقة من الأسطر. يُنشئ الفرق فقط، فتكراره لا يضيف شيئاً.
    /// <paramref name="vehicleIds"/> = null لكل مركبات المنشأة. يُرجع عدد الأسطر المُنشأة.
    /// </summary>
    public static async Task<int> EnsureOpeningAsync(ErpDbContext db, IReadOnlyCollection<Guid>? vehicleIds, CancellationToken ct)
    {
        var q = db.Set<Vehicle>().AsNoTracking().Where(v => (v.AdditionalCosts ?? 0) > 0 || (v.PreparationCost ?? 0) > 0);
        if (vehicleIds != null) q = q.Where(v => vehicleIds.Contains(v.Id));
        var vehicles = await q.Select(v => new { v.Id, v.ChassisNumber, v.CreatedAt, Additional = v.AdditionalCosts ?? 0, Preparation = v.PreparationCost ?? 0 }).ToListAsync(ct);
        if (vehicles.Count == 0) return 0;

        var covered = await SumsAsync(db, vehicleIds, ct);
        var gaps = vehicles.SelectMany(v => new[]
            {
                (Vehicle: v, Preparation: false, Gap: v.Additional - covered.GetValueOrDefault((v.Id, false))),
                (Vehicle: v, Preparation: true, Gap: v.Preparation - covered.GetValueOrDefault((v.Id, true))),
            }).Where(g => g.Gap > 0).ToList();
        if (gaps.Count == 0) return 0;

        var types = await OpeningTypesAsync(db, ct);
        var currency = await db.Set<Currency>().Where(c => c.IsBaseCurrency).Select(c => c.Code).FirstOrDefaultAsync(ct) ?? "SAR";
        foreach (var (v, preparation, gap) in gaps)
        {
            var type = types[preparation ? ExpenseTypeSeed.OpeningPreparation : ExpenseTypeSeed.OpeningAdditional];
            var line = new AdditionalChargeLine
            {
                DocumentType = ChargeDocumentTypes.Vehicle, DocumentId = v.Id, DocumentNumber = v.ChassisNumber,
                ExpenseTypeId = type.Id, ExpenseTypeCode = type.Code, ExpenseTypeNameAr = type.NameAr, Stage = type.Stage,
                Date = v.CreatedAt, Amount = gap, CurrencyCode = currency, ExchangeRate = 1, BaseAmount = gap, CostAmount = gap,
                VatCategory = type.VatCategory, Treatment = ChargeTreatment.Capitalize, Status = ChargeStatus.Actual,
                Source = ChargeSource.OpeningBalance, AllocationMethod = ChargeAllocationMethod.Equal,
            };
            line.Allocations.Add(new AdditionalChargeAllocation { VehicleId = v.Id, Amount = gap });
            db.Add(line);
        }
        await db.SaveChangesAsync(ct);
        return gaps.Count;
    }

    private static async Task<Dictionary<string, ExpenseType>> OpeningTypesAsync(ErpDbContext db, CancellationToken ct)
    {
        var codes = new[] { ExpenseTypeSeed.OpeningAdditional, ExpenseTypeSeed.OpeningPreparation };
        var types = await db.Set<ExpenseType>().AsNoTracking().Where(t => codes.Contains(t.Code)).ToDictionaryAsync(t => t.Code, ct);
        if (types.Count == codes.Length) return types;
        await ExpenseTypeSeed.EnsureAsync(db, ct);
        return await db.Set<ExpenseType>().AsNoTracking().Where(t => codes.Contains(t.Code)).ToDictionaryAsync(t => t.Code, ct);
    }

    /// <summary>
    /// يعيد اشتقاق حقلي التكلفة والتكلفة الكلية للمركبات من الأسطر المحفوظة. يُستدعى بعد حفظ تغيير الأسطر، ويحفظ المتصل النتيجة.
    /// </summary>
    public static async Task RecomputeAsync(ErpDbContext db, IReadOnlyCollection<Guid> vehicleIds, CancellationToken ct)
    {
        if (vehicleIds.Count == 0) return;
        var sums = await SumsAsync(db, vehicleIds, ct);
        var tax = await CarTax.LoadAsync(db, ct);
        foreach (var v in await db.Set<Vehicle>().Where(v => vehicleIds.Contains(v.Id)).ToListAsync(ct))
        {
            v.AdditionalCosts = sums.GetValueOrDefault((v.Id, false));
            v.PreparationCost = sums.GetValueOrDefault((v.Id, true));
            VehicleService.Recalculate(v, tax);
        }
    }
}
