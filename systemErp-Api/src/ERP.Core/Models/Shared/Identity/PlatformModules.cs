using ERP.Core.Contracts.Shared;

namespace ERP.Core.Models.Shared;

/// <summary>
/// وحدات المنتج التي يُرخَّص بها الاشتراك. الاشتراك يحمل مجموعة منها (مفاتيح نصية ثابتة)، ولا يُسمح باشتراك بلا وحدات.
/// </summary>
public static class PlatformModules
{
    /// <summary>النظام المحاسبي العام: مبيعات ومشتريات وسندات وحسابات وتقارير ونقطة بيع.</summary>
    public const string Accounting = "accounting";
    /// <summary>معارض السيارات: مركبات ومشتريات استيراد وعقود بيع وتقارير المعرض.</summary>
    public const string CarShowroom = "car_showroom";

    public static readonly string[] All = { Accounting, CarShowroom };
    public const string AllCsv = Accounting + "," + CarShowroom;

    /// <summary>يطبّع القائمة (حروف صغيرة، بلا تكرار، المعروف فقط، بترتيب ثابت). يرمي إن كانت فارغة أو فيها وحدة غير معروفة.</summary>
    public static string[] Normalize(IEnumerable<string>? modules)
    {
        var list = (modules ?? Array.Empty<string>()).Select(m => m?.Trim().ToLowerInvariant() ?? string.Empty).Where(m => m.Length > 0).Distinct().ToList();
        var unknown = list.Where(m => !All.Contains(m)).ToList();
        if (unknown.Count > 0) throw new ValidationFailedException($"وحدة غير معروفة: {string.Join("، ", unknown)}.");
        if (list.Count == 0) throw new ValidationFailedException("يجب اختيار وحدة واحدة على الأقل.");
        return All.Where(list.Contains).ToArray();
    }

    public static string[] Parse(string? csv)
        => string.IsNullOrWhiteSpace(csv) ? new[] { Accounting } : All.Where(m => csv.Split(',', StringSplitOptions.TrimEntries).Contains(m)).ToArray();

    public static string ToCsv(IEnumerable<string> modules) => string.Join(",", modules);
}
