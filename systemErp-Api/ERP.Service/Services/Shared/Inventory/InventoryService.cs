using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// محرك المخزون والتكلفة المشترك (مبيعات، مشتريات، POS، جرد). يسجّل الحركة ويحدّث رصيد الصنف وتكلفته
/// وفق سياسة المنشأة: متوسط متحرك، FIFO، آخر شراء، أو تكلفة معيارية. لا يبني قيوداً محاسبية:
/// الوحدة المستدعية تمرّر التكلفة الناتجة (UnitCost) إلى IAccountingPostingService.
/// </summary>
public class InventoryService : IInventoryService
{
    private readonly ErpDbContext _db;
    private readonly ERP.Core.Contracts.Accounting.IPeriodLock _periods;
    public InventoryService(ErpDbContext db, ERP.Core.Contracts.Accounting.IPeriodLock periods) { _db = db; _periods = periods; }

    public async Task<StockMovementDto> RecordMovementAsync(RecordStockMovementDto r, CancellationToken ct = default)
    {
        if (r.Quantity <= 0) throw new ValidationFailedException(Messages.QuantityMustBePositive);
        if (r.UnitCost < 0) throw new ValidationFailedException(Messages.UnitCostCannotBeNegative);
        await _periods.EnsureOpenAsync(r.Date ?? SaudiTime.Today, ct);
        await Accounting.PeriodicInventory.EnsureMovementDateAllowedAsync(_db, r.Date ?? SaudiTime.Today, ct);

        var product = await _db.Set<Product>().FirstOrDefaultAsync(p => p.Id == r.ItemId, ct)
            ?? throw new NotFoundException(Messages.ProductNotFound);
        if (r.Type is StockMovementType.TransferIn or StockMovementType.TransferOut) throw new ValidationFailedException(Messages.ManualAdjustmentTypeOnly);
        // الخدمي والمجمّع والمركّب بلا وجود مخزني: المجمّع والمركّب يُصرفان من مكوناتهما
        if (!ProductItemTypes.TracksStock(product.ItemType))
            throw new ValidationFailedException($"«{product.NameAr}» صنف بلا رصيد مخزني (خدمي أو مجمّع أو مركّب): لا حركة مخزنية عليه.");
        var warehouse = await WarehouseStocks.ResolveAsync(_db, r.WarehouseId, ct);

        var policy = await _db.Set<CostingPolicy>().AsNoTracking().FirstOrDefaultAsync(ct) ?? new CostingPolicy();
        var isIn = r.Type is StockMovementType.InPurchase or StockMovementType.AdjustmentIn;
        var valueBefore = InventoryValuation.Value(product);

        var movement = new StockMovement
        {
            ItemId = product.Id, ItemName = product.NameAr, WarehouseId = warehouse.Id,
            Date = r.Date ?? SaudiTime.Today, Type = r.Type, Quantity = r.Quantity,
            UnitPrice = r.UnitPrice, ReferenceNumber = r.ReferenceNumber, Notes = r.Notes,
            SourceType = r.SourceType, SourceId = r.SourceId,
        };
        _db.Add(movement); // معرّفها يُولَّد الآن: توزيع التشغيلات يُسجَّل عليها
        var batches = await ApplyBatchesAsync(product, movement, r, warehouse.Id, policy, ct);

        if (isIn)
        {
            var baseStock = Math.Max(product.CurrentStock, 0);
            movement.UnitCost = r.UnitCost;
            movement.RemainingQuantity = r.Quantity;

            if (policy.Method == CostingMethod.Standard && product.StandardCost.HasValue)
            {
                // المعيارية: الإضافة بالتكلفة المعيارية، والفرق عن التكلفة الفعلية يظهر في ValueChange فيُرحَّل فرقاً
                product.AverageCost = product.StandardCost.Value;
            }
            else if (KeepsCost(policy, r.Type, product.AverageCost))
            {
                // تحديث التكلفة عند الشراء موقوف: الصنف بتكلفته الحالية، وفرق سعر الشراء يظهر في ValueChange فيُرحَّل لحساب الانحرافات
            }
            else if (policy.Method is CostingMethod.MovingAverage or CostingMethod.FIFO or CostingMethod.Standard)
            {
                var newQty = baseStock + r.Quantity;
                product.AverageCost = newQty == 0 ? r.UnitCost
                    : Math.Round((baseStock * product.AverageCost + r.Quantity * r.UnitCost) / newQty, 4);
            }
            else if (policy.Method == CostingMethod.LastPurchase)
            {
                product.AverageCost = r.UnitCost;
            }

            if (r.Type == StockMovementType.InPurchase) product.LastPurchaseCost = r.UnitCost;
            product.CurrentStock += r.Quantity;
            await WarehouseStocks.ApplyAsync(_db, product.Id, warehouse.Id, r.Quantity, ct);
        }
        else
        {
            // مزامنة بيع أوف لاين: الخروج حدث فعلاً، فيُسجَّل ولو صار الرصيد سالباً ويُعلَّم للمراجعة
            var prohibit = policy.NegativeInventoryPolicy == "prohibit" && !_db.PermitNegativeStock;
            if (product.CurrentStock < r.Quantity && _db.PermitNegativeStock) _db.NegativeStockOccurred = true;
            if (product.CurrentStock < r.Quantity && prohibit)
                throw new ConflictException(string.Format(Messages.InsufficientStock, product.NameAr, product.CurrentStock, r.Quantity));
            // الصرف من مستودع بعينه: رصيده هو المتاح، لا رصيد المستودعات الأخرى
            var warehouseLeft = await WarehouseStocks.ApplyAsync(_db, product.Id, warehouse.Id, -r.Quantity, ct);
            if (warehouseLeft < 0 && _db.PermitNegativeStock) _db.NegativeStockOccurred = true;
            if (warehouseLeft < 0 && prohibit)
                throw new ConflictException(string.Format(Messages.InsufficientStockInWarehouse, product.NameAr, warehouse.NameAr,
                    await WarehouseStocks.QuantityAsync(_db, product.Id, warehouse.Id, ct) + r.Quantity, r.Quantity));

            // تكلفة خروج محددة (عكس شراء بتكلفة دخوله) في المتوسط المتحرك وآخر شراء والمعيارية؛ FIFO يخرج من طبقاته
            var fixedCost = policy.Method != CostingMethod.FIFO ? r.OutUnitCost : null;
            movement.UnitCost = policy.Method switch
            {
                CostingMethod.FIFO => await ConsumeFifoAsync(product, r.Quantity, ct),
                // آخر شراء والمعيارية: المتوسط الدفتري يساوي تكلفة الطريقة (يُضبط عند الشراء وبقيد إعادة تقييم عند تغيير المعيارية)
                _ => fixedCost ?? product.AverageCost,
            };
            if (policy.Method != CostingMethod.FIFO) await ConsumeLayersAsync(product.Id, r.Quantity, ct);
            product.CurrentStock -= r.Quantity;
            if (policy.Method == CostingMethod.FIFO) product.AverageCost = await RemainingAverageAsync(product, ct);
            else if (fixedCost is { } cost && product.CurrentStock > 0)
                product.AverageCost = Math.Max(0, Math.Round((valueBefore - r.Quantity * cost) / product.CurrentStock, 4));
        }

        movement.RemainingStock = product.CurrentStock;
        // فرق القيمتين لا الكمية × التكلفة: مجموع ما يُرحَّل يساوي قيمة الصنف دائماً (لا تتراكم هللات التقريب، ويدخل فيه أثر
        // إعادة تسعير الرصيد في آخر شراء وبالمعيارية وأثر الشراء على رصيد سالب). يُحفظ على الحركة فتقرؤه بطاقة الصنف.
        movement.ValueChange = InventoryValuation.Value(product) - valueBefore;
        await _db.SaveChangesAsync(ct);
        var dto = Mapper.Map<StockMovementDto>(movement);
        dto.Batches = batches;
        return dto;
    }

