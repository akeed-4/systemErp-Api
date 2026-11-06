using System.Globalization;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// تشغيلات الأصناف (Lot/Batch) وصلاحيتها: تحديد تشغيلة الوارد، وتخصيص الصادر بالأقرب انتهاءً (FEFO) مع منع بيع المنتهي،
/// وأرصدة كل تشغيلة في كل مستودع. التكلفة لا تُمس: الحركة سطر واحد بتكلفة الصنف، وتوزيعها على التشغيلات في StockMovementBatch.
/// كل تغيير في رصيد تشغيلة يمر من هنا ليبقى مجموع تشغيلات الصنف في المستودع مساوياً لرصيده فيه.
/// </summary>
public static class ItemBatches
{
    /// <summary>رقم ثابت (غير مترجم) لتشغيلة الرصيد الافتتاحي: لغة الطلب لا تُنشئ تشغيلة ثانية.</summary>
    public const string OpeningNumber = "OPENING";

    /// <summary>منتهية = تاريخ انتهائها قبل يوم الحركة (يوم الانتهاء نفسه صالح).</summary>
    public static bool IsExpired(ItemBatch b, DateTime date) => b.ExpiryDate is { } e && e.Date < date.Date;

    public static string Status(DateTime? expiry, int alertDays, DateTime today)
    {
        if (expiry is not { } e) return "no_expiry";
        var days = (e.Date - today.Date).Days;
        return days < 0 ? "expired" : days <= alertDays ? "near_expiry" : "valid";
    }

    public static int? DaysToExpiry(DateTime? expiry, DateTime today) => expiry is { } e ? (e.Date - today.Date).Days : null;

    // ---------------- الوارد ----------------
    /// <summary>
    /// تشغيلة الوارد: بمعرّفها، أو برقمها (تُطابَق القائمة وتُكمَل تواريخها الفارغة، وإلا تُنشأ). الصنف بصلاحية يلزمه تاريخ انتهاء،
    /// ويُقترح من تاريخ الإنتاج + مدة الصلاحية إن لم يُدخل. تاريخ انتهاء مختلف لرقم قائم يُرفض (رقم واحد = تشغيلة واحدة).
    /// </summary>
    public static async Task<ItemBatch> ResolveIncomingAsync(ErpDbContext db, Product product, Guid? batchId, string? number,
        DateTime? production, DateTime? expiry, CancellationToken ct)
    {
        if (batchId is { } id)
            return await FindAsync(db, product.Id, id, ct) ?? throw new NotFoundException(Messages.BatchNotFound);

        var code = (number ?? string.Empty).Trim();
        if (code.Length == 0) throw new ValidationFailedException(string.Format(Messages.BatchNumberRequired, product.NameAr));
        if (code.Length > 64) code = code[..64];
        production = production?.Date;
        expiry = expiry?.Date ?? (production is { } p && product.ShelfLifeDays is > 0 ? p.AddDays(product.ShelfLifeDays.Value) : null);
        if (production is { } pd && expiry is { } ed && pd > ed) throw new ValidationFailedException(string.Format(Messages.BatchDatesInvalid, code));

        var batch = db.Set<ItemBatch>().Local.FirstOrDefault(b => b.ItemId == product.Id && b.BatchNumber == code)
            ?? await db.Set<ItemBatch>().FirstOrDefaultAsync(b => b.ItemId == product.Id && b.BatchNumber == code, ct);
        if (batch != null)
        {
            if (expiry is { } given && batch.ExpiryDate is { } recorded && recorded.Date != given.Date)
                throw new ConflictException(string.Format(Messages.BatchExpiryMismatch, product.NameAr, code, Day(recorded), Day(given)));
            batch.ExpiryDate ??= expiry;
            batch.ProductionDate ??= production;
        }
        else
        {
            batch = new ItemBatch { ItemId = product.Id, BatchNumber = code, ProductionDate = production, ExpiryDate = expiry };
            db.Add(batch);
        }
        if (product.TrackExpiry && batch.ExpiryDate == null)
            throw new ValidationFailedException(string.Format(Messages.BatchExpiryRequired, product.NameAr, code));
        return batch;
    }

