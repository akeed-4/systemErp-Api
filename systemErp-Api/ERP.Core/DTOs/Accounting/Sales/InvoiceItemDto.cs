namespace ERP.Core.DTOs.Accounting;

public partial class InvoiceItemDto
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal UnitCost { get; set; }
    public decimal Discount { get; set; }
    public decimal VatRate { get; set; } = 15m;
    public decimal VatAmount { get; set; }
    public decimal? VatAmountOverride { get; set; }
    /// <summary>التصنيف الضريبي؛ فارغ = أساسي للسطر الخاضع، وصفري للسطر بنسبة 0.</summary>
    public VatCategory? VatCategory { get; set; }
    /// <summary>سبب الإعفاء/الصفرية برمز الهيئة (VATEX-SA-…)؛ إلزامي في فاتورة المبيعات عند تحديد تصنيف غير أساسي.</summary>
    public string? VatExemptionReasonCode { get; set; }
    /// <summary>
    /// فاتورة المبيعات: سطر الصنف يأخذ تصنيفه ونسبته من تعريف الصنف ويُتجاهل ما يُرسل منهما، إلا إذا أُرسل هذا السبب
    /// (تجاوز بصلاحية invoice-tax-treatment-override يُسجَّل في سجل التدقيق).
    /// </summary>
    public string? VatOverrideReason { get; set; }
    public decimal TotalBeforeVat { get; set; }
    public decimal TotalAfterVat { get; set; }
    public Guid? CostCenterId { get; set; }
    public string? RevenueAccountCode { get; set; }
}
