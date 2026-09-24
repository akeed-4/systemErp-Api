using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Inventory.Domain;
using Erp.Modules.Inventory.Persistence;
using Erp.SharedKernel.Errors;
using Erp.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Inventory.Application;

/// <summary>
/// The stock engine: writes the movement ledger and keeps per-warehouse balances and moving-average cost.
/// Balance rows are read with UPDLOCK inside the caller's transaction, so concurrent issues of the same item serialize.
/// </summary>
internal sealed class InventoryEngine(InventoryDbContext db, IUnitOfWork unitOfWork, ITenantContext tenant) : IInventoryService
{
    public async Task<IReadOnlyList<StockLineResult>> ReceiveAsync(StockDocument document, StockMovementType type, IReadOnlyList<StockLine> lines, CancellationToken cancellationToken)
    {
        EnsureDirection(type, inbound: true);
        await unitOfWork.EnsureTransactionAsync(cancellationToken);
        await ValidateLinesAsync(lines, cancellationToken);

        var results = new List<StockLineResult>();
        foreach (var line in lines)
        {
            var unitCost = line.UnitCost ?? throw ErpException.Validation("A unit cost is required when receiving stock.", "تكلفة الوحدة مطلوبة عند إدخال المخزون.");
            if (unitCost < 0)
            {
                throw ErpException.Validation("The unit cost cannot be negative.", "لا يمكن أن تكون تكلفة الوحدة سالبة.");
            }

            var balance = await LockBalanceAsync(line.ProductId, line.WarehouseId, cancellationToken);
            balance.Receive(line.Quantity, unitCost, isPurchase: type == StockMovementType.InPurchase);
            AddMovement(line, type, document, unitCost, balance);
            results.Add(new StockLineResult(line.ProductId, line.WarehouseId, line.Quantity, unitCost, Round(line.Quantity * unitCost)));
        }

        return results;
    }

    public async Task<IReadOnlyList<StockLineResult>> IssueAsync(StockDocument document, StockMovementType type, IReadOnlyList<StockLine> lines, CancellationToken cancellationToken)
    {
        EnsureDirection(type, inbound: false);
        await unitOfWork.EnsureTransactionAsync(cancellationToken);
        var products = await ValidateLinesAsync(lines, cancellationToken);
        var policy = await PolicyAsync(cancellationToken);

        var results = new List<StockLineResult>();
        foreach (var line in lines)
        {
            var product = products[line.ProductId];
            var balance = await LockBalanceAsync(line.ProductId, line.WarehouseId, cancellationToken);
            if (balance.QuantityOnHand < line.Quantity && policy.NegativeInventoryPolicy == NegativeInventoryPolicy.Prohibit)
            {
                throw ErpException.Conflict(
                    "insufficient_stock",
                    $"Not enough stock of {product.NameEn}: {balance.QuantityOnHand:0.###} available, {line.Quantity:0.###} requested.",
                    $"الكمية غير متوفرة للصنف {product.NameAr}: المتاح {balance.QuantityOnHand:0.###} والمطلوب {line.Quantity:0.###}.");
            }

            var unitCost = UnitCost(policy, product, balance);
            balance.Issue(line.Quantity);
            AddMovement(line, type, document, unitCost, balance);
            results.Add(new StockLineResult(line.ProductId, line.WarehouseId, line.Quantity, unitCost, Round(line.Quantity * unitCost)));
        }

        return results;
    }

    public async Task ReverseAsync(StockDocument document, CancellationToken cancellationToken)
    {
        await unitOfWork.EnsureTransactionAsync(cancellationToken);
        var movements = await db.StockMovements
            .Where(m => m.SourceModule == document.Module && m.SourceDocumentType == document.DocumentType && m.SourceDocumentId == document.DocumentId
                && !m.IsReversed && m.ReversalOfId == null)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        foreach (var original in movements)
        {
            var balance = await LockBalanceAsync(original.ProductId, original.WarehouseId, cancellationToken);
            if (original.IsInbound)
            {
                balance.UndoReceipt(original.Quantity, original.UnitCost);
            }
            else
            {
                balance.Receive(original.Quantity, original.UnitCost, isPurchase: false);
            }

            var reversal = new StockMovement(
                original.ProductId,
                original.WarehouseId,
                StockMovement.Opposite(original.Type),
                document with { Date = document.Date },
                original.Quantity,
                original.UnitCost,
                original.UnitPrice,
                $"عكس حركة {original.SourceNumber}");
            reversal.MarkAsReversalOf(original);
            reversal.RecordResult(balance.QuantityOnHand, balance.AverageCost);
            original.MarkReversed();
            db.StockMovements.Add(reversal);
        }
    }