    /// <summary>تشغيلة «رصيد افتتاحي» بلا صلاحية: تحمل الرصيد القائم عند تفعيل التتبّع، وما صُرف بلا تشغيلة في المزامنة أوف لاين.</summary>
    public static async Task<ItemBatch> OpeningAsync(ErpDbContext db, Guid itemId, CancellationToken ct)
    {
        var code = OpeningNumber;
        var batch = db.Set<ItemBatch>().Local.FirstOrDefault(b => b.ItemId == itemId && b.BatchNumber == code)
            ?? await db.Set<ItemBatch>().FirstOrDefaultAsync(b => b.ItemId == itemId && b.BatchNumber == code, ct);
        if (batch != null) return batch;
        batch = new ItemBatch { ItemId = itemId, BatchNumber = code, Notes = "رصيد قائم عند تفعيل تتبّع التشغيلات" };
        db.Add(batch);
        return batch;
    }

    // ---------------- الصادر ----------------
    /// <summary>
    /// تخصيص صادر من مستودع: التشغيلة المختارة، وإلا الأقرب انتهاءً فالأقدم (FEFO) من غير المنتهية. البيع من منتهية ممنوع دائماً؛
    /// والصادر لغير البيع (مرتجع شراء، تسوية، تحويل) يجوز من منتهية إن اختيرت صراحةً. النقص يُرفض، إلا إن سُمح بالسالب
    /// (سياسة تسمح، أو مزامنة أوف لاين) فيُحمَّل على آخر تشغيلة صالحة (أو الافتتاحية إن لم توجد).
    /// </summary>
    public static async Task<List<BatchAllocationDto>> AllocateOutAsync(ErpDbContext db, Product product, Guid warehouseId, decimal quantity,
        Guid? chosenBatchId, DateTime date, bool isSale, bool allowShortage, CancellationToken ct)
    {
        var balances = await BalancesAsync(db, product.Id, warehouseId, ct);
        if (chosenBatchId is { } chosenId)
        {
            var batch = await FindAsync(db, product.Id, chosenId, ct) ?? throw new NotFoundException(Messages.BatchNotFound);
            if (isSale && IsExpired(batch, date))
                throw new ConflictException(string.Format(Messages.ExpiredBatchCannotBeIssued, product.NameAr, batch.BatchNumber, Day(batch.ExpiryDate!.Value)));
            var available = balances.GetValueOrDefault(batch.Id);
            if (available < quantity && !allowShortage)
                throw new ConflictException(string.Format(Messages.InsufficientBatchStock, product.NameAr, batch.BatchNumber, Qty(available), Qty(quantity)));
            return new() { Allocation(batch, quantity) };
        }

        var ids = balances.Where(b => b.Value > 0).Select(b => b.Key).ToList();
        var batches = await LoadAsync(db, ids, ct);
        var usable = batches.Where(b => !IsExpired(b, date))
            .OrderBy(b => b.ExpiryDate == null).ThenBy(b => b.ExpiryDate).ThenBy(b => b.ProductionDate ?? b.CreatedAt).ThenBy(b => b.CreatedAt).ToList();

        var result = new List<BatchAllocationDto>();
        var left = quantity;
        foreach (var b in usable)
        {
            if (left <= 0) break;
            var take = Math.Min(balances[b.Id], left);
            result.Add(Allocation(b, take));
            left -= take;
        }
        if (left <= 0) return result;

        if (!allowShortage)
        {
            var expired = batches.Where(b => IsExpired(b, date)).Sum(b => balances[b.Id]);
            throw new ConflictException(string.Format(Messages.InsufficientValidBatchStock, product.NameAr, Qty(quantity - left), Qty(quantity),
                expired > 0 ? string.Format(Messages.ExpiredStockNote, Qty(expired)) : string.Empty));
        }
        // النقص المسموح: على آخر تشغيلة صالحة صُرف منها (أو الافتتاحية)، فيظهر سالباً للمراجعة
        var target = usable.LastOrDefault() ?? await OpeningAsync(db, product.Id, ct);
        var existing = result.FirstOrDefault(a => a.ItemBatchId == target.Id);
        if (existing != null) existing.Quantity += left;
        else result.Add(Allocation(target, left));
        return result;
    }

