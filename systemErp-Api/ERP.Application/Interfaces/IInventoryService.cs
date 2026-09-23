using ERP.Domain.Entities;
using ERP.Domain.Enums;

namespace ERP.Application.Interfaces;

public record StockMovementDto(
    string ProductId,
    StockMovementType MovementType,
    decimal Quantity,
    decimal UnitCost,
    string ReferenceNumber
);

public record MovingAverageSimulationResult(
    string Sku,
    string ProductNameAr,
    decimal OldStock,
    decimal OldAvgCost,
    decimal OldValuation,
    decimal NewIncomingQty,
    decimal NewPurchasePrice,
    decimal NewTotalStock,
    decimal NewWeightedAverageCost,
    decimal NewValuation,
    decimal MarginPercent
);

public interface IInventoryService
{
    Task<StockMovement> RecordMovementAsync(StockMovementDto dto, CancellationToken ct = default);
    Task<MovingAverageSimulationResult> SimulateWeightedAverageCostAsync(string productId, decimal incomingQty, decimal purchasePrice, CancellationToken ct = default);
    Task<Product> ApplyMovingAverageCostAsync(string productId, decimal incomingQty, decimal purchasePrice, CancellationToken ct = default);
    Task<List<Product>> GetLowStockAlertsAsync(CancellationToken ct = default);
}
