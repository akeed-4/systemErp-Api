using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public class VatReturnDto
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    // المبالغ بالعملة الأساسية، صافية من المرتجعات، ومصنّفة بحسب التصنيف الضريبي لكل سطر
    public decimal StandardRatedSales { get; set; }
    /// <summary>مبيعات محلية خاضعة لنسبة الصفر (دون الصادرات).</summary>
    public decimal ZeroRatedSales { get; set; }
    /// <summary>الصادرات (سطور صفرية بسبب تصدير سلع أو خدمات).</summary>
    public decimal ExportSales { get; set; }
    public decimal ExemptSales { get; set; }
    public decimal OutOfScopeSales { get; set; }
    /// <summary>ضريبة المخرجات شاملة تسويات القيود اليدوية.</summary>
    public decimal OutputVat { get; set; }
    /// <summary>تسويات ضريبة المخرجات بقيود يدوية في الفترة (موجبة = زيادة المستحق)، دون قيود السداد للهيئة.</summary>
    public decimal OutputVatAdjustments { get; set; }
    public decimal StandardRatedPurchases { get; set; }
    public decimal ZeroRatedPurchases { get; set; }
    public decimal ExemptPurchases { get; set; }
    public decimal OutOfScopePurchases { get; set; }
    /// <summary>ضريبة المدخلات شاملة تسويات القيود اليدوية.</summary>
    public decimal InputVat { get; set; }
    /// <summary>تسويات ضريبة المدخلات بقيود يدوية في الفترة (موجبة = زيادة القابل للخصم).</summary>
    public decimal InputVatAdjustments { get; set; }
    public decimal NetVatPayable { get; set; }
}
