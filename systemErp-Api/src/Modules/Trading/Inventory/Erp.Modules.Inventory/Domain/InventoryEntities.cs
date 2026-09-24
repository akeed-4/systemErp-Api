using Erp.Modules.Inventory.Contracts;
using Erp.SharedKernel.Domain;

namespace Erp.Modules.Inventory.Domain;

internal sealed class ProductCategory : TenantEntity
{
    private ProductCategory()
    {
    }

    public ProductCategory(string code) => Code = code.Trim().ToUpperInvariant();

    public string Code { get; private set; } = string.Empty;

    public string NameAr { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    public Guid? ParentId { get; private set; }

    public string? Description { get; private set; }

    public void Update(string nameAr, string nameEn, Guid? parentId, string? description)
    {
        NameAr = nameAr.Trim();
        NameEn = string.IsNullOrWhiteSpace(nameEn) ? NameAr : nameEn.Trim();
        ParentId = parentId;
        Description = description;
    }
}

internal sealed class UnitOfMeasure : TenantEntity
{
    private UnitOfMeasure()
    {
    }

    public UnitOfMeasure(string code) => Code = code.Trim().ToUpperInvariant();

    public string Code { get; private set; } = string.Empty;

    public string NameAr { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    public string Symbol { get; private set; } = string.Empty;

    public bool IsBaseUnit { get; private set; }

    public Guid? BaseUnitId { get; private set; }

    public decimal ConversionFactor { get; private set; } = 1;

    public bool IsActive { get; private set; } = true;

    public void Update(string nameAr, string nameEn, string symbol, Guid? baseUnitId, decimal conversionFactor, bool isActive)
    {
        NameAr = nameAr.Trim();
        NameEn = string.IsNullOrWhiteSpace(nameEn) ? NameAr : nameEn.Trim();
        Symbol = symbol.Trim();
        BaseUnitId = baseUnitId;
        IsBaseUnit = baseUnitId is null;
        ConversionFactor = baseUnitId is null ? 1 : conversionFactor;
        IsActive = isActive;
    }
}

/// <summary>inventory.Products: a normal item (POS / general trading). No vehicle fields, ever (§14 D5).</summary>
internal sealed class Product : TenantEntity, ISoftDeletable
{
    private Product()
    {
    }

    public Product(string sku) => Sku = sku.Trim().ToUpperInvariant();

    public string Sku { get; private set; } = string.Empty;

    public string? Barcode { get; private set; }

