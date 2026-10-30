using ERP.Core.Contracts.Shared;

namespace ERP.Core.Models.Shared;

/// <summary>
/// وحدات المنتج التي يُرخَّص بها الاشتراك. الاشتراك يحمل مجموعة منها (مفاتيح نصية ثابتة)، ولا يُسمح باشتراك بلا وحدات.
/// </summary>
public static class PlatformModules
{
    /// <summary>
    /// التجارة العامة: عروض الأسعار والاتفاقيات والطلبات والعقود التجارية وإشعارات التسليم وطلبات الشراء.
    /// المحاسبة نفسها (حسابات، قيود، سندات، تقارير) والبيانات الأساسية مشتركة بين كل الوحدات وليست ضمن هذا المفتاح.
    /// </summary>
    public const string Accounting = "accounting";
    /// <summary>معارض السيارات: مركبات ومشتريات استيراد وعقود بيع وتقارير المعرض.</summary>
    public const string CarShowroom = "car_showroom";
    /// <summary>نقاط البيع: الورديات والبيع والمرتجعات والعروض والولاء. تُباع منفردة أو مع غيرها.</summary>
    public const string Pos = "pos";
    /// <summary>شؤون الموظفين: ملفات الموظفين والإجازات والسلف والحضور ومسير الرواتب ونهاية الخدمة. تُباع كوحدة مستقلة.</summary>
    public const string Hr = "hr";
    /// <summary>
    /// إرسال المستندات عبر WhatsApp Business من رقم المنشأة (ربط رسمي عبر Meta). إضافة تُمنح صراحةً كشؤون الموظفين، لكنها
    /// ليست وحدة دفاتر: لا قيود ولا سندات ولا تقارير لها، فلا تدخل في <see cref="All"/> ولا يصح اشتراك بها وحدها.
    /// </summary>
    public const string WhatsApp = "whatsapp";

    /// <summary>الدليل المركزي للوحدات التي لها شاشات فعلية في النظام.</summary>
    public static readonly PlatformModuleDefinition[] Catalog =
    {
        new() { Number = 1, Key = Accounting, NameAr = "النظام المحاسبي", NameEn = "Accounting", DescriptionAr = "الحسابات والقيود والسندات والتقارير المالية.", DescriptionEn = "Accounts, journals, vouchers and financial reports.", Icon = "account_balance", SortOrder = 1 },
        new() { Number = 2, Key = CarShowroom, NameAr = "معارض السيارات", NameEn = "Car Showroom", DescriptionAr = "مخزون المركبات والشراء والاستيراد وعقود البيع وتقارير المعرض.", DescriptionEn = "Vehicle inventory, procurement, sales contracts and showroom reports.", Icon = "directions_car", SortOrder = 2 },
        new() { Number = 3, Key = Pos, NameAr = "نقاط البيع", NameEn = "Point of Sale", DescriptionAr = "البيع بالورديات والباركود والمرتجعات وطرق الدفع.", DescriptionEn = "Shift-based sales, barcode, returns and payment methods.", Icon = "point_of_sale", SortOrder = 3 },
        new() { Number = 4, Key = Hr, NameAr = "شؤون الموظفين", NameEn = "Human Resources", DescriptionAr = "الموظفون والإجازات والحضور والسلف والرواتب ونهاية الخدمة.", DescriptionEn = "Employees, leaves, attendance, loans, payroll and end of service.", Icon = "groups", SortOrder = 4 },
        new() { Number = 5, Key = WhatsApp, NameAr = "واتساب للأعمال", NameEn = "WhatsApp Business", DescriptionAr = "ربط رقم واتساب المنشأة وإرسال الفواتير للعملاء منه.", DescriptionEn = "Connect the company WhatsApp number and send invoices to customers from it.", Icon = "chat", SortOrder = 5 },
    };

    /// <summary>الإضافات: تُرخَّص مع وحدة دفاتر ولا تحمل قيوداً ولا تقارير.</summary>
    public static readonly string[] AddOns = { WhatsApp };
    /// <summary>وحدات الدفاتر: وحدة القيد والسند والتقرير.</summary>
    public static readonly string[] All = Catalog.Select(module => module.Key).Except(AddOns).ToArray();
    /// <summary>كل ما يحمله الاشتراك: وحدات الدفاتر والإضافات.</summary>
    public static readonly string[] Licensable = Catalog.Select(module => module.Key).ToArray();
    public const string AllCsv = Accounting + "," + CarShowroom + "," + Pos + "," + Hr;
    /// <summary>الوحدات التي تحملها الباقات والاشتراكات افتراضياً؛ شؤون الموظفين إضافة تُمنح صراحةً عند شرائها.</summary>
    public const string DefaultCsv = Accounting + "," + CarShowroom + "," + Pos;

    /// <summary>يطبّع القائمة (حروف صغيرة، بلا تكرار، المعروف فقط، بترتيب ثابت). يرمي إن خلت من وحدة دفاتر أو فيها وحدة غير معروفة.</summary>
    public static string[] Normalize(IEnumerable<string>? modules)
    {
        var list = (modules ?? Array.Empty<string>()).Select(m => m?.Trim().ToLowerInvariant() ?? string.Empty).Where(m => m.Length > 0).Distinct().ToList();
        var unknown = list.Where(m => !Licensable.Contains(m)).ToList();
        if (unknown.Count > 0) throw new ValidationFailedException($"وحدة غير معروفة: {string.Join("، ", unknown)}.");
        if (!list.Any(All.Contains)) throw new ValidationFailedException("يجب اختيار وحدة واحدة على الأقل.");
        return Licensable.Where(list.Contains).ToArray();
    }

    public static string[] Parse(string? csv)
        => string.IsNullOrWhiteSpace(csv) ? new[] { Accounting, Pos } : Licensable.Where(m => csv.Split(',', StringSplitOptions.TrimEntries).Contains(m)).ToArray();

    public static string ToCsv(IEnumerable<string> modules) => string.Join(",", modules);
}
