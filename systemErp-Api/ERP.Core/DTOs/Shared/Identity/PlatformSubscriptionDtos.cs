namespace ERP.Core.DTOs.Shared;

public class PlatformModuleDto
{
    public int Number { get; set; }
    public string Key { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string DescriptionAr { get; set; } = string.Empty;
    public string DescriptionEn { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public class PlatformAccessDto
{
    /// <summary>يرى لوحة المنصة (عرض).</summary>
    public bool IsPlatformAdmin { get; set; }
    /// <summary>يعدّل الاشتراكات والباقات ويمنح صلاحية المنصة لغيره.</summary>
    public bool CanManage { get; set; }
}

public class PlatformPlanDto
{
    public string Id { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string DescriptionAr { get; set; } = string.Empty;
    public string DescriptionEn { get; set; } = string.Empty;
    public string BadgeAr { get; set; } = string.Empty;
    public string BadgeEn { get; set; } = string.Empty;
    public bool IsPopular { get; set; }
    public string[] FeaturesAr { get; set; } = Array.Empty<string>();
    public string[] FeaturesEn { get; set; } = Array.Empty<string>();
    public decimal PriceMonthly { get; set; }
    public decimal PriceYearly { get; set; }
    public int? MaxUsers { get; set; }
    public int? MaxInvoicesPerMonth { get; set; }
    public int? Branches { get; set; }
    public bool ZatcaPhase2Enabled { get; set; }
    public string[] Modules { get; set; } = Array.Empty<string>();
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    /// <summary>عدد المنشآت المشتركة حالياً (اشتراك فعّال أو تجريبي).</summary>
    public int Subscribers { get; set; }
}

/// <summary>باقة جديدة: رمزها الفريد (حروف إنجليزية صغيرة وأرقام و- و_) وترتيب عرضها، مع حقول التعديل نفسها.</summary>
public class CreatePlanRequestDto : UpdatePlanRequestDto
{
    public string Id { get; set; } = string.Empty;
    /// <summary>null = بعد آخر باقة.</summary>
    public int? SortOrder { get; set; }
}

public class UpdatePlanRequestDto
{
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    // التفاصيل التسويقية: null = تبقى كما هي (طلب لا يرسلها لا يمسحها)
    public string? DescriptionAr { get; set; }
    public string? DescriptionEn { get; set; }
    public string? BadgeAr { get; set; }
    public string? BadgeEn { get; set; }
    public bool? IsPopular { get; set; }
    public string[]? FeaturesAr { get; set; }
    public string[]? FeaturesEn { get; set; }
    public decimal PriceMonthly { get; set; }
    public decimal PriceYearly { get; set; }
    public int? MaxUsers { get; set; }
    public int? MaxInvoicesPerMonth { get; set; }
    public int? Branches { get; set; }
    public bool ZatcaPhase2Enabled { get; set; } = true;
    public string[] Modules { get; set; } = PlatformModules.All;
    public bool IsActive { get; set; } = true;
}

public class PlatformTenantSubscriptionDto
{
    public Guid TenantId { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string VatNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public bool TenantActive { get; set; }
    public int UsersCount { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>أحدث اشتراك للمنشأة (null إن لم يكن لها اشتراك).</summary>
    public SubscriptionDto? Subscription { get; set; }
    /// <summary>الحالة الفعلية: الاشتراك الفعّال المنتهي تاريخه يُعرض منتهياً.</summary>
    public SubscriptionStatus? EffectiveStatus { get; set; }
    public int? DaysRemaining { get; set; }
    /// <summary>قاعدة المنشأة: مشتركة (بلا سجل ربط) أو مستقلة، وحالة تجهيز المستقلة. لا يُعاد نص الاتصال أبداً.</summary>
    public TenantDatabaseMode DatabaseMode { get; set; } = TenantDatabaseMode.Shared;
    public TenantDatabaseStatus? DatabaseStatus { get; set; }
}

/// <summary>إعدادات المنصة التي يغيّرها مديرها من اللوحة.</summary>
public class PlatformSettingsDto
{
    /// <summary>قاعدة المنشأة المسجّلة من التسجيل العام (الذي لا يحمل الخيار): مشتركة أو مستقلة تُجهَّز تلقائياً.</summary>
    public TenantDatabaseMode DefaultDatabaseMode { get; set; } = TenantDatabaseMode.Shared;
    /// <summary>false = لم يغيّره مدير بعد والقيمة من إعدادات الخادم.</summary>
    public bool IsCustomized { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class UpdatePlatformSettingsDto
{
    public TenantDatabaseMode DefaultDatabaseMode { get; set; } = TenantDatabaseMode.Shared;
}

/// <summary>حساب Paymob الخاص بالمنصة لدفع الاشتراكات: المحفوظ من اللوحة، وإلا من إعدادات الخادم. الأسرار لا تُعاد أبداً.</summary>
public class PlatformPaymobSettingsDto
{
    public string BaseUrl { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public bool HasSecretKey { get; set; }
    public bool HasHmacSecret { get; set; }
    public string IntegrationIds { get; set; } = string.Empty;
    /// <summary>المفاتيح والروابط مكتملة لاستقبال دفع الاشتراكات.</summary>
    public bool IsConfigured { get; set; }
    /// <summary>روابط الإشعار والعودة من إعدادات الخادم (Paymob:PublicApiUrl / Paymob:FrontendUrl).</summary>
    public bool HasServerUrls { get; set; }
    /// <summary>رابط الإشعارات الذي يُضبط في لوحة Paymob.</summary>
    public string WebhookUrl { get; set; } = string.Empty;
    /// <summary>false = لم يُحفظ من اللوحة بعد والقيم من إعدادات الخادم.</summary>
    public bool IsCustomized { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class UpdatePlatformPaymobSettingsDto
{
    public string BaseUrl { get; set; } = "https://ksa.paymob.com";
    public string PublicKey { get; set; } = string.Empty;
    /// <summary>فارغ = يبقى المفتاح المحفوظ.</summary>
    public string? SecretKey { get; set; }
    public string? HmacSecret { get; set; }
    public string IntegrationIds { get; set; } = string.Empty;
}

/// <summary>
/// إنشاء منشأة من مدير المنصة: بيانات التسجيل نفسها + نوع قاعدة البيانات. لا يقبله إلا مسار المنصة؛
/// التسجيل العام يستقبل <see cref="CompanyRegistrationRequestDto"/> الذي لا يحمل هذا الحقل.
/// </summary>
public class PlatformCreateTenantRequestDto : CompanyRegistrationRequestDto
{
    public TenantDatabaseMode DatabaseMode { get; set; } = TenantDatabaseMode.Shared;
}

public class PlatformSummaryDto
{
    public int TotalTenants { get; set; }
    public int Active { get; set; }
    public int Trial { get; set; }
    public int Expired { get; set; }
    public int Suspended { get; set; }
    public int Cancelled { get; set; }
    public int NoSubscription { get; set; }
    /// <summary>فعّالة وتنتهي خلال 14 يوماً.</summary>
    public int ExpiringSoon { get; set; }
    /// <summary>إيراد شهري متكرر تقديري (الاشتراكات الفعّالة، السنوي يُقسَّم على 12) قبل الضريبة.</summary>
    public decimal MonthlyRecurringRevenue { get; set; }
}

public class PlatformChangePlanRequestDto
{
    public string PlanId { get; set; } = string.Empty;
    public SubscriptionBillingCycle BillingCycle { get; set; } = SubscriptionBillingCycle.Monthly;
    public string PaymentMethod { get; set; } = "manual";
    /// <summary>null = وحدات الباقة الافتراضية.</summary>
    public string[]? Modules { get; set; }
}

public class PlatformExtendRequestDto
{
    public int Days { get; set; }
}

public class PlatformSetStatusRequestDto
{
    public SubscriptionStatus Status { get; set; }
}

public class PlatformSetModulesRequestDto
{
    public string[] Modules { get; set; } = Array.Empty<string>();
}
