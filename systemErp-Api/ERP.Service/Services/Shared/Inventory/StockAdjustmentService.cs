using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Service.Data;
using ERP.Service.Services.Accounting;

namespace ERP.Service.Services.Shared;

/// <summary>
/// تسوية مخزون يدوية = حركة عبر محرك المخزون + قيد عبر المحرك المحاسبي بقيمتها (الكمية × التكلفة):
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
            return await PostAsync(recorded.Id, r.CounterAccountCode, token);
        }, ct);

    public Task<StockMovementDto> UpdateAsync(Guid id, RecordStockMovementDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            EnsureAdjustment(r);
            await ReverseEntryAsync(id, "تعديل", token);
            await _inventory.UpdateMovementAsync(id, r, token);
            return await PostAsync(id, r.CounterAccountCode, token);
        }, ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            await ReverseEntryAsync(id, "حذف", token);
            await _inventory.DeleteMovementAsync(id, token);
        }, ct);

    private static void EnsureAdjustment(RecordStockMovementDto r)
    {
        if (r.Type is not (StockMovementType.AdjustmentIn or StockMovementType.AdjustmentOut))
            throw new ValidationFailedException(Messages.ManualAdjustmentTypeOnly);
    }

    /// <summary>يرحّل قيد التسوية بقيمتها ويربطه بالحركة. قيمة صفرية (كمية بلا تكلفة) لا قيد لها.</summary>
    private async Task<StockMovementDto> PostAsync(Guid movementId, string? counterAccountCode, CancellationToken ct)
    {
        var movement = await _db.Set<StockMovement>().FirstAsync(m => m.Id == movementId, ct);
        var counter = string.IsNullOrWhiteSpace(counterAccountCode) ? DefaultAccounts.InventoryAdjustment : counterAccountCode.Trim();
        var value = DocumentPricing.Round(movement.Quantity * movement.UnitCost);
        movement.CounterAccountCode = counter;
        movement.JournalEntryId = null;

        if (value > 0)
        {
            await DefaultAccounts.EnsureAsync(_db, ct, DefaultAccounts.Inventory, counter);
            var isIn = movement.Type == StockMovementType.AdjustmentIn;
            var note = $"تسوية مخزون {(isIn ? "بالإضافة" : "بالخصم")} - {movement.ItemName}";
            var posted = await _posting.PostAsync(new GenericPostingRequest
            {
                Date = movement.Date, Description = note, SourceType = SourceType, SourceId = movement.Id, SourceNumber = movement.ReferenceNumber,
                Lines = isIn
                    ? new() { new(DefaultAccounts.Inventory, value, 0, note), new(counter, 0, value, note) }
                    : new() { new(counter, value, 0, note), new(DefaultAccounts.Inventory, 0, value, note) },
            }, ct);
            movement.JournalEntryId = posted.JournalEntryId;
        }
        await _db.SaveChangesAsync(ct);
        return Mapper.Map<StockMovementDto>(movement);
    }

    private async Task ReverseEntryAsync(Guid movementId, string reason, CancellationToken ct)
    {
        var movement = await _db.Set<StockMovement>().AsNoTracking().Where(m => m.Id == movementId)
            .Select(m => new { m.JournalEntryId, m.ReferenceNumber }).FirstOrDefaultAsync(ct) ?? throw new NotFoundException(Messages.StockMovementNotFound);
        if (movement.JournalEntryId.HasValue)
            await _posting.ReverseAsync(movement.JournalEntryId.Value, $"{reason} تسوية المخزون {movement.ReferenceNumber}".Trim(), ct);
    }
}