    /// <summary>
    /// توزيع الحركة على التشغيلات للصنف المتتبَّع: توزيع مسبق (مرتجع لتشغيلات أصله، عكس مستند لتشغيلاته) ثم ما بقي؛ الوارد لتشغيلته
    /// (تُنشأ أو تُطابَق)، والصادر بالمختارة أو الأقرب انتهاءً مع منع بيع المنتهي. الصنف غير المتتبَّع: لا شيء.
    /// </summary>
    private async Task<List<BatchAllocationDto>> ApplyBatchesAsync(Product product, StockMovement movement, RecordStockMovementDto r, Guid warehouseId,
        CostingPolicy policy, CancellationToken ct)
    {
        if (!product.TrackBatches) return new();
        var isIn = movement.Type is StockMovementType.InPurchase or StockMovementType.AdjustmentIn;
        var parts = new List<BatchAllocationDto>();
        var left = movement.Quantity;
        // التوزيع المسبق لا يُرفض على مستوى التشغيلة (العكس يعيد ما سجّله المستند نفسه)؛ رصيد الصنف يُفحص كالعادة
        if (r.BatchAllocations is { Count: > 0 } preset)
        {
            parts.AddRange(ItemBatches.Take(preset.Select(a => new BatchAllocationDto
                { ItemBatchId = a.ItemBatchId, BatchNumber = a.BatchNumber, ExpiryDate = a.ExpiryDate, Quantity = a.Quantity }).ToList(), left));
            left -= parts.Sum(a => a.Quantity);
        }
        if (left > 0)
        {
            if (isIn)
            {
                var hasBatch = r.ItemBatchId.HasValue || !string.IsNullOrWhiteSpace(r.BatchNumber);
                // ما زاد عن توزيع الأصل في المرتجع بلا تشغيلة: للتشغيلة الافتتاحية
                var batch = !hasBatch && parts.Count > 0 ? await ItemBatches.OpeningAsync(_db, product.Id, ct)
                    : await ItemBatches.ResolveIncomingAsync(_db, product, r.ItemBatchId, r.BatchNumber, r.ProductionDate, r.ExpiryDate, ct);
                parts.Add(ItemBatches.Allocation(batch, left));
            }
            else
            {
                var allowShortage = policy.NegativeInventoryPolicy != "prohibit" || _db.PermitNegativeStock;
                parts.AddRange(await ItemBatches.AllocateOutAsync(_db, product, warehouseId, left, r.ItemBatchId, movement.Date,
                    isSale: movement.Type == StockMovementType.OutSales, allowShortage, ct));
            }
        }
        var loaded = (await ItemBatches.LoadAsync(_db, parts.Select(a => a.ItemBatchId).Distinct().ToList(), ct)).ToDictionary(b => b.Id);
        await ItemBatches.RecordAsync(_db, movement, parts.Select(a => (loaded[a.ItemBatchId], a.Quantity)).ToList(), isIn ? 1 : -1, r.SourceLineId, ct);
        return parts;
    }