    // ---------------- الأرصدة وتسجيل التوزيع ----------------
    /// <summary>يزيد/ينقص رصيد تشغيلة في مستودع ويُرجع الرصيد بعده (يُحفظ مع حفظ المستدعي). معرّف الكيان يُولَّد عند إضافته.</summary>
    public static async Task<decimal> ApplyAsync(ErpDbContext db, ItemBatch batch, Guid warehouseId, decimal delta, CancellationToken ct)
    {
        await db.Set<BatchStock>().Where(s => s.ItemBatchId == batch.Id && s.WarehouseId == warehouseId).LoadAsync(ct);
        var row = db.Set<BatchStock>().Local.FirstOrDefault(s => s.ItemBatchId == batch.Id && s.WarehouseId == warehouseId);
        if (row == null)
        {
            row = new BatchStock { ItemId = batch.ItemId, ItemBatchId = batch.Id, WarehouseId = warehouseId };
            db.Add(row);
        }
        row.Quantity += delta;
        return row.Quantity;
    }

    /// <summary>يطبّق توزيع حركة على أرصدة التشغيلات ويسجّله، ويكتب ملخصه على الحركة.</summary>
    public static async Task RecordAsync(ErpDbContext db, StockMovement movement, IReadOnlyList<(ItemBatch Batch, decimal Quantity)> parts, int sign,
        Guid? sourceLineId, CancellationToken ct)
    {
        foreach (var (batch, qty) in parts)
        {
            await ApplyAsync(db, batch, movement.WarehouseId!.Value, sign * qty, ct);
            db.Add(new StockMovementBatch
            {
                StockMovementId = movement.Id, ItemId = movement.ItemId, ItemBatchId = batch.Id, WarehouseId = movement.WarehouseId!.Value,
                Quantity = sign * qty, SourceType = movement.SourceType, SourceId = movement.SourceId, SourceLineId = sourceLineId,
            });
        }
        movement.BatchSummary = Summary(parts.Select(p => (p.Batch.BatchNumber, p.Batch.ExpiryDate, p.Quantity)));
    }

    /// <summary>يسحب أثر حركة من أرصدة تشغيلاتها ويحذف توزيعها (قبل تعديل/حذف حركة يدوية).</summary>
    public static async Task UndoAsync(ErpDbContext db, StockMovement movement, CancellationToken ct)
    {
        var rows = await db.Set<StockMovementBatch>().Where(b => b.StockMovementId == movement.Id).ToListAsync(ct);
        if (rows.Count == 0) return;
        var batches = (await LoadAsync(db, rows.Select(r => r.ItemBatchId).Distinct().ToList(), ct)).ToDictionary(b => b.Id);
        foreach (var r in rows) await ApplyAsync(db, batches[r.ItemBatchId], r.WarehouseId, -r.Quantity, ct);
        db.RemoveRange(rows);
        movement.BatchSummary = null;
    }

    /// <summary>
    /// تشغيلات مستند لصنف (غير المعكوسة) بكمياتها المطلقة: أساس المرتجع (يعود لتشغيلات أصله) وعكس المستند (يعود لتشغيلاته).
    /// </summary>
    public static async Task<List<BatchAllocationDto>> OfDocumentAsync(ErpDbContext db, string sourceType, Guid sourceId, Guid itemId, Guid? lineId,
        CancellationToken ct)
    {
        var rows = await db.Set<StockMovementBatch>().AsNoTracking()
            .Where(b => b.SourceType == sourceType && b.SourceId == sourceId && b.ItemId == itemId && !b.IsReversed && (lineId == null || b.SourceLineId == lineId))
            .GroupBy(b => b.ItemBatchId).Select(g => new { Id = g.Key, Qty = g.Sum(b => b.Quantity) }).ToListAsync(ct);
        var batches = (await LoadAsync(db, rows.Select(r => r.Id).ToList(), ct)).ToDictionary(b => b.Id);
        return rows.Where(r => r.Qty != 0).Select(r => Allocation(batches[r.Id], Math.Abs(r.Qty)))
            .OrderBy(a => a.ExpiryDate == null).ThenBy(a => a.ExpiryDate).ToList();
    }

