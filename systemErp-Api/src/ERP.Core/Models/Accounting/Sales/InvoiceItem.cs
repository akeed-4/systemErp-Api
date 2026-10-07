
namespace ERP.Core.Models.Accounting;

public class InvoiceItem : BaseEntity
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
    public decimal VatRate { get; set; } = 15m;
    public decimal VatAmount { get; set; }
    /// <summary>مبلغ ضريبة ثابت للسطر (نظام هامش الربح للسيارات المستعملة). فارغ = تُحسب من النسبة.</summary>
    public decimal? VatAmountOverride { get; set; }
    /// <summary>التصنيف الضريبي للسطر في الإقرار (أساسي / صفري / معفى / خارج النطاق).</summary>
    public VatCategory VatCategory { get; set; } = VatCategory.Standard;
    /// <summary>سبب عدم الخضوع للنسبة الأساسية برمز هيئة الزكاة (VATEX-SA-…) للسطر الصفري/المعفى/خارج النطاق.</summary>
    public string? VatExemptionReasonCode { get; set; }
    public decimal TotalBeforeVat { get; set; }
    public decimal TotalAfterVat { get; set; }
    public Guid? CostCenterId { get; set; }
    /// <summary>حساب إيراد هذا السطر (مبيعات)؛ فارغ = حساب إيراد الفاتورة ثم الافتراضي.</summary>
    public string? RevenueAccountCode { get; set; }

    public virtual Invoice Invoice { get; set; } = null!;
}