    /// <summary>
    /// شراء لا يغيّر تكلفة الصنف: «تحديث التكلفة عند الشراء» موقوف، والطريقة متوسط أو آخر شراء (FIFO طبقاتها بتكلفتها الفعلية)،
    /// وللصنف تكلفة سابقة (أول شراء يحدّد التكلفة دائماً).
    /// </summary>
    internal static bool KeepsCost(CostingPolicy policy, StockMovementType type, decimal currentCost)
        => !policy.RecalculateOnNewPurchase && type == StockMovementType.InPurchase && currentCost > 0
           && policy.Method is CostingMethod.MovingAverage or CostingMethod.LastPurchase or CostingMethod.Standard;

    /// <summary>يستهلك أقدم الدفعات ويُرجع متوسط تكلفة الكمية المستهلكة.</summary>
    private async Task<decimal> ConsumeFifoAsync(Product product, decimal qty, CancellationToken ct)
    {
        var layers = await _db.Set<StockMovement>()
            .Where(m => m.ItemId == product.Id && m.RemainingQuantity > 0)
            .OrderBy(m => m.Date).ThenBy(m => m.CreatedAt).ToListAsync(ct);
        var left = qty; var cost = 0m;
        foreach (var l in layers)
        {
            if (left <= 0) break;
            var take = Math.Min(l.RemainingQuantity, left);
            cost += take * l.UnitCost; l.RemainingQuantity -= take; left -= take;
        }
        if (left > 0) cost += left * product.AverageCost; // مخزون سالب مسموح: الباقي بآخر متوسط
        return qty == 0 ? 0 : Math.Round(cost / qty, 4);
    }

