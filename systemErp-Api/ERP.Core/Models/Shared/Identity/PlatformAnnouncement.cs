namespace ERP.Core.Models.Shared;

/// <summary>طريقة عرض الإعلان داخل النظام.</summary>
public static class AnnouncementDisplays
{
    /// <summary>نافذة منبثقة في منتصف الشاشة بعد الدخول.</summary>
    public const string Popup = "popup";
    /// <summary>شريط أعلى المحتوى لا يحجب العمل.</summary>
    public const string Banner = "banner";
    public static readonly string[] All = { Popup, Banner };
}

/// <summary>ألوان الإعلان: ترويجي، معلومة، ميزة جديدة، تنبيه.</summary>
public static class AnnouncementStyles
{
    public const string Promo = "promo";
    public const string Info = "info";
    public const string Feature = "feature";
    public const string Warning = "warning";
    public static readonly string[] All = { Promo, Info, Feature, Warning };
}

/// <summary>الفئة المستهدفة حسب حالة اشتراك المنشأة.</summary>
public static class AnnouncementAudiences
{
    public const string All = "all";
    /// <summary>المنشآت في الفترة التجريبية.</summary>
    public const string Trial = "trial";
    /// <summary>المنشآت باشتراك مدفوع فعّال.</summary>
    public const string Paid = "paid";
    public static readonly string[] Values = { All, Trial, Paid };
}

/// <summary>تكرار الظهور بعد أن يغلقه المستخدم.</summary>
public static class AnnouncementFrequencies
{
    /// <summary>يظهر مرة واحدة لكل مستخدم على الجهاز.</summary>
    public const string Once = "once";
    /// <summary>يعود للظهور مع كل تسجيل دخول.</summary>
    public const string EveryLogin = "every_login";
    public static readonly string[] All = { Once, EveryLogin };
}

/// <summary>
/// إعلان من مدير المنصة لعملاء النظام (ميزة جديدة، عرض، تنبيه صيانة...). على مستوى المنصة (ليس تابعاً لمنشأة) مثل رموز الخصم،
/// ويُستهدف بحالة الاشتراك والباقات والمنشآت، ويظهر بين تاريخي البداية والنهاية.
/// </summary>
public class PlatformAnnouncement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TitleAr { get; set; } = string.Empty;
    public string? TitleEn { get; set; }
    public string BodyAr { get; set; } = string.Empty;
    public string? BodyEn { get; set; }
    /// <summary>عنوان صغير فوق العنوان الرئيسي (مثل «جديد»).</summary>
    public string? BadgeAr { get; set; }
    public string? BadgeEn { get; set; }
    /// <summary>نص زر الإجراء؛ فارغ = بلا زر.</summary>
    public string? CtaLabelAr { get; set; }
    public string? CtaLabelEn { get; set; }
    /// <summary>مسار داخلي يبدأ بـ / أو رابط https خارجي.</summary>
    public string? CtaUrl { get; set; }
    /// <summary>اسم أيقونة Material.</summary>
    public string Icon { get; set; } = "campaign";
    public string Style { get; set; } = AnnouncementStyles.Promo;
    public string Display { get; set; } = AnnouncementDisplays.Popup;
    public string Audience { get; set; } = AnnouncementAudiences.All;
    /// <summary>الباقات المستهدفة مفصولة بفواصل؛ فارغ = كل الباقات.</summary>
    public string? PlanIds { get; set; }
    /// <summary>منشآت بعينها مفصولة بفواصل؛ فارغ = كل المنشآت المطابقة.</summary>
    public string? TenantIds { get; set; }
    public string Frequency { get; set; } = AnnouncementFrequencies.Once;
    /// <summary>الأعلى يظهر أولاً.</summary>
    public int Priority { get; set; }
    /// <summary>بداية الظهور (UTC)؛ null = فوراً.</summary>
    public DateTime? StartsAt { get; set; }
    /// <summary>نهاية الظهور (UTC)؛ null = بلا نهاية.</summary>
    public DateTime? EndsAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
