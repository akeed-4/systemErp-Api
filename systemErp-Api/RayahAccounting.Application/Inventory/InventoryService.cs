using Microsoft.EntityFrameworkCore;
using RayahAccounting.Application.Interfaces;
using RayahAccounting.Domain.Entities;
using RayahAccounting.Domain.Enums;

namespace RayahAccounting.Application.Inventory;

public class InventoryService : IInventoryService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantService _tenantService;

    public InventoryService(IApplicationDbContext db, ITenantService tenantService)
    {
        _db = db;
        _tenantService = tenantService;
    }

    public async Task<StockMovement> RecordMovementAsync(StockMovementDto dto, CancellationToken ct = default)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == dto.ProductId, ct);
        if (product == null)
        {
            throw new KeyNotFoundException($"الصنف المطلوب غير موجود: {dto.ProductId}");
        }

        decimal newStock = product.CurrentStock;

        if (dto.MovementType == StockMovementType.InPurchase || dto.MovementType == StockMovementType.InTransfer || dto.MovementType == StockMovementType.InitialStock)
        {
            newStock += dto.Quantity;
            // Update Moving Average Cost on incoming purchases
            if (dto.MovementType == StockMovementType.InPurchase && dto.Quantity > 0)
            {
                var currentValuation = product.CurrentStock * product.WeightedAverageCost;
                var incomingValuation = dto.Quantity * dto.UnitCost;
                product.WeightedAverageCost = Math.Round((currentValuation + incomingValuation) / newStock, 2);
                product.LastPurchaseCost = dto.UnitCost;
            }
        }
        else if (dto.MovementType == StockMovementType.OutSale || dto.MovementType == StockMovementType.OutTransfer)
        {
            newStock -= dto.Quantity;
        }

        product.CurrentStock = newStock;

        var movement = new StockMovement
        {
            TenantId = _tenantService.CurrentTenantId,
            ProductId = product.Id,
            Sku = product.Sku,
            ProductNameAr = product.NameAr,
            WarehouseId = dto.WarehouseId,
            MovementType = dto.MovementType,
            Quantity = dto.Quantity,
            UnitCost = dto.UnitCost,
            ReferenceNumber = dto.ReferenceNumber,
            RemainingStock = newStock,
            MovementDate = DateTime.UtcNow,
            Notes = dto.Notes
        };

        _db.StockMovements.Add(movement);
        await _db.SaveChangesAsync(ct);

        return movement;
    }

    public async Task<MovingAverageSimulationResult> SimulateWeightedAverageCostAsync(string productId, decimal incomingQty, decimal purchasePrice, CancellationToken ct = default)
    {
        var prod = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (prod == null) throw new KeyNotFoundException("الصنف غير موجود");

        var oldStock = prod.CurrentStock;
        var oldAvgCost = prod.WeightedAverageCost;
        var oldVal = Math.Round(oldStock * oldAvgCost, 2);

        var newTotalStock = oldStock + incomingQty;
        var newTotalValuation = oldVal + (incomingQty * purchasePrice);
        var newAvg = newTotalStock > 0 ? Math.Round(newTotalValuation / newTotalStock, 2) : purchasePrice;
        var margin = prod.SellingPrice > 0 ? Math.Round(((prod.SellingPrice - newAvg) / prod.SellingPrice) * 100, 1) : 0m;

        return new MovingAverageSimulationResult(
            prod.Sku,
            prod.NameAr,
            oldStock,
            oldAvgCost,
            oldVal,
            incomingQty,
            purchasePrice,
            newTotalStock,
            newAvg,
            Math.Round(newTotalStock * newAvg, 2),
            margin
        );
    }

    public async Task<Product> ApplyMovingAverageCostAsync(string productId, decimal incomingQty, decimal purchasePrice, CancellationToken ct = default)
    {
        var prod = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (prod == null) throw new KeyNotFoundException("الصنف غير موجود");

        var sim = await SimulateWeightedAverageCostAsync(productId, incomingQty, purchasePrice, ct);
        prod.CurrentStock = sim.NewTotalStock;
        prod.WeightedAverageCost = sim.NewWeightedAverageCost;
        prod.LastPurchaseCost = purchasePrice;

        await _db.SaveChangesAsync(ct);
        return prod;
    }

    public async Task<List<Product>> GetLowStockAlertsAsync(CancellationToken ct = default)
    {
        return await _db.Products
            .Where(p => p.CurrentStock <= p.MinimumStockAlert)
            .OrderBy(p => p.CurrentStock)
            .ToListAsync(ct);
    }
}
