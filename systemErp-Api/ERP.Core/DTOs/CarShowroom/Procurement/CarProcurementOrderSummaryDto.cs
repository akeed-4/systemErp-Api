namespace ERP.Core.DTOs.CarShowroom;

/// <summary>
/// إجماليات أوامر شراء السيارات على كامل النتيجة المرشّحة (نفس مرشّحات القائمة)، محسوبة في SQL.
/// <list type="bullet">
/// <item>Count = عدد الأوامر المطابقة.</item>
/// <item>Subtotal / VatTotal / GrandTotal = Σ الحقل المقابل للأمر (GrandTotal شامل الضريبة).</item>
/// <item>Cash* = الأوامر ذات PaymentType = "cash"، Credit* = ما عداها (credit / bank_lc / advance_milestone)،
/// وهي نفس قاعدة فوترة الأمر التي تقيّد غير النقدي على حساب المورد. CashCount + CreditCount = Count.</item>
/// </list>
/// </summary>
public class CarProcurementOrderSummaryDto
{
    public int Count { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public int CashCount { get; set; }
    public decimal CashGrandTotal { get; set; }
    public int CreditCount { get; set; }
    public decimal CreditGrandTotal { get; set; }
}
