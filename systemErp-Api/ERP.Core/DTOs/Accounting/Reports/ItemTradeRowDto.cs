namespace ERP.Core.DTOs.Accounting;

/// <summary>
/// الحركة التجارية للصنف في الفترة (مبيعات أو مشتريات): الكمية والقيمة قبل الضريبة (بعد خصم البند، قبل خصم الفاتورة)، والمرتجع،
/// والصافي ومتوسط سعره، وفي المبيعات تكلفة المباع ومجمل ربحه. من الفواتير المرحّلة وحدها.
/// </summary>
public class ItemTradeRowDto
{
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public int DocumentsCount { get; set; }
    public int PartiesCount { get; set; }
    public decimal Quantity { get; set; }
    public decimal Value { get; set; }
    public decimal ReturnQuantity { get; set; }
    public decimal ReturnValue { get; set; }
    public decimal NetQuantity { get; set; }
    public decimal NetValue { get; set; }
    public decimal AveragePrice { get; set; }
    public decimal VatTotal { get; set; }
    /// <summary>المبيعات: تكلفة المباع الصافية (المرتجع يخصمها)؛ المشتريات: صفر.</summary>
    public decimal Cost { get; set; }
    public decimal Profit { get; set; }
    public decimal ProfitPercent { get; set; }
    public DateTime? LastDate { get; set; }
}
