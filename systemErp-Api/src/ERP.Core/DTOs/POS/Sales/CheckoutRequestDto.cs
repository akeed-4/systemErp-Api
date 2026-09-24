namespace ERP.Core.DTOs.POS;

/// <summary>طلب إتمام بيع. الأسعار والضريبة والعروض تُحسب في الخادم من الكتالوج، لا من العميل.</summary>
public class CheckoutRequestDto
{
    public Guid? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerTaxNumber { get; set; }
    /// <summary>simplified | standard (فارغ = الافتراضي من الإعدادات)</summary>
    public string? InvoiceType { get; set; }
    public List<CheckoutLineDto> Items { get; set; } = new();

    public string? CouponCode { get; set; }
    public int LoyaltyPointsToRedeem { get; set; }

    public PosPaymentMethod PaymentMethod { get; set; } = PosPaymentMethod.Cash;
    /// <summary>المبالغ المدفوعة (النقد قد يزيد عن المطلوب ويُحتسب الباقي).</summary>
    public decimal PaidCash { get; set; }
    public decimal PaidCard { get; set; }
    public decimal PaidMada { get; set; }
    public decimal PaidApplePay { get; set; }
}
