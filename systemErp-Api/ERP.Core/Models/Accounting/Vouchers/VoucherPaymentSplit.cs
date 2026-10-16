namespace ERP.Core.Models.Accounting;

public class VoucherPaymentSplit : BaseEntity
{
    public Guid VoucherId { get; set; }
    public PaymentMethod Method { get; set; }
    /// <summary>رمز طريقة الدفع المؤسَّسة في النظام (جدول طرق الدفع): يُرحَّل على حسابها المرتبط. فارغ = أول طريقة فعّالة من النوع.</summary>
    public string? PaymentMethodCode { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }

    public virtual Voucher Voucher { get; set; } = null!;
}
