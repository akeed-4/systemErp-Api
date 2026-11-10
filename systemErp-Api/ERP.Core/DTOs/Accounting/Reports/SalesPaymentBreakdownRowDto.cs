namespace ERP.Core.DTOs.Accounting;

/// <summary>
/// صف تقرير توزيع مدفوعات المبيعات (فاتورة بيع واحدة)، محسوب كاملًا في SQL فيُرشَّح ويُفرز ويُلخَّص على كامل النتيجة.
/// قواعد المدفوع (نفس منطق التقرير السابق في الواجهة):
/// <list type="number">
/// <item>إن وُجدت أسطر سداد مجزأ: المدفوع بكل طريقة = Σ مبالغ الأسطر بتلك الطريقة.</item>
/// <item>وإلا: إن كانت طريقة الفاتورة غير آجلة وحالتها ليست مسودة/ملغاة فالمدفوع = GrandTotal بطريقتها؛ وإلا لا مدفوع.</item>
/// </list>
/// Paid = PaidCash + PaidBankCard + PaidBankTransfer + PaidCredit. Balance = max(0, Grand − Paid).
/// PaymentStatus = paid إذا Balance ≤ 0، partial إذا Paid > 0، وإلا unpaid.
/// بيانات العميل (الهاتف/البريد/العنوان/الرقم الضريبي) من الفاتورة، وإن خلت فمن بطاقة العميل المرتبط (PartyId).
/// </summary>
public class SalesPaymentBreakdownRowDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Time { get; set; } = string.Empty;
    public string InvoiceStatus { get; set; } = string.Empty;
    public string Customer { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? VatNumber { get; set; }
    public decimal ItemQuantity { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Discounts { get; set; }
    public decimal Tax { get; set; }
    public decimal Grand { get; set; }
    public decimal PaidCash { get; set; }
    public decimal PaidBankCard { get; set; }
    public decimal PaidBankTransfer { get; set; }
    public decimal PaidCredit { get; set; }
    public decimal Paid { get; set; }
    public decimal Balance { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
}
