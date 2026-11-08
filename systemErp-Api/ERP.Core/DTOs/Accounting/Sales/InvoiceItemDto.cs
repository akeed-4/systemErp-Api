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
    public decimal ExciseTaxRate { get; set; }
    public decimal ExciseRetailPrice { get; set; }
    public decimal ExciseStandardPrice { get; set; }
    public decimal ExciseTaxBaseAmount { get; set; }
    public decimal ExciseTaxAmount { get; set; }
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
    /// <summary>
    /// التشغيلة: في الوارد (شراء، مرتجع بلا أصل) رقمها وتاريخاها تُنشأ أو تُطابَق؛ في الصادر (بيع، مرتجع شراء) تشغيلة مختارة يدوياً،
    /// وفارغة = تخصيص تلقائي بالأقرب انتهاءً (FEFO). بعد الترحيل تحمل ما خُصِّص فعلاً (التشغيلة، أو ملخص عند التوزيع على أكثر من تشغيلة).
    /// </summary>
    public Guid? ItemBatchId { get; set; }
    public string? BatchNumber { get; set; }
    public DateTime? ProductionDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public Guid? CostCenterId { get; set; }
    public string? RevenueAccountCode { get; set; }
    /// <summary>للقراءة: طبيعة الصنف المحفوظة على السطر (stock | service | kit | composite | manufactured).</summary>
    public string? ItemType { get; set; }
    /// <summary>للقراءة: مكونات الصنف المجمّع/المركّب بكمياتها للسطر (تُطبع للمركّب بلا سعر).</summary>
    public List<InvoiceLineComponent>? Components { get; set; }
}
