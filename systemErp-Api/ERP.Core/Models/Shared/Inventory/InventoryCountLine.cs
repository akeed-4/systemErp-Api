namespace ERP.Core.Models.Shared;

/// <summary>سطر جرد: صنف (ItemId) أو مركبة (ChassisNumber). رصيد النظام والتكلفة والفرق يحسبها السرفر.</summary>
public class InventoryCountLine : BaseEntity
{
    public Guid InventoryCountId { get; set; }

    public Guid? ItemId { get; set; }
    public string? Sku { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? Unit { get; set; }

    /// <summary>تشغيلة السطر للصنف المتتبَّع: سطر لكل تشغيلة برصيدها؛ والتشغيلة الجديدة المعثور عليها برقمها وتاريخها.</summary>
    public Guid? ItemBatchId { get; set; }
    public string? BatchNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }

    public Guid? VehicleId { get; set; }
    public string? ChassisNumber { get; set; }

    public decimal SystemQuantity { get; set; }
    /// <summary>null = لم يُعدّ بعد؛ الإرسال للاعتماد يتطلب عدّ كل الأسطر.</summary>
    public decimal? CountedQuantity { get; set; }
    public decimal VarianceQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal VarianceValue { get; set; }
    public InventoryCountLineResolution Resolution { get; set; } = InventoryCountLineResolution.None;
    public string? Notes { get; set; }

    public virtual InventoryCount InventoryCount { get; set; } = null!;
}
