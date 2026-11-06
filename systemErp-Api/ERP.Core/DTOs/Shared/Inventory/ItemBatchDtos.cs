namespace ERP.Core.DTOs.Shared;

/// <summary>تشغيلة صنف برصيدها (في مستودع محدد أو في كل المستودعات) وحالة صلاحيتها.</summary>
public class ItemBatchDto
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ProductionDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>الأيام المتبقية حتى الانتهاء (سالبة = منتهية)؛ فارغة = بلا صلاحية.</summary>
    public int? DaysToExpiry { get; set; }
    /// <summary>valid | near_expiry | expired | no_expiry</summary>
    public string ExpiryStatus { get; set; } = "no_expiry";
    public string? Notes { get; set; }
}

/// <summary>سطر تقرير الصلاحية: رصيد تشغيلة في مستودع.</summary>
public class BatchExpiryRowDto
{
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public Guid ItemBatchId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ProductionDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int? DaysToExpiry { get; set; }
    public string ExpiryStatus { get; set; } = "no_expiry";
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    /// <summary>القيمة بتكلفة الصنف (التكلفة على مستوى الصنف لا التشغيلة).</summary>
    public decimal Value { get; set; }
}

/// <summary>تعديل بيانات تشغيلة (تاريخا الإنتاج والانتهاء والملاحظات)؛ الرقم والصنف ثابتان.</summary>
public class UpdateItemBatchDto
{
    public DateTime? ProductionDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? Notes { get; set; }
}
