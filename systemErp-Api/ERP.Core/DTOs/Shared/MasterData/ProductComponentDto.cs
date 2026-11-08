namespace ERP.Core.DTOs.Shared;

/// <summary>سطر مكونات صنف: صنف بكميته لكل وحدة، أو مصروف غير مباشر بمبلغه لكل وحدة (للمنتج الصناعي).</summary>
public class ProductComponentDto
{
    public Guid Id { get; set; }
    /// <summary>item | overhead.</summary>
    public string Kind { get; set; } = "item";
    public Guid? ComponentProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal WastePercent { get; set; }
    public string? Name { get; set; }
    public decimal UnitCost { get; set; }
    public string? AccountCode { get; set; }
    public int SortOrder { get; set; }

    // ---- للقراءة: بيانات الصنف المكوّن الحالية ----
    public string? ComponentSku { get; set; }
    public string? ComponentName { get; set; }
    public string? ComponentUnit { get; set; }
    public string? ComponentItemType { get; set; }
    public decimal ComponentStock { get; set; }
    /// <summary>تكلفة الوحدة الحالية للمكوّن (للصنف) أو المبلغ (للمصروف).</summary>
    public decimal ComponentUnitCost { get; set; }
    /// <summary>تكلفة السطر لكل وحدة واحدة من المالك = الكمية × (1 + الهدر) × التكلفة، أو المبلغ.</summary>
    public decimal LineCost { get; set; }
}