    /// <summary>يعلّم توزيع مستند معكوساً (تعديل/حذف فاتورة مرحّلة) فلا يُحسب في تشغيلاتها الحالية ولا في مرتجعاتها.</summary>
    public static async Task MarkReversedAsync(ErpDbContext db, string sourceType, Guid sourceId, CancellationToken ct)
    {
        foreach (var row in await db.Set<StockMovementBatch>().Where(b => b.SourceType == sourceType && b.SourceId == sourceId && !b.IsReversed).ToListAsync(ct))
            row.IsReversed = true;
    }

    /// <summary>يقتطع كمية من قائمة تخصيصات بترتيبها (يستهلكها): توزيع سطر من تخصيصات صنف على مستوى المستند.</summary>
    public static List<BatchAllocationDto> Take(List<BatchAllocationDto> pool, decimal quantity)
    {
        var result = new List<BatchAllocationDto>();
        foreach (var a in pool)
        {
            if (quantity <= 0) break;
            var take = Math.Min(a.Quantity, quantity);
            if (take <= 0) continue;
            result.Add(new BatchAllocationDto { ItemBatchId = a.ItemBatchId, BatchNumber = a.BatchNumber, ExpiryDate = a.ExpiryDate, Quantity = take });
            a.Quantity -= take; quantity -= take;
        }
        return result;
    }

    public static string Summary(IEnumerable<(string Number, DateTime? Expiry, decimal Quantity)> parts)
    {
        var list = parts.ToList();
        string One((string Number, DateTime? Expiry, decimal Quantity) p) => p.Expiry is { } e ? $"{p.Number} ({Day(e)})" : p.Number;
        return list.Count == 1 ? One(list[0]) : string.Join("، ", list.Select(p => $"{One(p)} × {Qty(p.Quantity)}"));
    }

    /// <summary>أرصدة تشغيلات صنف في مستودع (المحفوظة مع تغييرات المعاملة الجارية).</summary>
    public static async Task<Dictionary<Guid, decimal>> BalancesAsync(ErpDbContext db, Guid itemId, Guid warehouseId, CancellationToken ct)
    {
        await db.Set<BatchStock>().Where(s => s.ItemId == itemId && s.WarehouseId == warehouseId).LoadAsync(ct);
        return db.Set<BatchStock>().Local.Where(s => s.ItemId == itemId && s.WarehouseId == warehouseId)
            .GroupBy(s => s.ItemBatchId).ToDictionary(g => g.Key, g => g.Sum(s => s.Quantity));
    }

    public static BatchAllocationDto Allocation(ItemBatch b, decimal qty)
        => new() { ItemBatchId = b.Id, BatchNumber = b.BatchNumber, ExpiryDate = b.ExpiryDate, Quantity = qty };

    public static async Task<List<ItemBatch>> LoadAsync(ErpDbContext db, List<Guid> ids, CancellationToken ct)
    {
        var local = db.Set<ItemBatch>().Local.Where(b => ids.Contains(b.Id)).ToList();
        var missing = ids.Except(local.Select(b => b.Id)).ToList();
        if (missing.Count > 0) local.AddRange(await db.Set<ItemBatch>().Where(b => missing.Contains(b.Id)).ToListAsync(ct));
        return local;
    }

    private static async Task<ItemBatch?> FindAsync(ErpDbContext db, Guid itemId, Guid id, CancellationToken ct)
        => db.Set<ItemBatch>().Local.FirstOrDefault(b => b.Id == id && b.ItemId == itemId)
            ?? await db.Set<ItemBatch>().FirstOrDefaultAsync(b => b.Id == id && b.ItemId == itemId, ct);

    private static string Day(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Qty(decimal q) => q.ToString("0.####", CultureInfo.InvariantCulture);
}
