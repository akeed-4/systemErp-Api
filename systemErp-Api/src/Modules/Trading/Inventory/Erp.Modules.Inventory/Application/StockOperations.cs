using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.Accounting.Contracts;
using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Inventory.Domain;
using Erp.Modules.Inventory.Persistence;
using Erp.Modules.Settings.Contracts;
using Erp.SharedKernel.Errors;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Inventory.Application;

internal sealed record AdjustmentLineRequest(Guid ProductId, decimal Quantity, decimal? UnitCost, string? Notes);

/// <param name="Lines">Positive quantity = stock found / added; negative = shortage / damage.</param>
internal sealed record StockAdjustmentRequest(Guid? WarehouseId, DateOnly? Date, string Reason, IReadOnlyList<AdjustmentLineRequest> Lines);

internal sealed record TransferLineRequest(Guid ProductId, decimal Quantity);

internal sealed record StockTransferRequest(Guid FromWarehouseId, Guid ToWarehouseId, DateOnly? Date, string? Notes, IReadOnlyList<TransferLineRequest> Lines);

internal sealed record StockDocumentResult(Guid DocumentId, string DocumentNumber, decimal ValueIn, decimal ValueOut, Guid? JournalEntryId);

internal sealed record RecalculationResult(int BalancesRecalculated, int MovementsReplayed);

