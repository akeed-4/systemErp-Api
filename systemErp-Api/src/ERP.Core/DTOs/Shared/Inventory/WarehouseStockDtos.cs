namespace ERP.Core.DTOs.Shared;

/// <summary>رصيد صنف في مستودع وقيمته بالتكلفة الموحّدة للصنف.</summary>
public class WarehouseStockDto
{
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal AverageCost { get; set; }
    public decimal Value { get; set; }
}

/// <summary>نقل كمية صنف بين مستودعين (يُستدعى من مستند التحويل).</summary>
public class TransferStockDto
{
    public Guid ItemId { get; set; }
    public Guid FromWarehouseId { get; set; }
    public Guid ToWarehouseId { get; set; }
    public decimal Quantity { get; set; }
    public DateTime Date { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public Guid? SourceId { get; set; }
    public string? Notes { get; set; }
}

public class CreateStockTransferDto
{
    public DateTime? Date { get; set; }
    public Guid FromWarehouseId { get; set; }
    public Guid ToWarehouseId { get; set; }
    public string? Notes { get; set; }
    public List<StockTransferItemDto> Items { get; set; } = new();
}

public class StockTransferItemDto
{
    public Guid ItemId { get; set; }
    public string? Sku { get; set; }
    public string? ItemName { get; set; }
    public string? Unit { get; set; }
    public decimal Quantity { get; set; }
}

public class StockTransferDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string TransferNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public Guid FromWarehouseId { get; set; }
    public string FromWarehouseName { get; set; } = string.Empty;
    public Guid ToWarehouseId { get; set; }
    public string ToWarehouseName { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public List<StockTransferItemDto> Items { get; set; } = new();
}
