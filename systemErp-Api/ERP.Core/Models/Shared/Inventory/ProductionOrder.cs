namespace ERP.Core.Models.Shared;

/// <summary>
/// مستند تحويل وتصنيع منتج (يومي): يصرف المواد الأولية من مستودعها بتكلفتها الفعلية، ويحمّل الخدمات (شغل اليد) والمصاريف غير المباشرة
/// (إهلاك الآلة، رواتب وأجور) على المنتج، ويدخل المنتج التام لمستودعه بمجموع التكلفة. القيد: مدين مخزون المنتج / دائن مخزون المواد
/// وحسابات التكاليف المحمّلة. الأسطر لقطة من مكونات المنتج قابلة للتعديل في المسودة (الكمية الفعلية والمبالغ).
/// </summary>
public class ProductionOrder : BaseEntity
{
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    /// <summary>المنتج الصناعي المُنتَج.</summary>
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductSku { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    /// <summary>الكمية المنتجة بوحدة المنتج.</summary>
    public decimal Quantity { get; set; }
    /// <summary>مستودع صرف المواد (فارغ = مستودع الجلسة/الافتراضي).</summary>
    public Guid? MaterialsWarehouseId { get; set; }
    /// <summary>مستودع استلام المنتج التام (فارغ = مستودع المواد).</summary>
    public Guid? WarehouseId { get; set; }
    /// <summary><see cref="ProductionOrderStatuses"/>.</summary>
    public string Status { get; set; } = ProductionOrderStatuses.Draft;
    public decimal MaterialsCost { get; set; }
    public decimal ServicesCost { get; set; }
    public decimal OverheadCost { get; set; }
    public decimal TotalCost { get; set; }
    /// <summary>تكلفة وحدة المنتج = الإجمالي ÷ الكمية.</summary>
    public decimal UnitCost { get; set; }
    public Guid? CostCenterId { get; set; }
    public Guid? JournalEntryId { get; set; }
    /// <summary>تشغيلة المنتج التام (للمنتج المتتبَّع).</summary>
    public string? BatchNumber { get; set; }
    public DateTime? ProductionDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? Notes { get; set; }
    public DateTime? PostedAt { get; set; }
    public string? CancelReason { get; set; }

    public virtual ICollection<ProductionOrderLine> Lines { get; set; } = new List<ProductionOrderLine>();
}

/// <summary>سطر مستند التصنيع: مادة تُصرف، أو خدمة/مصروف غير مباشر يُحمَّل بمبلغه.</summary>
public class ProductionOrderLine : BaseEntity
{
    public Guid ProductionOrderId { get; set; }
    /// <summary><see cref="ProductionLineKinds"/>.</summary>
    public string Kind { get; set; } = ProductionLineKinds.Material;
    /// <summary>الصنف (المادة أو الخدمة)؛ فارغ للمصروف غير المباشر.</summary>
    public Guid? ComponentProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string? Unit { get; set; }
    /// <summary>الكمية المعيارية من مكونات المنتج = كمية المكوّن × (1 + الهدر) × الكمية المنتجة (للمقارنة).</summary>
    public decimal StandardQuantity { get; set; }
    /// <summary>الكمية الفعلية (المادة تُصرف بها، والخدمة تُحمَّل بها، والمصروف بعدد الوحدات المنتجة).</summary>
    public decimal Quantity { get; set; }
    /// <summary>تكلفة الوحدة: للمادة تكلفة خروجها الفعلية بعد الترحيل؛ للخدمة والمصروف المبلغ المحمَّل لكل وحدة.</summary>
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    /// <summary>الحساب الدائن للخدمة والمصروف المحمَّل (الفارغ = تكاليف التصنيع المحمّلة).</summary>
    public string? AccountCode { get; set; }
    public int SortOrder { get; set; }

    public virtual ProductionOrder Order { get; set; } = null!;
}

public static class ProductionOrderStatuses
{
    public const string Draft = "draft";
    public const string Posted = "posted";
    public const string Cancelled = "cancelled";
}

public static class ProductionLineKinds
{
    /// <summary>مادة أولية (صنف له رصيد) تُصرف من المخزون.</summary>
    public const string Material = "material";
    /// <summary>خدمة (صنف خدمي) مثل شغل اليد: تكلفة تُحمَّل بلا حركة مخزنية.</summary>
    public const string Service = "service";
    /// <summary>مصروف غير مباشر بلا صنف: إهلاك الآلة، رواتب وأجور...</summary>
    public const string Overhead = "overhead";

    public static bool IsValid(string? kind) => kind is Material or Service or Overhead;
}
