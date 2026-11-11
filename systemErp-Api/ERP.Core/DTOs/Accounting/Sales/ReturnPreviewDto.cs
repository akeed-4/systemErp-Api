namespace ERP.Core.DTOs.Accounting;

/// <summary>
/// معاينة مرتجع قبل إصداره (بلا أي أثر): الفاتورة الأصلية كاملة، والمتبقي القابل للإرجاع من كل صنف أو سيارة،
/// وتسعير المختار بالمحرك نفسه الذي يرحّل المرتجع (الخصم النسبي وخصم الفاتورة والضريبة ومعالجة الأصل).
/// </summary>
public class ReturnPreviewDto
{
    public InvoiceDto Original { get; set; } = new();
    /// <summary>أصناف الفاتورة الأصلية بكمياتها: الأصلية، المرتجعة من قبل، المتبقية، والمختارة الآن بتسعيرها.</summary>
    public List<ReturnPreviewLineDto> Lines { get; set; } = new();
    /// <summary>فاتورة سيارات: المركبات المرتجعة من قبل في مرتجع مرحّل (لا تُختار مرة ثانية).</summary>
    public List<Guid> ReturnedVehicleIds { get; set; } = new();
    /// <summary>إجماليات المرتجع للمختار؛ null إن لم يُختر شيء.</summary>
    public ReturnTotalsDto? Totals { get; set; }
}

public class ReturnPreviewLineDto
{
    public Guid? OriginalInvoiceItemId { get; set; }
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string? Unit { get; set; }
    public decimal OriginalQuantity { get; set; }
    public decimal ReturnedQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    /// <summary>خصم السطر في الفاتورة الأصلية (لكامل كميته).</summary>
    public decimal Discount { get; set; }
    public decimal VatRate { get; set; }
    /// <summary>الكمية المختارة للإرجاع (0 = غير مختار) وتسعيرها في المرتجع.</summary>
    public decimal Quantity { get; set; }
    public decimal Net { get; set; }
    public decimal VatAmount { get; set; }
    public decimal Total { get; set; }
}

public class ReturnTotalsDto
{
    /// <summary>الإجمالي قبل الخصم (الكمية × السعر).</summary>
    public decimal Gross { get; set; }
    public decimal ItemsDiscount { get; set; }
    /// <summary>حصة المرتجع من خصم الفاتورة الأصلية.</summary>
    public decimal InvoiceDiscount { get; set; }
    /// <summary>الصافي الخاضع للضريبة.</summary>
    public decimal NetTotal { get; set; }
    public decimal ExciseTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
}
