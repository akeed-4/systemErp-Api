namespace ERP.Core.DTOs.Accounting;

public partial class VoucherPaymentSplitDto
{
    public Guid Id { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
}
