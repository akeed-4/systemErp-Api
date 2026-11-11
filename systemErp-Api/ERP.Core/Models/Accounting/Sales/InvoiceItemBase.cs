
namespace ERP.Core.Models.Accounting;

/// <summary>بند فاتورة: الحقول المشتركة بين جداول بنود المستندات المستقلة (TPC). <see cref="InvoiceId"/> يشير إلى رأس جدوله.</summary>
public abstract class InvoiceItemBase : BaseEntity
{
    public Guid InvoiceId { get; set; }
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
    /// <summary>مبلغ ضريبة ثابت للسطر (نظام هامش الربح للسيارات المستعملة). فارغ = تُحسب من النسبة.</summary>
    public decimal? VatAmountOverride { get; set; }
    /// <summary>التصنيف الضريبي للسطر في الإقرار (أساسي / صفري / معفى / خارج النطاق).</summary>
    public VatCategory VatCategory { get; set; } = VatCategory.Standard;
    /// <summary>سبب عدم الخضوع للنسبة الأساسية برمز هيئة الزكاة (VATEX-SA-…) للسطر الصفري/المعفى/خارج النطاق.</summary>
    public string? VatExemptionReasonCode { get; set; }
    /// <summary>
    /// سبب تجاوز التصنيف الضريبي للصنف في هذا السطر (بصلاحية invoice-tax-treatment-override). فارغ = التصنيف والنسبة لقطة من
    /// تعريف الصنف وقت الإصدار؛ والقيمتان الأصلية والجديدة مسجلتان في سجل التدقيق.
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
    /// <summary>المعدة المستأجرة المرتبطة بسطر فاتورة المؤجر؛ فارغ لغير مصروفات الإيجار.</summary>
    public Guid? ProjectResourceId { get; set; }
    /// <summary>سطر الفاتورة الأصلية الذي أُرجع هذا السطر عنه؛ يميّز البنود المتكررة في المرتجعات.</summary>
    public Guid? OriginalInvoiceItemId { get; set; }
    /// <summary>حساب إيراد هذا السطر (مبيعات)؛ فارغ = حساب إيراد الفاتورة ثم الافتراضي.</summary>
    public string? RevenueAccountCode { get; set; }
    /// <summary>
    /// لقطة طبيعة الصنف وقت الحفظ (<see cref="ProductItemTypes"/>): تحدد أثر السطر على المخزون عند الترحيل وعكسه، فلا يغيّره
    /// تعديل الصنف لاحقاً. الفارغ (أسطر قديمة) = بضاعة للصنف، وبند خدمة لسطر بلا صنف.
    /// </summary>
    public string? ItemType { get; set; }
    /// <summary>
    /// لقطة مكونات الصنف المجمّع/المركّب (JSON من <see cref="InvoiceLineComponent"/>): كمياتها للسطر كله، وتكلفتها بعد الترحيل.
    /// منها يُعكس السطر ويُرجع، وتُطبع مكونات المركّب.
    /// </summary>
    public string? ComponentsJson { get; set; }
}
