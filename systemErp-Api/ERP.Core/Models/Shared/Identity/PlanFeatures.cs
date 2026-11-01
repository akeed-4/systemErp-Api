using ERP.Core.Contracts.Shared;

namespace ERP.Core.Models.Shared;

/// <summary>
/// ميزات الباقة: طبقة تحت الوحدات (<see cref="PlatformModules"/>) تفصل الأجزاء المتقدمة داخل وحدة المحاسبة عن الأساسية،
/// فتشمل الباقة الصغيرة المحاسبة دون مراكز التكلفة أو الأصول مثلاً. الباقة تحمل مجموعة منها، والاشتراك ينسخها وقت الشراء.
/// ما لا يرتبط بميزة (الفواتير والسندات والقيود والتقارير الأساسية والبيانات الرئيسية) متاح لكل باقة فيها الوحدة.
/// يفرضها السرفر بسمة RequireFeature، والواجهة في module-access.ts.
/// </summary>
public static class PlanFeatures
{
    /// <summary>الدورة التجارية: عروض الأسعار والاتفاقيات والطلبات والعقود وإشعارات التسليم وطلبات الشراء.</summary>
    public const string CommercialCycle = "commercial_cycle";
    /// <summary>التكاليف الإضافية للمشتريات (شحن وتخليص) وتحميلها على الأصناف.</summary>
    public const string LandedCosts = "landed_costs";
    public const string CostCenters = "cost_centers";
    /// <summary>الأصول الثابتة وإهلاكها واستبعادها.</summary>
    public const string FixedAssets = "fixed_assets";
    /// <summary>عملات غير العملة الأساسية: إضافتها وتعديلها، والمستندات بعملة أجنبية.</summary>
    public const string MultiCurrency = "multi_currency";
    public const string BankReconciliation = "bank_reconciliation";
    /// <summary>سياسات الموافقات وطلباتها.</summary>
    public const string ApprovalPolicies = "approval_policies";
    /// <summary>التحويل بين المستودعات.</summary>
    public const string StockTransfers = "stock_transfers";

    public static readonly PlanFeatureDefinition[] Catalog =
    {
        new(CommercialCycle, "الدورة التجارية", "Commercial cycle", "عروض الأسعار والاتفاقيات والطلبات والعقود وإشعارات التسليم وطلبات الشراء"),
        new(LandedCosts, "التكاليف الإضافية للمشتريات", "Purchase landed costs", "تحميل الشحن والتخليص على تكلفة الأصناف"),
        new(CostCenters, "مراكز التكلفة", "Cost centers", "تحليل القيود والمستندات بمراكز التكلفة"),
        new(FixedAssets, "الأصول الثابتة", "Fixed assets", "سجل الأصول وإهلاكها واستبعادها"),
        new(MultiCurrency, "تعدد العملات", "Multi-currency", "عملات أجنبية وأسعار صرف ومستندات بغير العملة الأساسية"),
        new(BankReconciliation, "التسوية البنكية", "Bank reconciliation", "مطابقة حركات البنوك مع كشوفها"),
        new(ApprovalPolicies, "سياسات الموافقات", "Approval policies", "مسارات اعتماد المستندات وطلباتها"),
        new(StockTransfers, "التحويل بين المستودعات", "Stock transfers", "نقل الأصناف بين المستودعات"),
    };

    public static readonly string[] All = Catalog.Select(f => f.Key).ToArray();
    public static readonly string AllCsv = string.Join(",", All);

    /// <summary>يطبّع القائمة (حروف صغيرة، بلا تكرار، بترتيب الدليل). يرمي إن فيها ميزة غير معروفة؛ القائمة الفارغة مسموحة.</summary>
    public static string[] Normalize(IEnumerable<string>? features)
    {
        var list = (features ?? Array.Empty<string>()).Select(f => f?.Trim().ToLowerInvariant() ?? string.Empty).Where(f => f.Length > 0).Distinct().ToList();
        var unknown = list.Where(f => !All.Contains(f)).ToList();
        if (unknown.Count > 0) throw new ValidationFailedException($"ميزة غير معروفة: {string.Join("، ", unknown)}.");
        return All.Where(list.Contains).ToArray();
    }

    /// <summary>null = لم تُحدَّد (بيانات قديمة) فكل الميزات؛ والنص الفارغ = بلا ميزات متقدمة.</summary>
    public static string[] Parse(string? csv)
        => csv == null ? All : All.Where(f => csv.Split(',', StringSplitOptions.TrimEntries).Contains(f)).ToArray();

    public static string ToCsv(IEnumerable<string> features) => string.Join(",", features);

    public static string NameAr(string key) => Catalog.FirstOrDefault(f => f.Key == key)?.NameAr ?? key;
}

public record PlanFeatureDefinition(string Key, string NameAr, string NameEn, string DescriptionAr);