    /// <summary>للطرق غير FIFO: تُستهلك الدفعات أيضاً لتبقى طبقات FIFO سليمة إن تغيّرت السياسة لاحقاً.</summary>
    private async Task ConsumeLayersAsync(Guid itemId, decimal qty, CancellationToken ct)
    {
        var layers = await _db.Set<StockMovement>().Where(m => m.ItemId == itemId && m.RemainingQuantity > 0)
            .OrderBy(m => m.Date).ThenBy(m => m.CreatedAt).ToListAsync(ct);
        var left = qty;
        foreach (var l in layers)
        {
            if (left <= 0) break;
            var take = Math.Min(l.RemainingQuantity, left);
            l.RemainingQuantity -= take; left -= take;
        }
    }

    private async Task<decimal> RemainingAverageAsync(Product product, CancellationToken ct)
    {
        var layers = await _db.Set<StockMovement>().Where(m => m.ItemId == product.Id && m.RemainingQuantity > 0).ToListAsync(ct); // متتبَّعة: تعكس استهلاك هذه العملية قبل الحفظ
        var qty = layers.Sum(l => l.RemainingQuantity);
        return qty == 0 ? product.AverageCost : Math.Round(layers.Sum(l => l.RemainingQuantity * l.UnitCost) / qty, 4);
    }

    // ---------------- تعديل/حذف الحركات اليدوية وسحب حركات مستند ----------------
    public async Task<StockMovementDto> UpdateMovementAsync(Guid id, RecordStockMovementDto r, CancellationToken ct = default)
    {
        if (r.Type is not (StockMovementType.AdjustmentIn or StockMovementType.AdjustmentOut))
            throw new ValidationFailedException(Messages.ManualAdjustmentTypeOnly);
        if (r.Quantity <= 0) throw new ValidationFailedException(Messages.QuantityMustBePositive);
        if (r.UnitCost < 0) throw new ValidationFailedException(Messages.UnitCostCannotBeNegative);

        var m = await LoadManualAsync(id, ct);
        await _periods.EnsureOpenAsync(m.Date, ct);
        await _periods.EnsureOpenAsync(r.Date ?? m.Date, ct);
        await Accounting.PeriodicInventory.EnsureMovementDateAllowedAsync(_db, new[] { m.Date, r.Date ?? m.Date }.Min(), ct);
        if (r.ItemId != m.ItemId) throw new ValidationFailedException(Messages.CannotChangeMovementItem);

        // رصيد المستودع: يُسحب أثر الحركة القديمة ثم يُطبَّق الجديد (قد يتغيّر المستودع نفسه)
        await UndoWarehouseEffectAsync(m, ct);
        await ItemBatches.UndoAsync(_db, m, ct);
        var warehouse = await WarehouseStocks.ResolveAsync(_db, r.WarehouseId ?? m.WarehouseId, ct);
        var product = await _db.Set<Product>().FirstAsync(p => p.Id == m.ItemId, ct);
        var valueBefore = InventoryValuation.Value(product);
        var oldValue = m.ValueChange;
        m.Type = r.Type; m.Quantity = r.Quantity; m.UnitCost = r.UnitCost; m.UnitPrice = r.UnitPrice;
        m.Date = r.Date ?? m.Date; m.ReferenceNumber = r.ReferenceNumber; m.Notes = r.Notes; m.WarehouseId = warehouse.Id;
        m.ValueChange = Sign(m.Type) * DocumentPricing.Round(m.Quantity * m.UnitCost);
        await WarehouseStocks.ApplyAsync(_db, m.ItemId, warehouse.Id, Sign(m.Type) * m.Quantity, ct);
        var policy = await _db.Set<CostingPolicy>().AsNoTracking().FirstOrDefaultAsync(ct) ?? new CostingPolicy();
        await ApplyBatchesAsync(product, m, r, warehouse.Id, policy, ct);
        await ReplayAsync(m.ItemId, new HashSet<Guid>(), ct);
        // ما تغيّر في قيمة الصنف غير قيمة الحركة نفسها = إعادة تسعير بقية الرصيد بإعادة البناء
        InventoryValuation.AddRevaluation(_db, product, InventoryValuation.Value(product) - valueBefore - (m.ValueChange - oldValue),
            m.ReferenceNumber, "إعادة تقييم بقية الرصيد بعد تعديل تسوية", m.Id);
        await _db.SaveChangesAsync(ct);
        return Mapper.Map<StockMovementDto>(m);
    }

