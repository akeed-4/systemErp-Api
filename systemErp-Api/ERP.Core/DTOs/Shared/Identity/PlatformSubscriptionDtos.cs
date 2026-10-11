namespace ERP.Core.DTOs.Shared;

public class PlatformAccessDto
{
    /// <summary>يرى لوحة المنصة (عرض).</summary>
    public bool IsPlatformAdmin { get; set; }
    /// <summary>يعدّل الاشتراكات والباقات ويمنح صلاحية المنصة لغيره.</summary>
    public bool CanManage { get; set; }
}

public class PlatformPlanDto
{
    public SubscriptionPlanId Id { get; set; }
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
    public SubscriptionPlanId PlanId { get; set; }
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
