using ERP.Service.Data;
using ERP.Service.Services.Accounting;

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

    /// <summary>
    /// السيارة ضمن نطاق الجرد: جرد بمعرض يقارن بمعرض السيارة (سيارة قديمة بلا معرض تُحسب على المعرض الافتراضي)،
    /// وجرد قديم بلا معرض يبقى على مطابقة الموقع النصي.
    /// </summary>
    private static bool InScope(Guid? countWarehouseId, string? countLocation, Vehicle v, Guid? defaultWarehouseId)
        => countWarehouseId.HasValue ? (v.WarehouseId ?? defaultWarehouseId) == countWarehouseId : SameLocation(countLocation, v.Location);

    /// <summary>
    /// جرد آخر المدة للأصناف بلا مستودع يشمل كل المستودعات (رصيد النظام مجموعها)؛ وأي جرد أصناف آخر على مستودع واحد
    /// يُثبَّت على المستند عند الحفظ، فلا يتغير بين الإرسال والاعتماد بتغيّر موقع جلسة المستخدم.
    /// </summary>
    public static bool CoversAllWarehouses(InventoryCountScope scope, InventoryCountType type, Guid? warehouseId)
        => scope == InventoryCountScope.Items && type == InventoryCountType.Periodic && warehouseId == null;

    public static bool CoversAllWarehouses(InventoryCount count) => CoversAllWarehouses(count.Scope, count.CountType, count.WarehouseId);

    /// <summary>تكلفة وحدة الزيادة كما يسجّلها محرك المخزون: المعيارية في سياستها، وإلا متوسط التكلفة.</summary>
    public static decimal SurplusUnitCost(Product p, CostingPolicy policy)
        => policy.Method == CostingMethod.Standard && p.StandardCost.HasValue ? p.StandardCost.Value : p.AverageCost;

    /// <summary>
    /// شروط لا تُفحص إلا عند الإرسال (العدّ قد يكون جارياً عند الحفظ): جرد آخر المدة في الدوري بلا مستودع، والزيادة على صنف متتبَّع
    /// برقم تشغيلة، والزيادة بتكلفة غير صفرية (وإلا دخلت الكمية بلا قيمة ولا قيد).
    /// </summary>
    public static async Task EnsureSubmittableAsync(ErpDbContext db, InventoryCount count, CancellationToken ct)
    {
        if (count.Scope != InventoryCountScope.Items) return;
        if (count.CountType == InventoryCountType.Periodic && count.WarehouseId != null
            && (await PostingAccounts.SettingsAsync(db, ct)).IsPeriodic(SaudiTime.Today))
            throw new ValidationFailedException(Messages.PeriodicCountMustCoverAllWarehouses);

        var surplus = count.Lines.Where(l => l.VarianceQuantity > 0).ToList();
        if (surplus.Count == 0) return;
        var ids = surplus.Select(l => l.ItemId!.Value).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var policy = await db.Set<CostingPolicy>().AsNoTracking().FirstOrDefaultAsync(ct) ?? new CostingPolicy();
        foreach (var line in surplus)
        {
            var p = products[line.ItemId!.Value];
            if (p.TracksLots && line.ItemBatchId == null && string.IsNullOrWhiteSpace(line.BatchNumber))
                throw new ValidationFailedException(string.Format(p.TrackBatches ? Messages.BatchNumberRequired : Messages.ExpiryDateRequired, p.NameAr));
            if (SurplusUnitCost(p, policy) <= 0)
                throw new ValidationFailedException(string.Format(Messages.CountSurplusRequiresCost, p.NameAr));
        }
    }

    internal static Task<Guid?> DefaultWarehouseIdAsync(ErpDbContext db, CancellationToken ct)
        => db.Set<Warehouse>().AsNoTracking().OrderByDescending(w => w.IsDefault).ThenBy(w => w.CreatedAt).Select(w => (Guid?)w.Id).FirstOrDefaultAsync(ct);

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
        var all = CoversAllWarehouses(count);
        var onHand = await WarehouseQuantitiesAsync(db, count.WarehouseId, all, ct);
        var batchOnHand = await BatchQuantitiesAsync(db, count.WarehouseId, all, ct);
        var batchIds = count.Lines.Where(l => l.ItemBatchId.HasValue).Select(l => l.ItemBatchId!.Value).Distinct().ToList();
        var batches = await db.Set<ItemBatch>().AsNoTracking().Where(b => batchIds.Contains(b.Id) || ids.Contains(b.ItemId)).ToListAsync(ct);
        foreach (var line in count.Lines)
        {
            var p = products.GetValueOrDefault(line.ItemId!.Value) ?? throw new ValidationFailedException(Messages.ItemNotFoundInCountLines);
            line.Sku = p.Sku; line.ItemName = p.NameAr; line.Unit = p.Unit;
            line.SystemQuantity = onHand.GetValueOrDefault(p.Id);
            if (p.TracksLots)
            {
                // تشغيلة جديدة معثور عليها برقم قائم تُربط به؛ ورصيد النظام رصيد التشغيلة (صفر للجديدة)، والسطر بلا تشغيلة برصيد الصنف كله
                var number = line.BatchNumber?.Trim();
                // «صلاحية فقط»: التشغيلة بتاريخها (القائمة بالتاريخ نفسه، وإلا رقم مولَّد منه)
                if (string.IsNullOrEmpty(number) && line.ItemBatchId == null && !p.TrackBatches && line.ExpiryDate is { } expiry)
                    number = batches.Where(b => b.ItemId == p.Id && b.ExpiryDate == expiry.Date && b.BatchNumber.StartsWith(ItemBatches.ExpiryPrefix))
                                 .OrderBy(b => b.CreatedAt).Select(b => b.BatchNumber).FirstOrDefault()
                             ?? ItemBatches.ExpiryBatchNumber(expiry.Date);
                var batch = line.ItemBatchId is { } bid ? batches.FirstOrDefault(b => b.Id == bid && b.ItemId == p.Id)
                    : string.IsNullOrEmpty(number) ? null : batches.FirstOrDefault(b => b.ItemId == p.Id && b.BatchNumber == number);
                if (batch != null)
                {
                    line.ItemBatchId = batch.Id; line.BatchNumber = batch.BatchNumber; line.ExpiryDate = batch.ExpiryDate;
                    line.SystemQuantity = batchOnHand.GetValueOrDefault(batch.Id);
                }
                else if (!string.IsNullOrEmpty(number))
                {
                    line.ItemBatchId = null; line.BatchNumber = number; line.SystemQuantity = 0;
                }
                else { line.ItemBatchId = null; line.BatchNumber = null; line.ExpiryDate = null; } // معرّف تشغيلة لا يخص الصنف: سطر بلا تشغيلة
            }
            else { line.ItemBatchId = null; line.BatchNumber = null; line.ExpiryDate = null; }
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

        // بعد ربط التشغيلات: السطر بلا تشغيلة يقارَن برصيد الصنف كله فلا يجتمع مع سطر آخر للصنف، ولا تتكرر التشغيلة
        // (بمعرّفها في سطر وبرقمها في آخر) — وإلا احتُسب الرصيد مرتين وظهرت فروق وهمية
        foreach (var g in count.Lines.GroupBy(l => l.ItemId))
        {
            var mixed = g.Count() > 1 && g.Any(l => l.ItemBatchId == null && l.BatchNumber == null);
            var repeated = g.GroupBy(l => (l.ItemBatchId, Number: l.ItemBatchId == null ? l.BatchNumber : null)).Any(x => x.Count() > 1);
            if (mixed || repeated) throw new ValidationFailedException(string.Format(Messages.DuplicateItemBatchInCount, g.First().ItemName));
        }
    }

    private static async Task RefreshVehiclesAsync(ErpDbContext db, InventoryCount count, CancellationToken ct)
    {
        foreach (var line in count.Lines) line.ChassisNumber = NormalizeVin(line.ChassisNumber);
        var vins = count.Lines.Select(l => l.ChassisNumber!).Distinct().ToList();
        var vehicles = await db.Set<Vehicle>().AsNoTracking().Where(v => vins.Contains(v.ChassisNumber)).ToDictionaryAsync(v => v.ChassisNumber, ct);
        var defaultWarehouseId = await DefaultWarehouseIdAsync(db, ct);
        foreach (var line in count.Lines)
        {
            var v = vehicles.GetValueOrDefault(line.ChassisNumber!);
            var expected = v != null && InStock.Contains(v.Status) && InScope(count.WarehouseId, count.Location, v, defaultWarehouseId);
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

    /// <summary>
    /// أرصدة الأصناف في المستودع المجرود (الافتراضي عند عدم التحديد): الجرد يقارن بما في مستودعه لا بإجمالي المنشأة،
    /// إلا جرد كل المستودعات فيقارن بمجموعها.
    /// </summary>
    private static async Task<Dictionary<Guid, decimal>> WarehouseQuantitiesAsync(ErpDbContext db, Guid? warehouseId, bool allWarehouses, CancellationToken ct)
    {
        var q = db.Set<WarehouseStock>().AsNoTracking();
        if (!allWarehouses)
        {
            var warehouse = await WarehouseStocks.ResolveAsync(db, warehouseId, ct);
            q = q.Where(s => s.WarehouseId == warehouse.Id);
        }
        return await q.GroupBy(s => s.ItemId).Select(g => new { g.Key, Qty = g.Sum(s => s.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
    }

    /// <summary>أرصدة التشغيلات في المستودع المجرود (أو كل المستودعات).</summary>
    private static async Task<Dictionary<Guid, decimal>> BatchQuantitiesAsync(ErpDbContext db, Guid? warehouseId, bool allWarehouses, CancellationToken ct)
    {
        var q = db.Set<BatchStock>().AsNoTracking();
        if (!allWarehouses)
        {
            var warehouse = await WarehouseStocks.ResolveAsync(db, warehouseId, ct);
            q = q.Where(s => s.WarehouseId == warehouse.Id);
        }
        return await q.GroupBy(s => s.ItemBatchId).Select(g => new { g.Key, Qty = g.Sum(s => s.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
    }

    /// <summary>قائمة المتوقَّع من رصيد النظام الحالي لتجهيز جرد جديد (الصنف المتتبَّع: سطر لكل تشغيلة برصيدها).</summary>
    public static async Task<List<InventoryCountLineDto>> SnapshotAsync(ErpDbContext db, InventoryCountSnapshotRequestDto r, CancellationToken ct)
    {
        if (r.Scope == InventoryCountScope.Items)
        {
            // الجرد للأصناف ذات الرصيد: الخدمي والمجمّع والمركّب بلا وجود مخزني
            var q = db.Set<Product>().AsNoTracking().Where(p => p.ItemType == ProductItemTypes.Stock || p.ItemType == ProductItemTypes.Manufactured);
            if (!string.IsNullOrWhiteSpace(r.Category)) q = q.Where(p => p.Category == r.Category);
            var all = CoversAllWarehouses(r.Scope, r.CountType ?? InventoryCountType.Full, r.WarehouseId);
            var onHand = await WarehouseQuantitiesAsync(db, r.WarehouseId, all, ct);
            var batchOnHand = await BatchQuantitiesAsync(db, r.WarehouseId, all, ct);
            var products = (await q.OrderBy(p => p.Sku).ToListAsync(ct)).Where(p => r.IncludeZeroStock || onHand.GetValueOrDefault(p.Id) != 0).ToList();
            var trackedIds = products.Where(p => p.TracksLots).Select(p => p.Id).ToList();
            var batches = (await db.Set<ItemBatch>().AsNoTracking().Where(b => trackedIds.Contains(b.ItemId)).ToListAsync(ct))
                .Where(b => batchOnHand.GetValueOrDefault(b.Id) != 0)
                .OrderBy(b => b.ExpiryDate == null).ThenBy(b => b.ExpiryDate).ThenBy(b => b.BatchNumber).ToLookup(b => b.ItemId);
            var lines = new List<InventoryCountLineDto>();
            foreach (var p in products)
            {
                if (p.TracksLots && batches[p.Id].Any())
                    lines.AddRange(batches[p.Id].Select(b => new InventoryCountLineDto
                    {
                        ItemId = p.Id, Sku = p.Sku, ItemName = p.NameAr, Unit = p.Unit, ItemBatchId = b.Id, BatchNumber = b.BatchNumber, ExpiryDate = b.ExpiryDate,
                        SystemQuantity = batchOnHand.GetValueOrDefault(b.Id), UnitCost = p.AverageCost,
                    }));
                else
                    lines.Add(new InventoryCountLineDto
                    {
                        ItemId = p.Id, Sku = p.Sku, ItemName = p.NameAr, Unit = p.Unit, SystemQuantity = onHand.GetValueOrDefault(p.Id), UnitCost = p.AverageCost,
                    });
            }
            return lines;
        }

        var vehicles = await db.Set<Vehicle>().AsNoTracking().Where(v => InStock.Contains(v.Status)).OrderBy(v => v.ChassisNumber).ToListAsync(ct);
        var defaultId = await DefaultWarehouseIdAsync(db, ct);
        return vehicles.Where(v => InScope(r.WarehouseId, r.Location, v, defaultId)).Select(v => new InventoryCountLineDto
        {
            VehicleId = v.Id, ChassisNumber = v.ChassisNumber, ItemName = Describe(v), SystemQuantity = 1, UnitCost = v.TotalCost,
        }).ToList();
    }
}