    public async Task DeleteMovementAsync(Guid id, CancellationToken ct = default)
    {
        var m = await LoadManualAsync(id, ct);
        await _periods.EnsureOpenAsync(m.Date, ct);
        await Accounting.PeriodicInventory.EnsureMovementDateAllowedAsync(_db, m.Date, ct);
        var product = await _db.Set<Product>().FirstAsync(p => p.Id == m.ItemId, ct);
        var valueBefore = InventoryValuation.Value(product);
        await ItemBatches.UndoAsync(_db, m, ct);
        _db.Remove(m);
        await UndoWarehouseEffectAsync(m, ct);
        await ReplayAsync(m.ItemId, new HashSet<Guid> { m.Id }, ct);
        // قيمة الحركة المحذوفة خرجت من سجل الصنف؛ ما تغيّر غيرها = إعادة تسعير بقية الرصيد
        InventoryValuation.AddRevaluation(_db, product, InventoryValuation.Value(product) - valueBefore + m.ValueChange,
            m.ReferenceNumber, "إعادة تقييم بقية الرصيد بعد حذف تسوية", null);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<StockMovement> LoadManualAsync(Guid id, CancellationToken ct)
    {
        var m = await _db.Set<StockMovement>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(Messages.StockMovementNotFound);
        if (m.SourceType != "manual")
            throw new ConflictException(Messages.SystemMovementFromDocument);
        return m;
    }

    /// <summary>يعيد بناء رصيد وتكلفة صنف من حركاته المتبقية، ويمنع أي تعديل يجعل الرصيد سالباً في أي تاريخ.</summary>
    private async Task ReplayAsync(Guid itemId, HashSet<Guid> excluded, CancellationToken ct)
    {
        var product = await _db.Set<Product>().FirstAsync(p => p.Id == itemId, ct);
        var policy = await _db.Set<CostingPolicy>().AsNoTracking().FirstOrDefaultAsync(ct) ?? new CostingPolicy();
        var ordered = (await _db.Set<StockMovement>().Where(m => m.ItemId == itemId).ToListAsync(ct))
            .Where(m => !excluded.Contains(m.Id)).OrderBy(m => m.Date).ThenBy(m => m.CreatedAt).ToList();
        var min = StockReplay.Apply(product, ordered, policy);
        var warehouseShort = _db.Set<WarehouseStock>().Local.Any(s => s.ItemId == itemId && s.Quantity < 0);
        if ((min < 0 || warehouseShort) && policy.NegativeInventoryPolicy == "prohibit" && !_db.PermitNegativeStock)
            throw new ConflictException(string.Format(Messages.OperationMakesStockNegativeLater, product.NameAr));
    }

    private static int Sign(StockMovementType type)
        => type is StockMovementType.InPurchase or StockMovementType.AdjustmentIn or StockMovementType.TransferIn ? 1 : -1;

    /// <summary>يسحب أثر حركة من رصيد مستودعها (الحركات القديمة بلا مستودع تُحسب على الافتراضي).</summary>
    private async Task UndoWarehouseEffectAsync(StockMovement m, CancellationToken ct)
    {
        var warehouse = await WarehouseStocks.ResolveAsync(_db, m.WarehouseId, ct);
        await WarehouseStocks.ApplyAsync(_db, m.ItemId, warehouse.Id, -Sign(m.Type) * m.Quantity, ct);
    }

    // ---------------- المستودعات: التحويل والأرصدة ----------------
    public async Task<List<BatchAllocationDto>> TransferAsync(TransferStockDto r, CancellationToken ct = default)
    {
        if (r.Quantity <= 0) throw new ValidationFailedException(Messages.QuantityMustBePositive);
        if (r.FromWarehouseId == r.ToWarehouseId) throw new ValidationFailedException(Messages.TransferWarehousesMustDiffer);
        await _periods.EnsureOpenAsync(r.Date, ct);
        await Accounting.PeriodicInventory.EnsureMovementDateAllowedAsync(_db, r.Date, ct); // توزيع القيمة المثبتة على المستودعات

        var product = await _db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == r.ItemId, ct)
            ?? throw new NotFoundException(Messages.ProductNotFound);
        if (!ProductItemTypes.TracksStock(product.ItemType))
            throw new ValidationFailedException($"«{product.NameAr}» صنف بلا رصيد مخزني: لا يُحوَّل بين المستودعات.");
        var from = await WarehouseStocks.ResolveAsync(_db, r.FromWarehouseId, ct);
        var to = await WarehouseStocks.ResolveAsync(_db, r.ToWarehouseId, ct);
        var policy = await _db.Set<CostingPolicy>().AsNoTracking().FirstOrDefaultAsync(ct) ?? new CostingPolicy();

        var available = await WarehouseStocks.QuantityAsync(_db, product.Id, from.Id, ct);
        if (available < r.Quantity && policy.NegativeInventoryPolicy == "prohibit")
            throw new ConflictException(string.Format(Messages.InsufficientStockInWarehouse, product.NameAr, from.NameAr, available, r.Quantity));

        // التشغيلات: تُخصَّص من المستودع المحوَّل منه (المختارة أو الأقرب انتهاءً) وتنتقل بأرقامها نفسها للمستلم
        var parts = product.TrackBatches
            ? await ItemBatches.AllocateOutAsync(_db, product, from.Id, r.Quantity, r.ItemBatchId, r.Date, isSale: false,
                allowShortage: policy.NegativeInventoryPolicy != "prohibit", ct)
            : new List<BatchAllocationDto>();
        var batches = (await ItemBatches.LoadAsync(_db, parts.Select(a => a.ItemBatchId).Distinct().ToList(), ct)).ToDictionary(b => b.Id);

        await WarehouseStocks.ApplyAsync(_db, product.Id, from.Id, -r.Quantity, ct);
        await WarehouseStocks.ApplyAsync(_db, product.Id, to.Id, r.Quantity, ct);
        // حركتان للتتبّع فقط: لا طبقة تكلفة ولا تغيير في رصيد الصنف الإجمالي
        foreach (var (type, warehouseId, sign) in new[] { (StockMovementType.TransferOut, from.Id, -1), (StockMovementType.TransferIn, to.Id, 1) })
        {
            var movement = new StockMovement
            {
                ItemId = product.Id, ItemName = product.NameAr, WarehouseId = warehouseId, Date = r.Date, Type = type, Quantity = r.Quantity,
                UnitCost = product.AverageCost, ReferenceNumber = r.ReferenceNumber, Notes = r.Notes, RemainingStock = product.CurrentStock,
                SourceType = "stock_transfer", SourceId = r.SourceId,
            };
            _db.Add(movement);
            if (parts.Count > 0)
                await ItemBatches.RecordAsync(_db, movement, parts.Select(a => (batches[a.ItemBatchId], a.Quantity)).ToList(), sign, r.SourceLineId, ct);
        }
        await _db.SaveChangesAsync(ct);
        return parts;
    }

