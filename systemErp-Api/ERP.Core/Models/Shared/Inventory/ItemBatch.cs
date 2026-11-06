namespace ERP.Core.Models.Shared;

/// <summary>
/// تشغيلة صنف (Lot/Batch): رقم فريد للصنف بتاريخ إنتاجه وانتهائه. تُنشأ عند أول وارد برقمها وتُعاد للوارد اللاحق بالرقم نفسه.
/// التكلفة تبقى على مستوى الصنف (لا تكلفة للتشغيلة)؛ التشغيلة تتبّع الكمية والصلاحية فقط.
/// </summary>
public class ItemBatch : BaseEntity
{
    public Guid ItemId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ProductionDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? Notes { get; set; }
}

/// <summary>رصيد تشغيلة في مستودع. مجموع تشغيلات الصنف في مستودع = رصيده فيه (WarehouseStock) للصنف المتتبَّع.</summary>
public class BatchStock : BaseEntity
{
    public Guid ItemId { get; set; }
    public Guid ItemBatchId { get; set; }
    public Guid WarehouseId { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>رمز تزامن: صرفان متزامنان من التشغيلة نفسها لا يتجاوزان رصيدها.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// توزيع حركة مخزون على التشغيلات: الحركة سطر واحد بتكلفتها، وهذه أجزاؤها بكمية كل تشغيلة (موجبة للوارد، سالبة للصادر).
/// المرتجع والعكس يقرآنها ليعودا للتشغيلات نفسها؛ وتُعلَّم معكوسة عند عكس مستندها.
/// </summary>
public class StockMovementBatch : BaseEntity
{
    public Guid StockMovementId { get; set; }
    public Guid ItemId { get; set; }
    public Guid ItemBatchId { get; set; }
    public Guid WarehouseId { get; set; }
    /// <summary>بإشارتها: + وارد، − صادر.</summary>
    public decimal Quantity { get; set; }
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }
    /// <summary>سطر المستند (بند الفاتورة/التحويل/الجرد) الذي خُصِّصت له.</summary>
    public Guid? SourceLineId { get; set; }
    /// <summary>عُكس مستندها (تعديل/حذف فاتورة مرحّلة): لا تُحسب في تخصيصات المستند الحالية.</summary>
    public bool IsReversed { get; set; }
}
