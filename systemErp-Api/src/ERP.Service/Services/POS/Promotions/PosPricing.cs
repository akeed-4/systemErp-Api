using ERP.Core.Contracts.POS;
using ERP.Core.DTOs.POS;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.POS;

/// <summary>قواعد التسعير والخصم الخاصة بنقطة البيع (كوبونات، عروض، ولاء).</summary>
public static class PosPricing
{
    public const decimal PointValueSar = 0.10m;      // 100 نقطة = 10 ريال
    public const decimal SarPerEarnedPoint = 10m;    // نقطة لكل 10 ريال

    public static string? CouponRejectionReason(PosCoupon? c, decimal cartAmount, DateTime now)
    {
        if (c == null) return "الكوبون غير موجود.";
        if (!c.IsActive) return "الكوبون غير مفعّل.";
        if (now < c.ValidFrom || now > c.ValidTo.Date.AddDays(1)) return "الكوبون خارج فترة الصلاحية.";
        if (c.UsageLimit.HasValue && c.UsageCount >= c.UsageLimit) return "تم استنفاد حد استخدام الكوبون.";
        if (cartAmount < c.MinCartAmount) return $"الحد الأدنى للسلة {c.MinCartAmount:0.00} ريال.";
        return null;
    }

    public static decimal CouponDiscount(PosCoupon c, decimal cartAmount)
    {
        var d = c.DiscountType == PosDiscountType.Percent ? cartAmount * c.DiscountValue / 100m : c.DiscountValue;
        if (c.MaxDiscountAmount.HasValue) d = Math.Min(d, c.MaxDiscountAmount.Value);
        return DocumentPricing.Round(Math.Min(d, cartAmount));
    }

    public static LoyaltyTier TierFor(int totalEarned) => totalEarned switch
    {
        >= 10000 => LoyaltyTier.Platinum, >= 5000 => LoyaltyTier.Gold, >= 1000 => LoyaltyTier.Silver, _ => LoyaltyTier.Bronze,
    };

    /// <summary>أفضل خصم عرض لسطر (عرض واحد لكل سطر): على الصنف، أو التصنيف، أو عام.</summary>
    public static decimal BestOfferDiscount(IEnumerable<PosOffer> offers, Guid itemId, Guid? categoryId, decimal qty, decimal unitPrice)
    {
        var lineTotal = qty * unitPrice;
        decimal best = 0;
        foreach (var o in offers)
        {
            if (o.TargetItemId.HasValue && o.TargetItemId != itemId) continue;
            if (o.Type == PosOfferType.CategoryDiscount && (categoryId == null || o.TargetCategoryId != categoryId)) continue;
            if (o.TargetCategoryId.HasValue && o.Type != PosOfferType.CategoryDiscount && o.TargetCategoryId != categoryId) continue;

            decimal d = o.Type switch
            {
                PosOfferType.PercentageDiscount or PosOfferType.CategoryDiscount => lineTotal * (o.DiscountPercent ?? 0) / 100m,
                PosOfferType.FixedDiscount => (o.DiscountAmount ?? 0) * qty,
                PosOfferType.BuyXGetY when o.BuyQuantity > 0 && o.GetQuantity > 0 =>
                    Math.Floor(qty / (o.BuyQuantity.Value + o.GetQuantity.Value)) * o.GetQuantity.Value * unitPrice,
                _ => 0,
            };
            best = Math.Max(best, Math.Min(d, lineTotal));
        }
        return DocumentPricing.Round(best);
    }
}
