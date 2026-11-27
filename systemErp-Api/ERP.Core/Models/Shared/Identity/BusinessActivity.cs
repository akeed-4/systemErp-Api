namespace ERP.Core.Models.Shared;

/// <summary>
/// النشاط الرئيسي للمنشأة (على مستوى المنصة، في القاعدة المشتركة وحدها، بلا مرشّح منشأة). يحدد قالب الدليل المحاسبي الأساسي؛
/// والاشتراك يحدد الموديولات وحساباتها الإضافية. نشاط جديد = سجل جديد (وقالب نشاط له) بلا تعديل في الكود.
/// </summary>
public class BusinessActivity
{
    public string Key { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? DescriptionAr { get; set; }
    public string? DescriptionEn { get; set; }
    /// <summary>للأنشطة الفرعية مستقبلاً: النشاط الأب (قالبه يُطبَّق قبل قالب الفرعي). فارغ = نشاط رئيسي.</summary>
    public string? ParentKey { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>مفاتيح الأنشطة المبدئية (البذرة). الكود لا يتفرع عليها؛ القوالب هي التي تحمل الفرق.</summary>
public static class BusinessActivities
{
    public const string Trading = "trading";
    public const string Construction = "construction";
    public const string CarShowroom = "car_showroom";
    public const string Services = "services";
    public const string Manufacturing = "manufacturing";

    /// <summary>نشاط التسجيل حين لا يُرسل الطلب نشاطاً (الواجهات القديمة): التجارة العامة.</summary>
    public const string Default = Trading;

    public static readonly BusinessActivity[] Seed =
    {
        new() { Key = Trading, NameAr = "تجارة عامة", NameEn = "General Trading", SortOrder = 1,
            DescriptionAr = "بيع وشراء البضائع والمخزون", DescriptionEn = "Buying and selling goods with inventory" },
        new() { Key = Construction, NameAr = "مقاولات وإنشاءات", NameEn = "Contracting & Construction", SortOrder = 2,
            DescriptionAr = "مشاريع وعقود ومستخلصات", DescriptionEn = "Projects, contracts and progress billing" },
        new() { Key = CarShowroom, NameAr = "معارض وتجارة السيارات", NameEn = "Car Showrooms & Trading", SortOrder = 3,
            DescriptionAr = "شراء وبيع واستيراد السيارات", DescriptionEn = "Buying, selling and importing vehicles" },
        new() { Key = Services, NameAr = "خدمات", NameEn = "Services", SortOrder = 4,
            DescriptionAr = "تقديم الخدمات والأعمال المهنية", DescriptionEn = "Service delivery and professional work" },
        new() { Key = Manufacturing, NameAr = "صناعي", NameEn = "Manufacturing", SortOrder = 5,
            DescriptionAr = "تصنيع المنتجات من المواد الخام", DescriptionEn = "Producing goods from raw materials" },
    };
}