    public string NameAr { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    public Guid CategoryId { get; private set; }

    public Guid UnitId { get; private set; }

    public decimal SellingPrice { get; private set; }

    public decimal VatRate { get; private set; } = 15m;

    public decimal MinStockLevel { get; private set; }

    public decimal? StandardCost { get; private set; }

    public string? Notes { get; private set; }

    public bool IsActive { get; private set; } = true;

    public bool IsDeleted { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public void Update(string? barcode, string nameAr, string nameEn, Guid categoryId, Guid unitId, decimal sellingPrice, decimal vatRate, decimal minStockLevel, decimal? standardCost, string? notes, bool isActive)
    {
        Barcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim();
        NameAr = nameAr.Trim();
        NameEn = string.IsNullOrWhiteSpace(nameEn) ? NameAr : nameEn.Trim();
        CategoryId = categoryId;
        UnitId = unitId;
        SellingPrice = sellingPrice;
        VatRate = vatRate;
        MinStockLevel = minStockLevel;
        StandardCost = standardCost;
        Notes = notes;
        IsActive = isActive;
    }

    public void Delete()
    {
        IsDeleted = true;
        IsActive = false;
    }
}

internal sealed class Warehouse : TenantEntity
{
    public const string DefaultCode = "WH-MAIN";

    private Warehouse()
    {
    }

    public Warehouse(string code) => Code = code.Trim().ToUpperInvariant();

    public string Code { get; private set; } = string.Empty;

    public string NameAr { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    public string? Location { get; private set; }

    public Guid? BranchId { get; private set; }

    public string? ManagerName { get; private set; }

    public string? Phone { get; private set; }

    public bool IsDefault { get; private set; }

    public bool IsActive { get; private set; } = true;

    public void Update(string nameAr, string nameEn, string? location, Guid? branchId, string? managerName, string? phone, bool isActive)
    {
        NameAr = nameAr.Trim();
        NameEn = string.IsNullOrWhiteSpace(nameEn) ? NameAr : nameEn.Trim();
        Location = location;
        BranchId = branchId;
        ManagerName = managerName;
        Phone = phone;
        IsActive = isActive;
    }

    public void SetDefault(bool isDefault) => IsDefault = isDefault;
}

/// <summary>Current quantity and moving-average cost of one product in one warehouse.</summary>
internal sealed class StockBalance : TenantEntity
{
    private StockBalance()
    {
    }

    public StockBalance(Guid productId, Guid warehouseId)
    {
        ProductId = productId;
        WarehouseId = warehouseId;
    }

    public Guid ProductId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public decimal QuantityOnHand { get; private set; }

    public decimal AverageCost { get; private set; }

    public decimal LastPurchaseCost { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    /// <summary>Weighted moving average: (qty × avg + in × cost) / (qty + in); a non-positive stock restarts at the incoming cost.</summary>
    public void Receive(decimal quantity, decimal unitCost, bool isPurchase)
    {
        AverageCost = QuantityOnHand <= 0
            ? unitCost
            : Math.Round(((QuantityOnHand * AverageCost) + (quantity * unitCost)) / (QuantityOnHand + quantity), 4, MidpointRounding.AwayFromZero);
        QuantityOnHand += quantity;
        if (isPurchase)
        {
            LastPurchaseCost = unitCost;
        }
    }

    public void Issue(decimal quantity) => QuantityOnHand -= quantity;

    /// <summary>Takes a receipt back out of the average (reversal of a cancelled purchase).</summary>
    public void UndoReceipt(decimal quantity, decimal unitCost)
    {
        var remaining = QuantityOnHand - quantity;
        if (remaining > 0)
        {
            AverageCost = Math.Round(((QuantityOnHand * AverageCost) - (quantity * unitCost)) / remaining, 4, MidpointRounding.AwayFromZero);
        }

        QuantityOnHand = remaining;
    }

    public void Reset(decimal quantity, decimal averageCost, decimal lastPurchaseCost)
    {
        QuantityOnHand = quantity;
        AverageCost = averageCost;
        LastPurchaseCost = lastPurchaseCost;
    }
}

/// <summary>inventory.StockMovements: the stock ledger. Quantity is always positive; the type gives the direction.</summary>
internal sealed class StockMovement : TenantEntity
{
    private StockMovement()
    {
    }

    public StockMovement(Guid productId, Guid warehouseId, StockMovementType type, StockDocument document, decimal quantity, decimal unitCost, decimal? unitPrice, string? notes)
    {
        ProductId = productId;
        WarehouseId = warehouseId;
        Type = type;
        Date = document.Date;
        SourceModule = document.Module;
        SourceDocumentType = document.DocumentType;
        SourceDocumentId = document.DocumentId;
        SourceNumber = document.DocumentNumber;
        Quantity = quantity;
        UnitCost = unitCost;
        UnitPrice = unitPrice;
        Notes = notes;
    }

    public Guid ProductId { get; private set; }

    public Guid WarehouseId { get; private set; }

    public StockMovementType Type { get; private set; }

    public DateOnly Date { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal UnitCost { get; private set; }

    public decimal? UnitPrice { get; private set; }

    public decimal BalanceAfter { get; private set; }

    public decimal AverageCostAfter { get; private set; }

    public string SourceModule { get; private set; } = string.Empty;

    public string SourceDocumentType { get; private set; } = string.Empty;

    public Guid SourceDocumentId { get; private set; }

    public string SourceNumber { get; private set; } = string.Empty;

    public string? Notes { get; private set; }

    /// <summary>Set on both the original and the compensating movement when a document is reversed.</summary>
    public bool IsReversed { get; private set; }

    public Guid? ReversalOfId { get; private set; }

    public bool IsInbound => Type is StockMovementType.Opening or StockMovementType.InPurchase or StockMovementType.InReturn or StockMovementType.AdjustmentIn or StockMovementType.TransferIn;

    public void RecordResult(decimal balanceAfter, decimal averageCostAfter)
    {
        BalanceAfter = balanceAfter;
        AverageCostAfter = averageCostAfter;
    }

    public void MarkReversed() => IsReversed = true;

    public void MarkAsReversalOf(StockMovement original)
    {
        ReversalOfId = original.Id;
        IsReversed = true;
    }

    public static StockMovementType Opposite(StockMovementType type) => type switch
    {
        StockMovementType.Opening or StockMovementType.InPurchase or StockMovementType.InReturn or StockMovementType.AdjustmentIn => StockMovementType.AdjustmentOut,
        StockMovementType.TransferIn => StockMovementType.TransferOut,
        StockMovementType.TransferOut => StockMovementType.TransferIn,
        _ => StockMovementType.AdjustmentIn,
    };
}

internal enum CostingMethod
{
    MovingAverage,
    Fifo,
    LastPurchase,
    Standard,
}

internal enum NegativeInventoryPolicy
{
    Prohibit,
    AllowWithLastCost,
}

/// <summary>inventory.CostingPolicies (Id = TenantId): the frontend's CostingPolicyConfig.</summary>
internal sealed class CostingPolicy : TenantEntity
{
    private CostingPolicy()
    {
    }

    public CostingPolicy(Guid tenantId) => Id = tenantId;

    public CostingMethod Method { get; private set; } = CostingMethod.MovingAverage;

    public bool RecalculateOnNewPurchase { get; private set; } = true;

    public bool IncludeFreightAndCustoms { get; private set; } = true;

    public NegativeInventoryPolicy NegativeInventoryPolicy { get; private set; } = NegativeInventoryPolicy.Prohibit;

    public Guid? PurchasePriceVarianceAccountId { get; private set; }

    public DateTimeOffset LastUpdated { get; private set; }

    public string? Notes { get; private set; }

    public void Update(CostingMethod method, bool recalculateOnNewPurchase, bool includeFreightAndCustoms, NegativeInventoryPolicy negativePolicy, Guid? ppvAccountId, string? notes, DateTimeOffset at)
    {
        Method = method;
        RecalculateOnNewPurchase = recalculateOnNewPurchase;
        IncludeFreightAndCustoms = includeFreightAndCustoms;
        NegativeInventoryPolicy = negativePolicy;
        PurchasePriceVarianceAccountId = ppvAccountId;
        Notes = notes;
        LastUpdated = at;
    }
}
