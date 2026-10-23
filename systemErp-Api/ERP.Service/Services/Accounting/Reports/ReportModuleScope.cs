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
    public static IQueryable<JournalEntry> Entries(IQueryable<JournalEntry> entries, string[]? scope)
        => scope == null ? entries : entries.Where(e => scope.Contains(e.Module)
            || e.SourceType == OpeningBalances.SourceType
            || (e.SourceType == ReversalSource && entries.Any(o => o.Id == e.SourceReferenceId && o.SourceType == OpeningBalances.SourceType)));
}
