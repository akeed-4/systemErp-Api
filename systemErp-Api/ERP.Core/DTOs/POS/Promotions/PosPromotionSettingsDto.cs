namespace ERP.Core.DTOs.POS;

/// <summary>قواعد الخصومات وبرنامج الولاء في نقاط البيع.</summary>
public class PosPromotionSettingsDto : UpdatePosPromotionSettingsDto
{
    public Guid Id { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class UpdatePosPromotionSettingsDto
{
    public bool AllowManualDiscount { get; set; } = true;
    public decimal? MaxLineDiscountPercent { get; set; }
    public decimal? MaxInvoiceDiscountPercent { get; set; }
    public bool AllowCoupons { get; set; } = true;
    public bool AllowCouponWithOffers { get; set; } = true;
    public bool AllowLoyaltyRedemption { get; set; } = true;

    public bool LoyaltyEnabled { get; set; } = true;
    public decimal SarPerEarnedPoint { get; set; } = 10m;
    public int? PointsPendingDays { get; set; }
    public decimal PointValueSar { get; set; } = 0.10m;
    public int MinPointsToRedeem { get; set; }
    public decimal? MaxRedeemPercent { get; set; }
    public int SilverThreshold { get; set; } = 1000;
    public int GoldThreshold { get; set; } = 5000;
    public int PlatinumThreshold { get; set; } = 10000;
}
