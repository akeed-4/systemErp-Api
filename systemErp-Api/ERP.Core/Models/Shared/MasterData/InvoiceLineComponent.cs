namespace ERP.Core.Models.Shared;

/// <summary>مكوّن سطر فاتورة لصنف مجمّع/مركّب: الصنف بكميته للسطر كله، وتكلفة وحدته الفعلية بعد الترحيل.</summary>
public class InvoiceLineComponent
{
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    /// <summary>الكمية بوحدة المكوّن لكل وحدة واحدة من الصنف المالك.</summary>
    public decimal QuantityPerUnit { get; set; }
    /// <summary>الكمية للسطر كله = كمية السطر × الكمية لكل وحدة.</summary>
    public decimal Quantity { get; set; }
    /// <summary>تكلفة وحدة المكوّن عند خروجه (بعد الترحيل).</summary>
    public decimal UnitCost { get; set; }
}
