using Microsoft.EntityFrameworkCore;
using ERP.Application.Costing;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;

namespace ERP.Application.Inventory;

/// <summary>
/// محرك المخزون المركزي - المصدر الوحيد لتسجيل حركات الوارد/الصادر وتحديث متوسط التكلفة.
/// كل وحدة (مشتريات، مبيعات، نقاط بيع) يجب أن تستخدم هذه الخدمة بدلاً من تعديل رصيد الصنف مباشرة.
/// </summary>
public class InventoryService : IInventoryService
{
    private readonly IApplicationDbContext _db;

    public InventoryService(IApplicationDbContext db)
    {
        _db = db;
    }

    private static bool IsIncoming(StockMovementType type) =>
        type is StockMovementType.InPurchase or StockMovementType.AdjustmentIn;

    public async Task<StockMovement> RecordMovementAsync(StockMovementDto dto, CancellationToken ct = default)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == Guid.Parse(dto.ProductId), ct)
            ?? throw new KeyNotFoundException($"الصنف المطلوب غير موجود: {dto.ProductId}");

        var incoming = IsIncoming(dto.MovementType);

        if (incoming && dto.MovementType == StockMovementType.InPurchase && dto.Quantity > 0)
        {
            product.AverageCost = MovingAverageCalculator.CalculateNewAverageCost(
                product.CurrentStock, product.AverageCost, dto.Quantity, dto.UnitCost);
            product.LastPurchaseCost = dto.UnitCost;
        }

        product.CurrentStock += incoming ? dto.Quantity : -dto.Quantity;

        var movement = new StockMovement
        {
            ItemId = product.Id,
            ItemName = product.NameAr,
            Date = DateTime.UtcNow,
            Type = dto.MovementType,
            Quantity = dto.Quantity,
            UnitCost = dto.UnitCost,
            ReferenceNumber = dto.ReferenceNumber,
            RemainingStock = product.CurrentStock,
        };

        _db.StockMovements.Add(movement);
        await _db.SaveChangesAsync(ct);

        return movement;
    }

    public async Task<MovingAverageSimulationResult> SimulateWeightedAverageCostAsync(string productId, decimal incomingQty, decimal purchasePrice, CancellationToken ct = default)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == Guid.Parse(productId), ct)
            ?? throw new KeyNotFoundException("الصنف غير موجود");

        var oldStock = product.CurrentStock;
        var oldAvgCost = product.AverageCost;
        var oldValuation = oldStock * oldAvgCost;
        var newTotalStock = oldStock + incomingQty;
        var newAvgCost = MovingAverageCalculator.CalculateNewAverageCost(oldStock, oldAvgCost, incomingQty, purchasePrice);
        var newValuation = newTotalStock * newAvgCost;
        var margin = newAvgCost > 0 ? ((product.SellingPrice - newAvgCost) / newAvgCost) * 100 : 0m;

        return new MovingAverageSimulationResult(
            product.Sku, product.NameAr,
            oldStock, oldAvgCost, oldValuation,
            incomingQty, purchasePrice,
            newTotalStock, newAvgCost, newValuation,
            Math.Round(margin, 1));
    }

    public async Task<Product> ApplyMovingAverageCostAsync(string productId, decimal incomingQty, decimal purchasePrice, CancellationToken ct = default)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == Guid.Parse(productId), ct)
            ?? throw new KeyNotFoundException("الصنف غير موجود");

        product.AverageCost = MovingAverageCalculator.CalculateNewAverageCost(product.CurrentStock, product.AverageCost, incomingQty, purchasePrice);
        product.CurrentStock += incomingQty;
        product.LastPurchaseCost = purchasePrice;

        await _db.SaveChangesAsync(ct);
        return product;
    }

    public async Task<List<Product>> GetLowStockAlertsAsync(CancellationToken ct = default)
    {
        return await _db.Products
            .Where(p => p.CurrentStock <= p.MinStockLevel)
            .OrderBy(p => p.CurrentStock)
            .ToListAsync(ct);
    }
}
