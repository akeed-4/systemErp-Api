namespace Erp.Modules.Inventory.Contracts;

/// <summary>Normal (non-vehicle) products only. Vehicles live in the Vehicles module and never in the product catalog.</summary>
public sealed record ProductSummary(
    Guid Id,
    string Sku,
    string? Barcode,
    string NameAr,
    string NameEn,
    Guid CategoryId,
    Guid UnitId,
    string UnitCode,
    string UnitNameAr,
    decimal SellingPrice,
    decimal VatRate,
    bool IsActive);

public interface IProductCatalog
{
    Task<ProductSummary?> FindAsync(Guid productId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, ProductSummary>> FindManyAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken);

    Task<ProductSummary?> FindByBarcodeAsync(string barcodeOrSku, CancellationToken cancellationToken);
}

public sealed record WarehouseSummary(Guid Id, string Code, string NameAr, string NameEn, Guid? BranchId, bool IsDefault, bool IsActive);

public interface IWarehouseDirectory
{
    Task<WarehouseSummary?> FindAsync(Guid warehouseId, CancellationToken cancellationToken);

    Task<WarehouseSummary> GetDefaultAsync(CancellationToken cancellationToken);
}

public enum StockMovementType
{
    Opening,
    InPurchase,
    OutSales,
    InReturn,
    OutReturn,
    AdjustmentIn,
    AdjustmentOut,
    TransferIn,
    TransferOut,
}

/// <summary>The business document behind a stock movement.</summary>
public sealed record StockDocument(string Module, string DocumentType, Guid DocumentId, string DocumentNumber, DateOnly Date);

/// <param name="UnitCost">Required when receiving goods; ignored when issuing (the costing method decides).</param>
public sealed record StockLine(Guid ProductId, Guid WarehouseId, decimal Quantity, decimal? UnitCost = null, decimal? UnitPrice = null, string? Notes = null);

/// <summary>The cost the inventory engine applied to a line: what callers post as COGS / inventory value.</summary>
public sealed record StockLineResult(Guid ProductId, Guid WarehouseId, decimal Quantity, decimal UnitCost, decimal TotalCost);

/// <summary>
/// Moves stock and values it (moving average by default, per the tenant's costing policy). Runs in the caller's unit of
/// work; the caller posts the financial effect (e.g. COGS / inventory) using the returned costs.
/// </summary>
public interface IInventoryService
{
    Task<IReadOnlyList<StockLineResult>> ReceiveAsync(StockDocument document, StockMovementType type, IReadOnlyList<StockLine> lines, CancellationToken cancellationToken);

    Task<IReadOnlyList<StockLineResult>> IssueAsync(StockDocument document, StockMovementType type, IReadOnlyList<StockLine> lines, CancellationToken cancellationToken);

    /// <summary>Undoes every live movement of the document (cancelled invoice, voided POS sale …) at its original cost.</summary>
    Task ReverseAsync(StockDocument document, CancellationToken cancellationToken);

    /// <summary>Cost the next issue of the product would use (for previews and quotations).</summary>
    Task<decimal> GetUnitCostAsync(Guid productId, Guid warehouseId, CancellationToken cancellationToken);
}