/// <summary>Inventory's own documents: adjustments (posted to accounting), transfers (no GL effect) and cost recalculation.</summary>
internal sealed class StockOperations(
    InventoryDbContext db,
    IUnitOfWork unitOfWork,
    IInventoryService inventory,
    IWarehouseDirectory warehouses,
    INumberSequenceService numbers,
    IAccountingPostingService posting,
    TimeProvider clock)
{
    public async Task<StockDocumentResult> AdjustAsync(StockAdjustmentRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Lines.Count == 0 || request.Lines.Any(l => l.Quantity == 0))
        {
            throw ErpException.Validation("A reason and non-zero quantities are required.", "سبب التسوية وكميات غير صفرية مطلوبة.");
        }

        return await unitOfWork.ExecuteAsync(
            async innerCt =>
            {
                var date = request.Date ?? Today();
                var warehouseId = request.WarehouseId ?? (await warehouses.GetDefaultAsync(innerCt)).Id;
                var number = await numbers.NextAsync("adj", date, null, innerCt);
                var document = new StockDocument(ProductService.Module, "stock_adjustment", Guid.CreateVersion7(), number, date);

                var increases = new List<StockLine>();
                foreach (var line in request.Lines.Where(l => l.Quantity > 0))
                {
                    var cost = line.UnitCost ?? await inventory.GetUnitCostAsync(line.ProductId, warehouseId, innerCt);
                    increases.Add(new StockLine(line.ProductId, warehouseId, line.Quantity, cost, Notes: line.Notes));
                }

                var decreases = request.Lines.Where(l => l.Quantity < 0)
                    .Select(l => new StockLine(l.ProductId, warehouseId, -l.Quantity, Notes: l.Notes)).ToList();

                var valueIn = increases.Count > 0 ? (await inventory.ReceiveAsync(document, StockMovementType.AdjustmentIn, increases, innerCt)).Sum(r => r.TotalCost) : 0;
                var valueOut = decreases.Count > 0 ? (await inventory.IssueAsync(document, StockMovementType.AdjustmentOut, decreases, innerCt)).Sum(r => r.TotalCost) : 0;

                var postingLines = new List<PostingLine>();
                if (valueIn > 0)
                {
                    postingLines.Add(new PostingLine(AccountRef.For(PostingPurpose.Inventory), valueIn, 0));
                    postingLines.Add(new PostingLine(AccountRef.For(PostingPurpose.InventoryAdjustment), 0, valueIn));
                }

                if (valueOut > 0)
                {
                    postingLines.Add(new PostingLine(AccountRef.For(PostingPurpose.InventoryAdjustment), valueOut, 0));
                    postingLines.Add(new PostingLine(AccountRef.For(PostingPurpose.Inventory), 0, valueOut));
                }

                Guid? journalEntryId = null;
                if (postingLines.Count > 0)
                {
                    journalEntryId = (await posting.PostAsync(
                        new PostingRequest(new SourceRef(document.Module, document.DocumentType, document.DocumentId, number), "adjustment", date, $"تسوية مخزنية {number}: {request.Reason}", postingLines),
                        innerCt)).JournalEntryId;
                }

                return new StockDocumentResult(document.DocumentId, number, valueIn, valueOut, journalEntryId);
            },
            ct);
    }

    public async Task<StockDocumentResult> TransferAsync(StockTransferRequest request, CancellationToken ct)
    {
        if (request.FromWarehouseId == request.ToWarehouseId || request.Lines.Count == 0)
        {
            throw ErpException.Validation("Choose two different warehouses and at least one line.", "يرجى اختيار مستودعين مختلفين وصنف واحد على الأقل.");
        }

        return await unitOfWork.ExecuteAsync(
            async innerCt =>
            {
                var date = request.Date ?? Today();
                var number = await numbers.NextAsync("trf", date, null, innerCt);
                var document = new StockDocument(ProductService.Module, "stock_transfer", Guid.CreateVersion7(), number, date);

                var issued = await inventory.IssueAsync(
                    document,
                    StockMovementType.TransferOut,
                    request.Lines.Select(l => new StockLine(l.ProductId, request.FromWarehouseId, l.Quantity, Notes: request.Notes)).ToList(),
                    innerCt);

                // Goods arrive at the cost they left with, so total inventory value is unchanged (no journal entry).
                var received = await inventory.ReceiveAsync(
                    document,
                    StockMovementType.TransferIn,
                    issued.Select(i => new StockLine(i.ProductId, request.ToWarehouseId, i.Quantity, i.UnitCost, Notes: request.Notes)).ToList(),
                    innerCt);

                return new StockDocumentResult(document.DocumentId, number, received.Sum(r => r.TotalCost), issued.Sum(i => i.TotalCost), null);
            },
            ct);
    }

    /// <summary>
    /// Replays every movement of every item to rebuild quantities, moving-average costs and running balances.
    /// A repair tool: posted costs (and so the ledger) are never changed.
    /// </summary>
    public async Task<RecalculationResult> RecalculateAsync(CancellationToken ct) =>
        await unitOfWork.ExecuteAsync(
            async innerCt =>
            {
                var movements = await db.StockMovements.OrderBy(m => m.Date).ThenBy(m => m.CreatedAt).ToListAsync(innerCt);
                var balances = await db.StockBalances.ToListAsync(innerCt);
                var originals = movements.ToDictionary(m => m.Id);

                foreach (var group in movements.GroupBy(m => (m.ProductId, m.WarehouseId)))
                {
                    var balance = balances.SingleOrDefault(b => b.ProductId == group.Key.ProductId && b.WarehouseId == group.Key.WarehouseId);
                    if (balance is null)
                    {
                        balance = new StockBalance(group.Key.ProductId, group.Key.WarehouseId);
                        db.StockBalances.Add(balance);
                        balances.Add(balance);
                    }

                    balance.Reset(0, 0, 0);
                    foreach (var movement in group)
                    {
                        var reversesInbound = movement.ReversalOfId is { } originalId && originals.TryGetValue(originalId, out var original) && original.IsInbound;
                        if (reversesInbound)
                        {
                            balance.UndoReceipt(movement.Quantity, movement.UnitCost);
                        }
                        else if (movement.IsInbound)
                        {
                            balance.Receive(movement.Quantity, movement.UnitCost, isPurchase: movement.Type == StockMovementType.InPurchase);
                        }
                        else
                        {
                            balance.Issue(movement.Quantity);
                        }

                        movement.RecordResult(balance.QuantityOnHand, balance.AverageCost);
                    }
                }

                return new RecalculationResult(balances.Count, movements.Count);
            },
            ct);

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
}
