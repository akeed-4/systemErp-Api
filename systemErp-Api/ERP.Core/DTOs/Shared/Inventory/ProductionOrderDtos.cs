namespace ERP.Core.DTOs.Shared;

public class ProductionOrderDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductSku { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public Guid? MaterialsWarehouseId { get; set; }
    public Guid? WarehouseId { get; set; }
    public string Status { get; set; } = "draft";
    public decimal MaterialsCost { get; set; }
    public decimal ServicesCost { get; set; }
    public decimal OverheadCost { get; set; }
    public decimal TotalCost { get; set; }
    public decimal UnitCost { get; set; }
    public Guid? CostCenterId { get; set; }
    public Guid? JournalEntryId { get; set; }
    public string? BatchNumber { get; set; }
    public DateTime? ProductionDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? Notes { get; set; }
    public DateTime? PostedAt { get; set; }
    public string? CancelReason { get; set; }
    public List<ProductionOrderLineDto> Lines { get; set; } = new();
}

public class ProductionOrderLineDto
{
    public Guid Id { get; set; }
    /// <summary>material | service | overhead.</summary>
    public string Kind { get; set; } = "material";
    public Guid? ComponentProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string? Unit { get; set; }
    public decimal StandardQuantity { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public string? AccountCode { get; set; }
    public int SortOrder { get; set; }
    /// <summary>للقراءة: رصيد المادة الحالي في مستودع الصرف (للمسودة).</summary>
    public decimal? Available { get; set; }
}

/// <summary>إنشاء/تعديل مسودة مستند تصنيع. الأسطر الغائبة (null) تُشتق من مكونات المنتج بالكمية المنتجة.</summary>
public class SaveProductionOrderDto
{
    public DateTime? Date { get; set; }
    public string? ManualNumber { get; set; }
    public Guid ProductId { get; set; }
    public decimal Quantity { get; set; }
    public Guid? MaterialsWarehouseId { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? CostCenterId { get; set; }
    public string? BatchNumber { get; set; }
    public DateTime? ProductionDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? Notes { get; set; }
    /// <summary>ترحيل مباشر بعد الحفظ.</summary>
    public bool Post { get; set; }
    public List<ProductionOrderLineDto>? Lines { get; set; }
}

public class CancelProductionOrderDto
{
    public string Reason { get; set; } = string.Empty;
}
