namespace ERP.Core.DTOs.Accounting;

/// <summary>
/// إجماليات قائمة الفواتير على كامل النتيجة المرشّحة (لا الصفحة)، محسوبة في SQL بنفس مرشّحات القائمة.
/// <list type="bullet">
/// <item>Count = عدد الفواتير المطابقة.</item>
/// <item>Subtotal = Σ Subtotal (الصافي قبل الضريبة وبعد الخصم كما يخزّنه محرك الفوترة).</item>
/// <item>DiscountTotal / VatTotal / GrandTotal = Σ الحقل المقابل (GrandTotal شامل الضريبة).</item>
/// <item>TotalCost = Σ TotalCost (تكلفة البضاعة)، GrossProfit = Σ GrossProfit.</item>
/// <item>Cash* = الفواتير ذات طريقة سداد غير آجلة (نقد/شبكة/تحويل)، Credit* = الآجلة. CashCount + CreditCount = Count.</item>
/// </list>
/// لا تُدمج أنواع المستندات: ما تجمعه هو ما يطابق المرشّحات فقط (مثلًا kind=sales لا يشمل المرتجعات).
/// </summary>
public class InvoiceSummaryDto
{
    public int Count { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal TotalCost { get; set; }
    public decimal GrossProfit { get; set; }
    public int CashCount { get; set; }
    public decimal CashGrandTotal { get; set; }
    public int CreditCount { get; set; }
    public decimal CreditGrandTotal { get; set; }
}
