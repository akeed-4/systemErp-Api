namespace ERP.Core.Models.Shared;

/// <summary>
/// تعريف باقة اشتراك على مستوى المنصة (ليست تابعة لمنشأة، فلا ترث BaseEntity ولا مرشّح منشأة).
/// تُعدَّل من شاشة مدير المنصة، والتعديل يسري على الاشتراكات الجديدة فقط.
/// </summary>
public class PlanDefinition
{
    public SubscriptionPlanId Id { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    /// <summary>وصف تسويقي يظهر في بطاقة الباقة عند التسجيل.</summary>
    public string DescriptionAr { get; set; } = string.Empty;
    public string DescriptionEn { get; set; } = string.Empty;
    /// <summary>شارة البطاقة (مثل «الأكثر طلباً»).</summary>
    public string BadgeAr { get; set; } = string.Empty;
    public string BadgeEn { get; set; } = string.Empty;
    /// <summary>الباقة المُبرَزة في شاشة التسجيل.</summary>
    public bool IsPopular { get; set; }
    /// <summary>مزايا الباقة: ميزة في كل سطر.</summary>
    public string FeaturesAr { get; set; } = string.Empty;
    public string FeaturesEn { get; set; } = string.Empty;
    public decimal PriceMonthly { get; set; }
    public decimal PriceYearly { get; set; }
    /// <summary>null = غير محدود.</summary>
    public int? MaxUsers { get; set; }
    public int? MaxInvoicesPerMonth { get; set; }
    public int? Branches { get; set; }
    public bool ZatcaPhase2Enabled { get; set; } = true;
    /// <summary>الوحدات المرخّصة (مفاتيح PlatformModules مفصولة بفاصلة).</summary>
    public string ModuleKeys { get; set; } = PlatformModules.AllCsv;
    /// <summary>الباقة المعطّلة لا تظهر في التسجيل ولا تُباع، وتبقى للمشتركين الحاليين.</summary>
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
