namespace ERP.Core.Models.Accounting;

/// <summary>تقسيم الدفع على الفاتورة (PaymentSplit في الواجهة: method, amount, reference).</summary>
public class InvoicePaymentSplit : BaseEntity
{
    public Guid InvoiceId { get; set; }
    public PaymentMethod Method { get; set; }
    /// <summary>رمز طريقة الدفع المؤسَّسة في النظام (جدول طرق الدفع): يُرحَّل على حسابها المرتبط. فارغ = أول طريقة فعّالة من النوع.</summary>
    public string? PaymentMethodCode { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }

    public virtual Invoice Invoice { get; set; } = null!;
}