    public async Task<decimal> GetUnitCostAsync(Guid productId, Guid warehouseId, CancellationToken cancellationToken)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == productId, cancellationToken)
            ?? throw ErpException.NotFound("Product", "الصنف");
        var balance = await db.StockBalances.AsNoTracking().SingleOrDefaultAsync(b => b.ProductId == productId && b.WarehouseId == warehouseId, cancellationToken)
            ?? new StockBalance(productId, warehouseId);
        return UnitCost(await PolicyAsync(cancellationToken), product, balance);
    }

    private static decimal UnitCost(CostingPolicy policy, Product product, StockBalance balance) =>
        policy.Method switch
        {
            CostingMethod.Standard when product.StandardCost is { } standard => standard,
            CostingMethod.LastPurchase when balance.LastPurchaseCost > 0 => balance.LastPurchaseCost,
            _ => balance.AverageCost > 0 ? balance.AverageCost : balance.LastPurchaseCost,
        };

    private void AddMovement(StockLine line, StockMovementType type, StockDocument document, decimal unitCost, StockBalance balance)
    {
        var movement = new StockMovement(line.ProductId, line.WarehouseId, type, document, line.Quantity, unitCost, line.UnitPrice, line.Notes);
        movement.RecordResult(balance.QuantityOnHand, balance.AverageCost);
        db.StockMovements.Add(movement);
    }

    /// <summary>The balance row for (product, warehouse), locked for update; created when the item was never stocked there.</summary>
    private async Task<StockBalance> LockBalanceAsync(Guid productId, Guid warehouseId, CancellationToken ct)
    {
        var tracked = db.StockBalances.Local.SingleOrDefault(b => b.ProductId == productId && b.WarehouseId == warehouseId);
        if (tracked is not null)
        {
            return tracked;
        }

        var tenantId = tenant.TenantId;
        var locked = await db.StockBalances
            .FromSqlInterpolated($"SELECT * FROM [inventory].[StockBalances] WITH (UPDLOCK, ROWLOCK) WHERE [TenantId] = {tenantId} AND [ProductId] = {productId} AND [WarehouseId] = {warehouseId}")
            .SingleOrDefaultAsync(ct);
        if (locked is not null)
        {
            return locked;
        }

        var created = new StockBalance(productId, warehouseId);
        db.StockBalances.Add(created);
        return created;
    }

    private async Task<Dictionary<Guid, Product>> ValidateLinesAsync(IReadOnlyList<StockLine> lines, CancellationToken ct)
    {
        if (lines.Count == 0 || lines.Any(l => l.Quantity <= 0))
        {
            throw ErpException.Validation("Stock lines need a positive quantity.", "يجب أن تكون الكميات أكبر من صفر.");
        }

        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        if (products.Count != productIds.Count)
        {
            throw ErpException.Validation("Unknown product in stock lines.", "يوجد صنف غير معروف في الأسطر.");
        }

        var warehouseIds = lines.Select(l => l.WarehouseId).Distinct().ToList();
        if (await db.Warehouses.CountAsync(w => warehouseIds.Contains(w.Id) && w.IsActive, ct) != warehouseIds.Count)
        {
            throw ErpException.Validation("Unknown or inactive warehouse.", "المستودع غير موجود أو غير نشط.");
        }

        return products;
    }

    private async Task<CostingPolicy> PolicyAsync(CancellationToken ct) =>
        await db.CostingPolicies.AsNoTracking().SingleOrDefaultAsync(ct) ?? new CostingPolicy(tenant.TenantId);

    private static void EnsureDirection(StockMovementType type, bool inbound)
    {
        var isInbound = type is StockMovementType.Opening or StockMovementType.InPurchase or StockMovementType.InReturn or StockMovementType.AdjustmentIn or StockMovementType.TransferIn;
        if (isInbound != inbound)
        {
            throw new ArgumentException($"Movement type {type} does not match the operation.", nameof(type));
        }
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