    public async Task<List<WarehouseStockDto>> GetWarehouseStockAsync(Guid? itemId, Guid? warehouseId, CancellationToken ct = default)
    {
        var q = from s in _db.Set<WarehouseStock>().AsNoTracking()
                join p in _db.Set<Product>().AsNoTracking() on s.ItemId equals p.Id
                join w in _db.Set<Warehouse>().AsNoTracking() on s.WarehouseId equals w.Id
                where s.Quantity != 0 && (itemId == null || s.ItemId == itemId) && (warehouseId == null || s.WarehouseId == warehouseId)
                orderby p.Sku, w.Code
                select new WarehouseStockDto
                {
                    ItemId = p.Id, Sku = p.Sku, ItemName = p.NameAr, Unit = p.Unit, WarehouseId = w.Id, WarehouseName = w.NameAr,
                    Quantity = s.Quantity, AverageCost = p.AverageCost, Value = Math.Round(s.Quantity * p.AverageCost, 2),
                };
        return await q.ToListAsync(ct);
    }

    // ---------------- التشغيلات والصلاحية ----------------
    public async Task<List<ItemBatchDto>> GetBatchesAsync(Guid itemId, Guid? warehouseId, bool includeEmpty, CancellationToken ct = default)
    {
        var product = await _db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == itemId, ct) ?? throw new NotFoundException(Messages.ProductNotFound);
        var batches = await _db.Set<ItemBatch>().AsNoTracking().Where(b => b.ItemId == itemId).ToListAsync(ct);
        var stock = await _db.Set<BatchStock>().AsNoTracking().Where(s => s.ItemId == itemId && (warehouseId == null || s.WarehouseId == warehouseId))
            .GroupBy(s => s.ItemBatchId).Select(g => new { g.Key, Qty = g.Sum(s => s.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        var today = SaudiTime.Today;
        return batches.Select(b => new ItemBatchDto
            {
                Id = b.Id, ItemId = b.ItemId, BatchNumber = b.BatchNumber, ProductionDate = b.ProductionDate, ExpiryDate = b.ExpiryDate, Notes = b.Notes,
                Quantity = stock.GetValueOrDefault(b.Id), DaysToExpiry = ItemBatches.DaysToExpiry(b.ExpiryDate, today),
                ExpiryStatus = ItemBatches.Status(b.ExpiryDate, product.ExpiryAlertDays, today),
            })
            .Where(b => includeEmpty || b.Quantity != 0)
            .OrderBy(b => b.ExpiryDate == null).ThenBy(b => b.ExpiryDate).ThenBy(b => b.BatchNumber).ToList();
    }

    public async Task<ItemBatchDto> UpdateBatchAsync(Guid batchId, UpdateItemBatchDto r, CancellationToken ct = default)
    {
        var batch = await _db.Set<ItemBatch>().FirstOrDefaultAsync(b => b.Id == batchId, ct) ?? throw new NotFoundException(Messages.BatchNotFound);
        var product = await _db.Set<Product>().AsNoTracking().FirstAsync(p => p.Id == batch.ItemId, ct);
        if (r.ProductionDate is { } pd && r.ExpiryDate is { } ed && pd.Date > ed.Date)
            throw new ValidationFailedException(string.Format(Messages.BatchDatesInvalid, batch.BatchNumber));
        if (product.TrackExpiry && r.ExpiryDate == null)
            throw new ValidationFailedException(string.Format(Messages.BatchExpiryRequired, product.NameAr, batch.BatchNumber));
        batch.ProductionDate = r.ProductionDate?.Date;
        batch.ExpiryDate = r.ExpiryDate?.Date;
        batch.Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim();
        await _db.SaveChangesAsync(ct);
        return (await GetBatchesAsync(batch.ItemId, null, true, ct)).First(b => b.Id == batch.Id);
    }

    public async Task<List<BatchExpiryRowDto>> GetExpiryReportAsync(Guid? warehouseId, string? status, int? withinDays, CancellationToken ct = default)
    {
        var rows = await (from s in _db.Set<BatchStock>().AsNoTracking()
                          join b in _db.Set<ItemBatch>().AsNoTracking() on s.ItemBatchId equals b.Id
                          join p in _db.Set<Product>().AsNoTracking() on s.ItemId equals p.Id
                          join w in _db.Set<Warehouse>().AsNoTracking() on s.WarehouseId equals w.Id
                          where s.Quantity != 0 && (warehouseId == null || s.WarehouseId == warehouseId)
                          select new { s, b, p, w }).ToListAsync(ct);
        var today = SaudiTime.Today;
        return rows.Select(x => new BatchExpiryRowDto
            {
                ItemId = x.p.Id, Sku = x.p.Sku, ItemName = x.p.NameAr, Unit = x.p.Unit, ItemBatchId = x.b.Id, BatchNumber = x.b.BatchNumber,
                ProductionDate = x.b.ProductionDate, ExpiryDate = x.b.ExpiryDate, DaysToExpiry = ItemBatches.DaysToExpiry(x.b.ExpiryDate, today),
                ExpiryStatus = ItemBatches.Status(x.b.ExpiryDate, x.p.ExpiryAlertDays, today),
                WarehouseId = x.w.Id, WarehouseName = x.w.NameAr, Quantity = x.s.Quantity, Value = Math.Round(x.s.Quantity * x.p.AverageCost, 2),
            })
            .Where(r => string.IsNullOrWhiteSpace(status) || r.ExpiryStatus == status)
            .Where(r => withinDays == null || (r.DaysToExpiry is { } d && d <= withinDays))
            .OrderBy(r => r.ExpiryDate == null).ThenBy(r => r.ExpiryDate).ThenBy(r => r.Sku).ThenBy(r => r.WarehouseName).ToList();
    }

    public async Task<StockMovementDto> GetMovementAsync(Guid id, CancellationToken ct = default)
        => Mapper.Map<StockMovementDto>(await _db.Set<StockMovement>().AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new NotFoundException(Messages.StockMovementNotFound));

    public async Task<PagedResult<StockMovementDto>> ListMovementsAsync(Guid? itemId, Guid? warehouseId, PaginationParams p, CancellationToken ct = default)
    {
        var q = _db.Set<StockMovement>().AsNoTracking().AsQueryable();
        if (itemId.HasValue) q = q.Where(m => m.ItemId == itemId);
        if (warehouseId.HasValue) q = q.Where(m => m.WarehouseId == warehouseId);
        if (p.StartDate.HasValue) q = q.Where(m => m.Date >= p.StartDate);
        if (p.EndDate.HasValue) q = q.Where(m => m.Date <= p.EndDate);
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var t = p.SearchTerm.Trim();
            q = q.Where(m => m.ItemName.Contains(t) || m.ReferenceNumber.Contains(t));
        }
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(m => m.Date).ThenByDescending(m => m.CreatedAt)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);
        return new PagedResult<StockMovementDto>
        {
            Items = items.Select(Mapper.Map<StockMovementDto>).ToList(),
            TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }
}
