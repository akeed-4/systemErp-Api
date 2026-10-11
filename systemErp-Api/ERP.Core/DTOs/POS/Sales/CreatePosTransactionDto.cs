namespace ERP.Core.DTOs.POS;

public partial class CreatePosTransactionDto
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public Guid ShiftId { get; set; }
    public Guid? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public string? CustomerTaxNumber { get; set; }
    public string InvoiceType { get; set; } = "simplified";
    public decimal SubtotalBeforeVat { get; set; }
    public decimal CouponDiscount { get; set; }
    public string? CouponCode { get; set; }
    public decimal LoyaltyDiscount { get; set; }
    public int LoyaltyPointsRedeemed { get; set; }
    public decimal TotalDiscount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public PosPaymentMethod PaymentMethod { get; set; } = PosPaymentMethod.Cash;
    public decimal PaidCash { get; set; }
    public decimal PaidCard { get; set; }
    public decimal PaidMada { get; set; }
    public decimal PaidApplePay { get; set; }
    public decimal ChangeAmount { get; set; }
    public int PointsEarned { get; set; }
    public string QrCodeBase64 { get; set; } = string.Empty;
    public ZatcaSubmissionStatus ZatcaStatus { get; set; } = ZatcaSubmissionStatus.NotSubmitted;
    public string? ZatcaUuid { get; set; }
    public string? ZatcaResponseMsg { get; set; }
    public Guid? InvoiceId { get; set; }
    public PosTransactionStatus Status { get; set; } = PosTransactionStatus.Completed;
    public List<PosTransactionItemDto> Items { get; set; } = new();
}
