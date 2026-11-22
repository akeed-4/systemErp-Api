namespace ERP.Service.Services.Accounting;

internal static class ReportModuleScope
{
    private const string ReversalSource = "reversal";

    public static string[]? Normalize(string? module)
    {
        if (string.IsNullOrWhiteSpace(module)) return null;
        var requested = module.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(key => key.ToLowerInvariant()).Distinct().ToArray();
        if (requested.Length == 0 || requested.Any(key => !PlatformModules.All.Contains(key)))
            throw new ValidationFailedException("وحدة التقرير غير معروفة.");
        return PlatformModules.All.Where(requested.Contains).ToArray();
    }

    /// <summary>
    /// قيود الوحدات المطلوبة، ومعها قيود الأرصدة الافتتاحية للعملاء والموردين والبنوك وعكسها: الطرف مشترك بين الوحدات،
    /// فرصيده الافتتاحي يظهر في ميزان كل وحدة (طرفا القيد معاً فيبقى الميزان متوازناً).
    /// </summary>
    /// <summary>مصدر القيد اليدوي (أو بلا مصدر للقيود القديمة).</summary>
    private const string ManualSource = "manual";

    /// <summary>
    /// القيود اليدوية وعكسها (إلغاؤها): يُدخلها المحاسب لا مستند وحدة، فتُحفظ بوحدة «المحاسبة» أياً كانت مساحة العمل.
    /// </summary>
    public static IQueryable<JournalEntry> Manual(IQueryable<JournalEntry> entries)
        => entries.Where(e => e.SourceType == null || e.SourceType == ManualSource
            || (e.SourceType == ReversalSource && entries.Any(o => o.Id == e.SourceReferenceId && (o.SourceType == null || o.SourceType == ManualSource))));

    /// <summary>
    /// دفتر اليومية: قيود الوحدات المطلوبة ومعها القيود اليدوية دائماً، فلا يختفي قيد أدخله المستخدم لأن مساحة العمل غير المحاسبة
    /// (المعارض مثلاً). القوائم المالية تبقى على <see cref="Entries"/> بلا تغيير.
    /// </summary>
    public static IQueryable<JournalEntry> LedgerEntries(IQueryable<JournalEntry> entries, string[]? scope)
        => scope == null ? entries : entries.Where(e => scope.Contains(e.Module)
            || e.SourceType == OpeningBalances.SourceType || e.SourceType == null || e.SourceType == ManualSource
            || (e.SourceType == ReversalSource && entries.Any(o => o.Id == e.SourceReferenceId
                && (o.SourceType == OpeningBalances.SourceType || o.SourceType == null || o.SourceType == ManualSource))));

    public static IQueryable<JournalEntry> Entries(IQueryable<JournalEntry> entries, string[]? scope)
        => scope == null ? entries : entries.Where(e => scope.Contains(e.Module)
            || e.SourceType == OpeningBalances.SourceType
            || (e.SourceType == ReversalSource && entries.Any(o => o.Id == e.SourceReferenceId && o.SourceType == OpeningBalances.SourceType)));
}
