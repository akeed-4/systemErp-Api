namespace ERP.Core.Models.POS;

/// <summary>
/// قواعد الخصومات وبرنامج الولاء في نقاط البيع — سجل واحد لكل منشأة. حدود نقطة البيع (السماح بالخصم ونسبته) تضيّقها ولا توسّعها.
/// </summary>
public class PosPromotionSettings : BaseEntity
{
    // ---------- الخصومات ----------
    /// <summary>السماح بالخصم اليدوي على السطر (مع صلاحية الخصم للمستخدم).</summary>
    public bool AllowManualDiscount { get; set; } = true;
    /// <summary>أقصى خصم يدوي على السطر كنسبة من قيمته (فارغ = بلا حد).</summary>
    public decimal? MaxLineDiscountPercent { get; set; }
    /// <summary>أقصى خصم على الفاتورة من الكاشير (يدوي + كوبون + نقاط) كنسبة من إجماليها؛ العروض التلقائية خارجه (فارغ = بلا حد).</summary>
    public decimal? MaxInvoiceDiscountPercent { get; set; }
    public bool AllowCoupons { get; set; } = true;
    /// <summary>يجوز الكوبون مع سلة فيها عروض تلقائية مطبقة.</summary>
    public bool AllowCouponWithOffers { get; set; } = true;
    /// <summary>استبدال نقاط الولاء كخصم على الفاتورة.</summary>
    public bool AllowLoyaltyRedemption { get; set; } = true;

    // ---------- برنامج الولاء ----------
    /// <summary>البرنامج مفعّل: كسب النقاط واستبدالها. المعطّل لا يكسب ولا يستبدل (والأرصدة محفوظة).</summary>
    public bool LoyaltyEnabled { get; set; } = true;
    /// <summary>ريال مشتريات لكل نقطة مكتسبة.</summary>
    public decimal SarPerEarnedPoint { get; set; } = 10m;
    /// <summary>أيام تعليق النقاط المكتسبة قبل إتاحتها: فارغ = مدة ترجيع المبيعات من إعدادات المحاسبة، 0 = فوراً.</summary>
    public int? PointsPendingDays { get; set; }
    /// <summary>قيمة النقطة بالريال عند الاستبدال.</summary>
    public decimal PointValueSar { get; set; } = 0.10m;
    /// <summary>أقل عدد نقاط يُقبل استبداله في الفاتورة.</summary>
    public int MinPointsToRedeem { get; set; }
    /// <summary>أقصى ما يُدفع بالنقاط كنسبة من الفاتورة (فارغ = حتى كامل الفاتورة).</summary>
    public decimal? MaxRedeemPercent { get; set; }
    /// <summary>إجمالي النقاط المكتسبة لبلوغ كل فئة.</summary>
    public int SilverThreshold { get; set; } = 1000;
    public int GoldThreshold { get; set; } = 5000;
    public int PlatinumThreshold { get; set; } = 10000;
}
