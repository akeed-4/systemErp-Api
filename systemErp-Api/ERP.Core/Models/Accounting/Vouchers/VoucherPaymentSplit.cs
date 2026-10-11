namespace ERP.Core.Models.Accounting;

public class VoucherPaymentSplit : BaseEntity
{
    public Guid VoucherId { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }

    public virtual Voucher Voucher { get; set; } = null!;
}
