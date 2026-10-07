using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// حساب أسطر الجرد من بيانات النظام الحالية: رصيد الصنف وتكلفته، أو وجود المركبة في المخزون وموقعها.
/// المكان الوحيد لقواعد الفرق وأثره المتوقع، تستخدمه شاشة التجهيز والحفظ والإرسال.
/// </summary>
internal static class InventoryCountLines
{
    /// <summary>المركبة داخل المخزون الفعلي للمعرض (المحجوزة لم تُسلَّم بعد).</summary>
    public static readonly VehicleStatus[] InStock = { VehicleStatus.Available, VehicleStatus.Reserved };

    public static string NormalizeVin(string? vin) => (vin ?? string.Empty).Trim().ToUpperInvariant();

    private static bool SameLocation(string? countLocation, string vehicleLocation)
        => string.IsNullOrWhiteSpace(countLocation) || string.Equals(countLocation.Trim(), vehicleLocation.Trim(), StringComparison.OrdinalIgnoreCase);

    public static string Describe(Vehicle v) => $"{v.BrandNameAr} {v.ModelNameAr} {v.Year}".Trim();

    /// <summary>يعيد احتساب رصيد النظام والتكلفة والفرق لكل سطر ثم ملخص المستند.</summary>
    public static async Task RefreshAsync(ErpDbContext db, InventoryCount count, CancellationToken ct)
    {
        if (count.Scope == InventoryCountScope.Items) await RefreshItemsAsync(db, count, ct);
        else await RefreshVehiclesAsync(db, count, ct);
        Summarize(count);
    }

    private static async Task RefreshItemsAsync(ErpDbContext db, InventoryCount count, CancellationToken ct)
    {
        var ids = count.Lines.Select(l => l.ItemId!.Value).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var onHand = await WarehouseQuantitiesAsync(db, count.WarehouseId, ct);
        foreach (var line in count.Lines)
        {
            var p = products.GetValueOrDefault(line.ItemId!.Value) ?? throw new ValidationFailedException(Messages.ItemNotFoundInCountLines);
            line.Sku = p.Sku; line.ItemName = p.NameAr; line.Unit = p.Unit;
            line.SystemQuantity = onHand.GetValueOrDefault(p.Id);
            line.UnitCost = p.AverageCost;
            line.VarianceQuantity = line.CountedQuantity.HasValue ? line.CountedQuantity.Value - line.SystemQuantity : 0;
            line.VarianceValue = Math.Round(line.VarianceQuantity * line.UnitCost, 2);
            line.Resolution = line.VarianceQuantity switch
            {
                > 0 => InventoryCountLineResolution.StockIn,
                < 0 => InventoryCountLineResolution.StockOut,
                _ => InventoryCountLineResolution.None,
            };
        }
    }

    private static async Task RefreshVehiclesAsync(ErpDbContext db, InventoryCount count, CancellationToken ct)
    {
        foreach (var line in count.Lines) line.ChassisNumber = NormalizeVin(line.ChassisNumber);
        var vins = count.Lines.Select(l => l.ChassisNumber!).Distinct().ToList();
        var vehicles = await db.Set<Vehicle>().AsNoTracking().Where(v => vins.Contains(v.ChassisNumber)).ToDictionaryAsync(v => v.ChassisNumber, ct);
        foreach (var line in count.Lines)
        {
            var v = vehicles.GetValueOrDefault(line.ChassisNumber!);
            var expected = v != null && InStock.Contains(v.Status) && SameLocation(count.Location, v.Location);
            line.VehicleId = v?.Id;
            line.ItemName = v != null ? Describe(v) : "مركبة غير مسجّلة في النظام";
            line.SystemQuantity = expected ? 1 : 0;
            line.UnitCost = v?.TotalCost ?? 0;
            line.VarianceQuantity = line.CountedQuantity.HasValue ? line.CountedQuantity.Value - line.SystemQuantity : 0;
            line.Resolution = line.VarianceQuantity switch
            {
                < 0 => InventoryCountLineResolution.VehicleWrittenOff,
                > 0 when v == null => InventoryCountLineResolution.RequiresRegistration,
                > 0 when v.Status == VehicleStatus.WrittenOff => InventoryCountLineResolution.VehicleReinstated,
                > 0 when InStock.Contains(v.Status) => InventoryCountLineResolution.VehicleRelocated,
                > 0 => InventoryCountLineResolution.NotAdjusted, // مباعة ولم تُسلَّم بعد: فرق مُثبت دون أثر مخزني
                _ => InventoryCountLineResolution.None,
            };
            line.VarianceValue = line.Resolution switch
            {
                InventoryCountLineResolution.VehicleWrittenOff => -line.UnitCost,
                InventoryCountLineResolution.VehicleReinstated => line.UnitCost,
                _ => 0,
            };
        }
    }

