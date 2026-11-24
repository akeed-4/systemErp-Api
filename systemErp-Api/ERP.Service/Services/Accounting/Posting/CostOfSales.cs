namespace ERP.Service.Services.Accounting;

/// <summary>
/// قاعدة واحدة لمجموعة تكلفة المبيعات (قائمة الدخل، أداء مراكز التكلفة، إعدادات الجرد الدوري وتهيئة حساباته):
/// حساب مصروف رقمه تحت 51 (كما صُنِّف دائماً)، أو داخل المجموعة الأب لحساب تكلفة البضاعة المباعة الفعّال (شجرة بترقيم آخر).
/// وحسابات الجرد الدوري المحددة في الإعدادات تُعرض في تكلفة المبيعات أياً كان نوعها التاريخي (لا تظهر إيراداً ولا مصروفاً تشغيلياً).
/// </summary>
internal static class CostOfSales
{
    public const string DefaultGroup = "51";

    /// <summary>أب حساب تكلفة البضاعة المباعة الفعّال، أو null إن لم يوجد في الشجرة.</summary>
    public static string? Group(AccountingSettings s, Func<string, string?> parentOf) => parentOf(s.Cogs());

    public static bool Contains(string code, AccountCategory type, Func<string, string?> parentOf, string? group)
    {
        if (type != AccountCategory.Expense) return false;
        if (code.StartsWith(DefaultGroup, StringComparison.Ordinal)) return true;
        if (group == null) return false;
        for (var c = parentOf(code); c != null; c = parentOf(c))
            if (c == group) return true;
        return false;
    }

    /// <summary>حسابات الجرد الدوري المحددة (المشتريات ومردوداتها وأول المدة وآخرها).</summary>
    public static HashSet<string> PeriodicAccounts(AccountingSettings s)
        => new[] { s.PurchasesAccountCode, s.PurchaseReturnsAccountCode, s.OpeningInventoryAccountCode, s.EndingInventoryAccountCode }
            .OfType<string>().ToHashSet();

    /// <summary>
    /// بضاعة أول المدة وآخرها الخاصة بالمواقع (Warehouse): تُنشأ بجوار حسابي الإعدادات فترث نوع أبيهما، فتُعرض في تكلفة المبيعات
    /// صراحةً أياً كان نوعها (لا إيراداً ولا مصروفاً تشغيلياً).
    /// </summary>
    public static async Task<HashSet<string>> SitePeriodicAccountsAsync(ERP.Service.Data.ErpDbContext db, CancellationToken ct)
        => (await db.Set<Warehouse>().AsNoTracking()
                .Where(w => w.OpeningInventoryAccountCode != null || w.EndingInventoryAccountCode != null)
                .Select(w => new { w.OpeningInventoryAccountCode, w.EndingInventoryAccountCode }).ToListAsync(ct))
            .SelectMany(w => new[] { w.OpeningInventoryAccountCode, w.EndingInventoryAccountCode }).OfType<string>().ToHashSet();
}
