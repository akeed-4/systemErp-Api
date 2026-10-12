namespace ERP.Service.Services.Accounting;

internal static class ReportModuleScope
{
    public static string[]? Normalize(string? module)
    {
        if (string.IsNullOrWhiteSpace(module)) return null;
        var requested = module.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(key => key.ToLowerInvariant()).Distinct().ToArray();
        if (requested.Length == 0 || requested.Any(key => !PlatformModules.All.Contains(key)))
            throw new ValidationFailedException("وحدة التقرير غير معروفة.");
        return PlatformModules.All.Where(requested.Contains).ToArray();
    }
}