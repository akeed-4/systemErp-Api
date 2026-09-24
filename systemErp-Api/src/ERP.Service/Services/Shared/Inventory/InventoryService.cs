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
    public InventoryService(ErpDbContext db) => _db = db;

    public async Task<StockMovementDto> RecordMovementAsync(RecordStockMovementDto r, CancellationToken ct = default)
    {
        if (r.Quantity <= 0) throw new ValidationFailedException("الكمية يجب أن تكون أكبر من صفر.");
        if (r.UnitCost < 0) throw new ValidationFailedException("تكلفة الوحدة لا تكون سالبة.");

        var product = await _db.Set<Product>().FirstOrDefaultAsync(p => p.Id == r.ItemId, ct)
            ?? throw new NotFoundException("الصنف غير موجود");
        if (r.WarehouseId.HasValue && !await _db.Set<Warehouse>().AnyAsync(w => w.Id == r.WarehouseId, ct))
            throw new ValidationFailedException("المستودع غير موجود.");

        var policy = await _db.Set<CostingPolicy>().AsNoTracking().FirstOrDefaultAsync(ct) ?? new CostingPolicy();
        var isIn = r.Type is StockMovementType.InPurchase or StockMovementType.AdjustmentIn;

        var movement = new StockMovement
        {
            ItemId = product.Id, ItemName = product.NameAr, WarehouseId = r.WarehouseId,
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
        }
        else
        {
            if (product.CurrentStock < r.Quantity && policy.NegativeInventoryPolicy == "prohibit")
                throw new ConflictException($"الرصيد غير كافٍ للصنف {product.NameAr}: المتاح {product.CurrentStock:0.####} والمطلوب {r.Quantity:0.####}.");

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
            throw new ValidationFailedException("التسوية اليدوية: adjustment_in أو adjustment_out فقط.");
        if (r.Quantity <= 0) throw new ValidationFailedException("الكمية يجب أن تكون أكبر من صفر.");
        if (r.UnitCost < 0) throw new ValidationFailedException("تكلفة الوحدة لا تكون سالبة.");

        var m = await LoadManualAsync(id, ct);
        if (r.ItemId != m.ItemId) throw new ValidationFailedException("لا يمكن تغيير الصنف؛ احذف الحركة وأنشئ أخرى.");
        if (r.WarehouseId.HasValue && !await _db.Set<Warehouse>().AnyAsync(w => w.Id == r.WarehouseId, ct))
            throw new ValidationFailedException("المستودع غير موجود.");

        m.Type = r.Type; m.Quantity = r.Quantity; m.UnitCost = r.UnitCost; m.UnitPrice = r.UnitPrice;
        m.Date = r.Date ?? m.Date; m.ReferenceNumber = r.ReferenceNumber; m.Notes = r.Notes; m.WarehouseId = r.WarehouseId;
        await ReplayAsync(m.ItemId, new HashSet<Guid>(), ct);
        await _db.SaveChangesAsync(ct);
        return Mapper.Map<StockMovementDto>(m);
    }

    public async Task DeleteMovementAsync(Guid id, CancellationToken ct = default)
    {
        var m = await LoadManualAsync(id, ct);
        _db.Remove(m);
        await ReplayAsync(m.ItemId, new HashSet<Guid> { m.Id }, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveDocumentMovementsAsync(string sourceType, Guid sourceId, CancellationToken ct = default)
    {
        var movements = await _db.Set<StockMovement>().Where(m => m.SourceType == sourceType && m.SourceId == sourceId).ToListAsync(ct);
        if (movements.Count == 0) return;
        _db.RemoveRange(movements);
        var excluded = movements.Select(m => m.Id).ToHashSet();
        foreach (var itemId in movements.Select(m => m.ItemId).Distinct())
            await ReplayAsync(itemId, excluded, ct);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<StockMovement> LoadManualAsync(Guid id, CancellationToken ct)
    {
        var m = await _db.Set<StockMovement>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("حركة المخزون غير موجودة");
        if (m.SourceType != "manual")
            throw new ConflictException("هذه حركة نظامية ناتجة عن مستند؛ عدّل المستند نفسه (أو أنشئ تسوية جديدة).");
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
        if (min < 0 && policy.NegativeInventoryPolicy == "prohibit")
            throw new ConflictException($"العملية تجعل رصيد الصنف {product.NameAr} سالباً في تاريخ لاحق.");
    }

    public async Task<StockMovementDto> GetMovementAsync(Guid id, CancellationToken ct = default)
        => Mapper.Map<StockMovementDto>(await _db.Set<StockMovement>().AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new NotFoundException("حركة المخزون غير موجودة"));

    public async Task<PagedResult<StockMovementDto>> ListMovementsAsync(Guid? itemId, PaginationParams p, CancellationToken ct = default)
    {
        var q = _db.Set<StockMovement>().AsNoTracking().AsQueryable();
        if (itemId.HasValue) q = q.Where(m => m.ItemId == itemId);
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
