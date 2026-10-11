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
        await _periods.EnsureOpenAsync(r.Date ?? DateTime.UtcNow, ct);

        var product = await _db.Set<Product>().FirstOrDefaultAsync(p => p.Id == r.ItemId, ct)
            ?? throw new NotFoundException(Messages.ProductNotFound);
        if (r.Type is StockMovementType.TransferIn or StockMovementType.TransferOut) throw new ValidationFailedException(Messages.ManualAdjustmentTypeOnly);
        var warehouse = await WarehouseStocks.ResolveAsync(_db, r.WarehouseId, ct);

        var policy = await _db.Set<CostingPolicy>().AsNoTracking().FirstOrDefaultAsync(ct) ?? new CostingPolicy();
        var isIn = r.Type is StockMovementType.InPurchase or StockMovementType.AdjustmentIn;

        var movement = new StockMovement
        {
            ItemId = product.Id, ItemName = product.NameAr, WarehouseId = warehouse.Id,
            Date = r.Date ?? DateTime.UtcNow, Type = r.Type, Quantity = r.Quantity,
            UnitPrice = r.UnitPrice, ReferenceNumber = r.ReferenceNumber, Notes = r.Notes,
            SourceType = r.SourceType, SourceId = r.SourceId,
        };

        if (isIn)
        {
            var baseStock = Math.Max(product.CurrentStock, 0);
            movement.UnitCost = r.UnitCost;
            movement.RemainingQuantity = r.Quantity;

            if (policy.Method is CostingMethod.MovingAverage or CostingMethod.FIFO)
            {
                var newQty = baseStock + r.Quantity;
                product.AverageCost = newQty == 0 ? r.UnitCost
                    : Math.Round((baseStock * product.AverageCost + r.Quantity * r.UnitCost) / newQty, 4);
            }
            else if (policy.Method == CostingMethod.LastPurchase)
            {
                product.AverageCost = r.UnitCost;
            }
            // Standard: التكلفة المعيارية لا تتغيّر بالمشتريات.

            if (r.Type == StockMovementType.InPurchase) product.LastPurchaseCost = r.UnitCost;
            product.CurrentStock += r.Quantity;
            await WarehouseStocks.ApplyAsync(_db, product.Id, warehouse.Id, r.Quantity, ct);
        }
        else
        {
            if (product.CurrentStock < r.Quantity && policy.NegativeInventoryPolicy == "prohibit")
                throw new ConflictException(string.Format(Messages.InsufficientStock, product.NameAr, product.CurrentStock, r.Quantity));
            // الصرف من مستودع بعينه: رصيده هو المتاح، لا رصيد المستودعات الأخرى
            if (await WarehouseStocks.ApplyAsync(_db, product.Id, warehouse.Id, -r.Quantity, ct) < 0 && policy.NegativeInventoryPolicy == "prohibit")
                throw new ConflictException(string.Format(Messages.InsufficientStockInWarehouse, product.NameAr, warehouse.NameAr,
                    await WarehouseStocks.QuantityAsync(_db, product.Id, warehouse.Id, ct) + r.Quantity, r.Quantity));

            movement.UnitCost = policy.Method switch
            {
                CostingMethod.FIFO => await ConsumeFifoAsync(product, r.Quantity, ct),
                CostingMethod.LastPurchase => product.LastPurchaseCost > 0 ? product.LastPurchaseCost : product.AverageCost,
                CostingMethod.Standard => product.StandardCost ?? product.AverageCost,
                _ => product.AverageCost,
            };
            if (policy.Method != CostingMethod.FIFO) await ConsumeLayersAsync(product.Id, r.Quantity, ct);
            product.CurrentStock -= r.Quantity;
            if (policy.Method == CostingMethod.FIFO) product.AverageCost = await RemainingAverageAsync(product, ct);
        }

        movement.RemainingStock = product.CurrentStock;
        _db.Add(movement);
        await _db.SaveChangesAsync(ct);
        return Mapper.Map<StockMovementDto>(movement);
    }

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
        if (r.ItemId != m.ItemId) throw new ValidationFailedException(Messages.CannotChangeMovementItem);

        // رصيد المستودع: يُسحب أثر الحركة القديمة ثم يُطبَّق الجديد (قد يتغيّر المستودع نفسه)
        await UndoWarehouseEffectAsync(m, ct);
        var warehouse = await WarehouseStocks.ResolveAsync(_db, r.WarehouseId ?? m.WarehouseId, ct);
        m.Type = r.Type; m.Quantity = r.Quantity; m.UnitCost = r.UnitCost; m.UnitPrice = r.UnitPrice;
        m.Date = r.Date ?? m.Date; m.ReferenceNumber = r.ReferenceNumber; m.Notes = r.Notes; m.WarehouseId = warehouse.Id;
        await WarehouseStocks.ApplyAsync(_db, m.ItemId, warehouse.Id, Sign(m.Type) * m.Quantity, ct);
        await ReplayAsync(m.ItemId, new HashSet<Guid>(), ct);
        await _db.SaveChangesAsync(ct);
        return Mapper.Map<StockMovementDto>(m);
    }

    public async Task DeleteMovementAsync(Guid id, CancellationToken ct = default)
    {
        var m = await LoadManualAsync(id, ct);
        await _periods.EnsureOpenAsync(m.Date, ct);
        _db.Remove(m);
        await UndoWarehouseEffectAsync(m, ct);
        await ReplayAsync(m.ItemId, new HashSet<Guid> { m.Id }, ct);
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
        if ((min < 0 || warehouseShort) && policy.NegativeInventoryPolicy == "prohibit")
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
    public async Task TransferAsync(TransferStockDto r, CancellationToken ct = default)
    {
        if (r.Quantity <= 0) throw new ValidationFailedException(Messages.QuantityMustBePositive);
        if (r.FromWarehouseId == r.ToWarehouseId) throw new ValidationFailedException(Messages.TransferWarehousesMustDiffer);
        await _periods.EnsureOpenAsync(r.Date, ct);

        var product = await _db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == r.ItemId, ct)
            ?? throw new NotFoundException(Messages.ProductNotFound);
        var from = await WarehouseStocks.ResolveAsync(_db, r.FromWarehouseId, ct);
        var to = await WarehouseStocks.ResolveAsync(_db, r.ToWarehouseId, ct);
        var policy = await _db.Set<CostingPolicy>().AsNoTracking().FirstOrDefaultAsync(ct) ?? new CostingPolicy();

        var available = await WarehouseStocks.QuantityAsync(_db, product.Id, from.Id, ct);
        if (available < r.Quantity && policy.NegativeInventoryPolicy == "prohibit")
            throw new ConflictException(string.Format(Messages.InsufficientStockInWarehouse, product.NameAr, from.NameAr, available, r.Quantity));

        await WarehouseStocks.ApplyAsync(_db, product.Id, from.Id, -r.Quantity, ct);
        await WarehouseStocks.ApplyAsync(_db, product.Id, to.Id, r.Quantity, ct);
        // حركتان للتتبّع فقط: لا طبقة تكلفة ولا تغيير في رصيد الصنف الإجمالي
        foreach (var (type, warehouseId) in new[] { (StockMovementType.TransferOut, from.Id), (StockMovementType.TransferIn, to.Id) })
            _db.Add(new StockMovement
            {
                ItemId = product.Id, ItemName = product.NameAr, WarehouseId = warehouseId, Date = r.Date, Type = type, Quantity = r.Quantity,
                UnitCost = product.AverageCost, ReferenceNumber = r.ReferenceNumber, Notes = r.Notes, RemainingStock = product.CurrentStock,
                SourceType = "stock_transfer", SourceId = r.SourceId,
            });
        await _db.SaveChangesAsync(ct);
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
