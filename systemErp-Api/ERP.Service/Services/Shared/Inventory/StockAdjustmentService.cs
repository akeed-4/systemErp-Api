using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Service.Data;
using ERP.Service.Services.Accounting;

namespace ERP.Service.Services.Shared;

/// <summary>
/// تسوية مخزون يدوية = حركة عبر محرك المخزون + قيد عبر المحرك المحاسبي بتغيّر القيمة الدفترية للصنف:
/// الإضافة مدين المخزون / دائن الحساب المقابل، والخصم عكسه. الحساب المقابل فروقات الجرد افتراضياً،
/// أو ما يحدده المستخدم (مثل الأرصدة الافتتاحية لمخزون أول المدة). كل ذلك في معاملة واحدة.
/// </summary>
public class StockAdjustmentService : IStockAdjustmentService
{
    private const string SourceType = "stock_adjustment";

    private readonly ErpDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly IAccountingPostingService _posting;
    private readonly ITransactionRunner _tx;

    public StockAdjustmentService(ErpDbContext db, IInventoryService inventory, IAccountingPostingService posting, ITransactionRunner tx)
    {
        _db = db; _inventory = inventory; _posting = posting; _tx = tx;
    }

    public Task<StockMovementDto> AdjustAsync(RecordStockMovementDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            EnsureAdjustment(r);
            r.SourceType = "manual";
            var recorded = await _inventory.RecordMovementAsync(r, token);
            return await PostAsync(recorded.Id, r.CounterAccountCode, recorded.ValueChange, token);
        }, ct);

    /// <summary>
    /// تعديل تسوية (ولو بتاريخ سابق): يُعكس قيدها وتُعاد الحركة فيُعاد بناء تكلفة الصنف، ثم يُرحَّل ما تغيّر في قيمته الدفترية فعلاً
    /// = (القيمة بعد − القيمة قبل) + ما كان القيد القديم أضافه. فيشمل القيد أثر إعادة التسعير على بقية الرصيد ولا يفترق المخزون عن حسابه.
    /// </summary>
    public Task<StockMovementDto> UpdateAsync(Guid id, RecordStockMovementDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            EnsureAdjustment(r);
            var (itemId, oldSigned) = await CurrentAsync(id, token);
            var before = await ValueBeforeAsync(itemId, token);
            await ReverseEntryAsync(id, "تعديل", token);
            await _inventory.UpdateMovementAsync(id, r, token);
            return await PostAsync(id, r.CounterAccountCode, await ValueAfterAsync(itemId, token) - before + oldSigned, token);
        }, ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var (itemId, oldSigned) = await CurrentAsync(id, token);
            var before = await ValueBeforeAsync(itemId, token);
            await ReverseEntryAsync(id, "حذف", token);
            await _inventory.DeleteMovementAsync(id, token);
            // ما بقي بعد عكس القيد = أثر إعادة بناء التكلفة على بقية الرصيد (صرف سابق أعيد تسعيره)
            await InventoryValuation.PostRevaluationAsync(_db, _posting, new[] { (itemId, await ValueAfterAsync(itemId, token) - before + oldSigned) },
                "إعادة تقييم المخزون بعد حذف تسوية", id, token);
        }, ct);

    private static void EnsureAdjustment(RecordStockMovementDto r)
    {
        if (r.Type is not (StockMovementType.AdjustmentIn or StockMovementType.AdjustmentOut))
            throw new ValidationFailedException(Messages.ManualAdjustmentTypeOnly);
    }

    /// <summary>صنف الحركة، وما أضافه قيدها الحالي إلى حساب المخزون (بإشارته).</summary>
    private async Task<(Guid ItemId, decimal OldSigned)> CurrentAsync(Guid movementId, CancellationToken ct)
    {
        var movement = await _db.Set<StockMovement>().AsNoTracking().Where(m => m.Id == movementId)
            .Select(m => new { m.ItemId, m.JournalEntryId, m.CounterAccountCode }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.StockMovementNotFound);
        var oldSigned = movement.JournalEntryId == null ? 0m
            : await _db.Set<JournalEntryLine>().AsNoTracking()
                .Where(l => l.JournalEntryId == movement.JournalEntryId && l.AccountCode != (movement.CounterAccountCode ?? DefaultAccounts.InventoryAdjustment))
                .SumAsync(l => l.Debit - l.Credit, ct);
        return (movement.ItemId, oldSigned);
    }

    private async Task<decimal> ValueBeforeAsync(Guid itemId, CancellationToken ct)
        => InventoryValuation.Value(await _db.Set<Product>().AsNoTracking().FirstAsync(p => p.Id == itemId, ct));

    /// <summary>الصنف المتتبَّع بعد إعادة بناء تكلفته في هذه المعاملة.</summary>
    private async Task<decimal> ValueAfterAsync(Guid itemId, CancellationToken ct)
        => InventoryValuation.Value(await _db.Set<Product>().FirstAsync(p => p.Id == itemId, ct));

    /// <summary>
    /// يرحّل تغيّر القيمة الدفترية (موجب = زيادة المخزون) ويربط قيد التسوية بالحركة. الإضافة قيدها بقيمتها المعلنة (الكمية × التكلفة)
    /// على حسابها المقابل، وما زاد أو نقص عنها (إعادة تسعير بقية الرصيد أو صرف سابق) قيد إعادة تقييم على حساب فروق التكلفة؛
    /// والخصم قيده بتغيّر القيمة كله. التغيّر الصفري لا قيد له.
    /// في الجرد الدوري لا قيد للتسوية العادية (أثرها في بضاعة آخر المدة)، والتسوية مقابل حساب صريح (أرصدة افتتاحية مثلاً)
    /// تُرحَّل على بضاعة أول المدة بدل المخزون، بلا إعادة تقييم.
    /// </summary>
    private async Task<StockMovementDto> PostAsync(Guid movementId, string? counterAccountCode, decimal signedValue, CancellationToken ct)
    {
        var movement = await _db.Set<StockMovement>().FirstAsync(m => m.Id == movementId, ct);
        var counter = string.IsNullOrWhiteSpace(counterAccountCode) ? DefaultAccounts.InventoryAdjustment : counterAccountCode.Trim();
        var isIn = movement.Type == StockMovementType.AdjustmentIn;
        var own = isIn ? DocumentPricing.Round(movement.Quantity * movement.UnitCost) : signedValue;
        var note = $"تسوية مخزون {(isIn ? "بالإضافة" : "بالخصم")} - {movement.ItemName}";
        movement.CounterAccountCode = counter;
        var accounts = await PostingAccounts.SettingsAsync(_db, ct);
        if (accounts.IsPeriodic(movement.Date))
        {
            await PeriodicInventory.EnsureMethodOpeningAsync(_db, _posting, accounts, movement.Date, ct);
            movement.JournalEntryId = own == 0 || string.IsNullOrWhiteSpace(counterAccountCode) ? null
                : (await PostEntryAsync(movement.Id, movement.ReferenceNumber, movement.Date, accounts.OpeningInventory(), counter, own, note, ct)).JournalEntryId;
        }
        else
        {
            movement.JournalEntryId = own == 0 ? null
                : (await PostEntryAsync(movement.Id, movement.ReferenceNumber, movement.Date, await accounts.InventoryAsync(_db, movement.WarehouseId, ct),
                    counter, own, note, ct)).JournalEntryId;
            // ما زاد عن قيمة الإضافة إعادة تسعير للرصيد السابق لها: يُوزَّع على المستودعات بكمياتها قبل الإضافة
            await InventoryValuation.PostRevaluationAsync(_db, _posting, new[] { (movement.ItemId, signedValue - own) },
                $"إعادة تقييم المخزون بأثر التسوية {movement.ReferenceNumber} - {movement.ItemName}", movement.Id, ct,
                isIn && movement.WarehouseId is { } warehouseId ? (warehouseId, movement.Quantity) : null);
        }
        await _db.SaveChangesAsync(ct);
        return Mapper.Map<StockMovementDto>(movement);
    }

    private async Task<PostingResult> PostEntryAsync(Guid movementId, string referenceNumber, DateTime date, string inventory, string counter,
        decimal signedValue, string note, CancellationToken ct)
    {
        await DefaultAccounts.EnsureAsync(_db, ct, inventory, counter);
        var value = Math.Abs(signedValue);
        return await _posting.PostAsync(new GenericPostingRequest
        {
            Date = date, Description = note, SourceType = SourceType, SourceId = movementId, SourceNumber = referenceNumber,
            Lines = signedValue > 0
                ? new() { new(inventory, value, 0, note), new(counter, 0, value, note) }
                : new() { new(counter, value, 0, note), new(inventory, 0, value, note) },
        }, ct);
    }

    private async Task ReverseEntryAsync(Guid movementId, string reason, CancellationToken ct)
    {
        var movement = await _db.Set<StockMovement>().AsNoTracking().Where(m => m.Id == movementId)
            .Select(m => new { m.JournalEntryId, m.ReferenceNumber }).FirstOrDefaultAsync(ct) ?? throw new NotFoundException(Messages.StockMovementNotFound);
        if (movement.JournalEntryId.HasValue)
            await _posting.ReverseAsync(movement.JournalEntryId.Value, $"{reason} تسوية المخزون {movement.ReferenceNumber}".Trim(), ct);
    }
}
