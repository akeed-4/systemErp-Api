namespace ERP.Core.DTOs.POS;

/// <summary>
/// تسعير السلة من الخادم قبل الدفع (نفس محرك الإتمام: عروض، خصم يدوي، كوبون، ولاء، ضريبة) — بلا أي أثر.
/// أخطاء الكوبون/الولاء تُعاد رسائل بدل رفض الطلب ليبقى الكاشير قادراً على المتابعة.
/// </summary>
public class PosQuoteDto
{
    public List<PosQuoteLineDto> Lines { get; set; } = new();
    public decimal GrossTotal { get; set; }
    public decimal LineDiscounts { get; set; }
    public decimal CouponDiscount { get; set; }
    public string? CouponCode { get; set; }
    public string? CouponError { get; set; }
    public decimal LoyaltyDiscount { get; set; }
    public int LoyaltyPointsRedeemed { get; set; }
    public string? LoyaltyError { get; set; }
    public int LoyaltyPointsBalance { get; set; }
    public decimal TotalDiscount { get; set; }
    public decimal NetBeforeVat { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public int PointsToEarn { get; set; }
}

public class PosQuoteLineDto
{
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal VatRate { get; set; }
    public decimal OfferDiscount { get; set; }
    public decimal ManualDiscount { get; set; }
    public decimal Net { get; set; }
    public decimal VatAmount { get; set; }
    public decimal Total { get; set; }
    public decimal StockOnHand { get; set; }
}
