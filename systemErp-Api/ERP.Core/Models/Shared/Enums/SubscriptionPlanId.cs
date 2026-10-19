namespace ERP.Core.Models.Shared;

/// <summary>
/// رموز الباقات الافتراضية المزروعة. الباقة تُعرَّف برمز نصي (حروف إنجليزية صغيرة وأرقام و- و_)،
/// ويُنشئ مدير المنصة باقات أخرى برموزها من شاشة الباقات.
/// </summary>
public static class SubscriptionPlanId
{
    public const string Starter = "starter";
    public const string Professional = "professional";
    public const string Enterprise = "enterprise";

    /// <summary>طول الرمز الأقصى (يُتحقق منه عند الإنشاء).</summary>
    public const int MaxLength = 64;

    /// <summary>الرمز كما يُخزَّن: بلا مسافات وبأحرف صغيرة.</summary>
    public static string Normalize(string? id) => (id ?? string.Empty).Trim().ToLowerInvariant();

    public static bool IsValid(string? id)
    {
        var v = Normalize(id);
        return v.Length is >= 2 and <= MaxLength && v.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_');
    }
}