    public static void Summarize(InventoryCount count)
    {
        count.TotalLines = count.Lines.Count;
        count.CountedLines = count.Lines.Count(l => l.CountedQuantity.HasValue);
        count.VarianceLines = count.Lines.Count(l => l.VarianceQuantity != 0);
        count.TotalSurplusValue = count.Lines.Where(l => l.VarianceValue > 0).Sum(l => l.VarianceValue);
        count.TotalShortageValue = -count.Lines.Where(l => l.VarianceValue < 0).Sum(l => l.VarianceValue);
        count.NetVarianceValue = count.TotalSurplusValue - count.TotalShortageValue;
    }

    /// <summary>أرصدة الأصناف في المستودع المجرود (الافتراضي عند عدم التحديد): الجرد يقارن بما في مستودعه لا بإجمالي المنشأة.</summary>
    private static async Task<Dictionary<Guid, decimal>> WarehouseQuantitiesAsync(ErpDbContext db, Guid? warehouseId, CancellationToken ct)
    {
        var warehouse = await WarehouseStocks.ResolveAsync(db, warehouseId, ct);
        return await db.Set<WarehouseStock>().AsNoTracking().Where(s => s.WarehouseId == warehouse.Id).ToDictionaryAsync(s => s.ItemId, s => s.Quantity, ct);
    }

    /// <summary>قائمة المتوقَّع من رصيد النظام الحالي لتجهيز جرد جديد.</summary>
    public static async Task<List<InventoryCountLineDto>> SnapshotAsync(ErpDbContext db, InventoryCountSnapshotRequestDto r, CancellationToken ct)
    {
        if (r.Scope == InventoryCountScope.Items)
        {
            var q = db.Set<Product>().AsNoTracking();
            if (!string.IsNullOrWhiteSpace(r.Category)) q = q.Where(p => p.Category == r.Category);
            var onHand = await WarehouseQuantitiesAsync(db, r.WarehouseId, ct);
            return (await q.OrderBy(p => p.Sku).ToListAsync(ct)).Where(p => r.IncludeZeroStock || onHand.GetValueOrDefault(p.Id) != 0).Select(p => new InventoryCountLineDto
            {
                ItemId = p.Id, Sku = p.Sku, ItemName = p.NameAr, Unit = p.Unit, SystemQuantity = onHand.GetValueOrDefault(p.Id), UnitCost = p.AverageCost,
            }).ToList();
        }

        var vehicles = await db.Set<Vehicle>().AsNoTracking().Where(v => InStock.Contains(v.Status)).OrderBy(v => v.ChassisNumber).ToListAsync(ct);
        return vehicles.Where(v => SameLocation(r.Location, v.Location)).Select(v => new InventoryCountLineDto
        {
            VehicleId = v.Id, ChassisNumber = v.ChassisNumber, ItemName = Describe(v), SystemQuantity = 1, UnitCost = v.TotalCost,
        }).ToList();
    }
}
