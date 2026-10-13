using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

/// <summary>حصص السيارات من مصروف واحد حسب طريقة التوزيع. يجمع أوزان السيارات (القيمة/الحجم/الوزن) ثم يوزّع بـ <see cref="ChargeAllocator"/>.</summary>
internal static class ChargeShares
{
    public sealed record VehicleWeights(Guid Id, string ChassisNumber, VehicleStatus Status, decimal Value, decimal? Volume, decimal? Weight);

    public static async Task<Dictionary<Guid, VehicleWeights>> LoadAsync(ErpDbContext db, IReadOnlyCollection<Guid> vehicleIds, CancellationToken ct)
    {
        var vehicles = await db.Set<Vehicle>().AsNoTracking().Where(v => vehicleIds.Contains(v.Id))
            .Select(v => new VehicleWeights(v.Id, v.ChassisNumber, v.Status, v.PurchasePrice ?? 0, v.VolumeCbm, v.WeightKg)).ToDictionaryAsync(v => v.Id, ct);
        if (vehicles.Count != vehicleIds.Distinct().Count()) throw new ValidationFailedException(Messages.VehicleNotFound);
        return vehicles;
    }

    /// <summary>الحصص بترتيب السيارات. <paramref name="manual"/> مبالغ التوزيع اليدوي بالترتيب نفسه.</summary>
    public static decimal[] Compute(ChargeAllocationMethod method, decimal costAmount, IReadOnlyList<VehicleWeights> vehicles, IReadOnlyList<decimal>? manual = null)
    {
        if (vehicles.Count == 0) return Array.Empty<decimal>();
        if (vehicles.Count == 1) return new[] { costAmount };
        switch (method)
        {
            case ChargeAllocationMethod.Equal:
                return ChargeAllocator.Equal(costAmount, vehicles.Count);
            case ChargeAllocationMethod.ByValue:
                if (vehicles.Sum(v => v.Value) <= 0) throw new ValidationFailedException(Messages.ChargeAllocationByValueNeedsPrices);
                return ChargeAllocator.Allocate(costAmount, vehicles.Select(v => v.Value).ToList());
            case ChargeAllocationMethod.ByVolume:
                if (vehicles.Any(v => (v.Volume ?? 0) <= 0)) throw new ValidationFailedException(Messages.ChargeAllocationNeedsVolume);
                return ChargeAllocator.Allocate(costAmount, vehicles.Select(v => v.Volume!.Value).ToList());
            case ChargeAllocationMethod.ByWeight:
                if (vehicles.Any(v => (v.Weight ?? 0) <= 0)) throw new ValidationFailedException(Messages.ChargeAllocationNeedsWeight);
                return ChargeAllocator.Allocate(costAmount, vehicles.Select(v => v.Weight!.Value).ToList());
            case ChargeAllocationMethod.Manual:
                var amounts = (manual ?? Array.Empty<decimal>()).Select(DocumentPricing.Round).ToArray();
                if (amounts.Length != vehicles.Count || amounts.Any(a => a < 0) || amounts.Sum() != costAmount)
                    throw new ValidationFailedException(string.Format(Messages.ChargeManualAllocationMismatch, amounts.Sum(), costAmount));
                return amounts;
            default:
                throw new ValidationFailedException(Messages.ExpenseTypeValuesInvalid);
        }
    }

    /// <summary>ما يُحمَّل فعلاً بالعملة الأساسية: المبلغ + الضريبة غير المستردة؛ وضريبة المدخلات المستردة لا تُحمَّل.</summary>
    public static decimal CostAmount(ExpenseType type, decimal baseAmount, decimal baseVat)
        => type.IsInputTax ? (type.IsVatRecoverable ? 0 : baseAmount) : baseAmount + (type.IsVatRecoverable ? 0 : baseVat);

    /// <summary>عملة السطر/السند وسعر صرفها: العملة الأساسية بسعر 1، وغيرها يجب أن تكون معرّفة وبسعر موجب.</summary>
    public static async Task<(string Code, decimal Rate)> ResolveCurrencyAsync(ErpDbContext db, string? currencyCode, decimal exchangeRate, CancellationToken ct)
    {
        var baseCode = await db.Set<Currency>().AsNoTracking().Where(c => c.IsBaseCurrency).Select(c => c.Code).FirstOrDefaultAsync(ct) ?? "SAR";
        var code = string.IsNullOrWhiteSpace(currencyCode) ? baseCode : currencyCode.Trim().ToUpperInvariant();
        if (code == baseCode) return (code, 1);
        if (!await db.Set<Currency>().AnyAsync(c => c.Code == code, ct)) throw new ValidationFailedException(Messages.ChargeCurrencyNotFound);
        if (exchangeRate <= 0) throw new ValidationFailedException(Messages.ExchangeRateMustBePositive);
        return (code, exchangeRate);
    }
}
