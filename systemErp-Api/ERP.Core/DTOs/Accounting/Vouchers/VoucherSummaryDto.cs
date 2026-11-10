namespace ERP.Core.DTOs.Accounting;

/// <summary>
/// إجماليات السندات على كامل النتيجة المرشّحة (نفس مرشّحات القائمة)، محسوبة في SQL.
/// <list type="bullet">
/// <item>Count = عدد السندات المطابقة = ReceiptCount + PaymentCount.</item>
/// <item>ReceiptTotal = Σ Amount لسندات القبض، PaymentTotal = Σ Amount لسندات الصرف.</item>
/// <item>NetCashFlow = ReceiptTotal − PaymentTotal (صافي التدفق النقدي للسندات).</item>
/// </list>
/// </summary>
public class VoucherSummaryDto
{
    public int Count { get; set; }
    public int ReceiptCount { get; set; }
    public decimal ReceiptTotal { get; set; }
    public int PaymentCount { get; set; }
    public decimal PaymentTotal { get; set; }
    public decimal NetCashFlow { get; set; }
}
