using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// قراءة القوالب والأنشطة من القاعدة المشتركة (المصدر المركزي الوحيد، أياً كانت قاعدة المنشأة) واختيار ما يُطبَّق على منشأة.
/// تُزرع البذرة عند أول قراءة إن كان الجدول فارغاً (كقائمة الدول)، فلا تحمل المهاجرة بيانات.
/// </summary>
internal static class ChartTemplateCatalog
{
    /// <summary>
    /// تركيبة المنشأة القديمة (بلا نشاط): الأساسي والتجارة ومعارض السيارات والموارد البشرية والمقاولات = الشجرة القديمة وحسابات العقود
    /// التي كانت تُنشأ مع كل منشأة. لمسارات الإنشاء التي لا تحمل نشاطاً (والاختبارات).
    /// </summary>
    public static readonly string[] LegacyModules = { PlatformModules.CarShowroom, PlatformModules.Hr, PlatformModules.Construction };

    public static async Task EnsureSeededAsync(ErpDbContext catalog, CancellationToken ct)
    {
        if (!await catalog.Set<BusinessActivity>().AnyAsync(ct))
        {
            foreach (var a in BusinessActivities.Seed)
                catalog.Add(new BusinessActivity
                {
                    Key = a.Key, NameAr = a.NameAr, NameEn = a.NameEn, DescriptionAr = a.DescriptionAr, DescriptionEn = a.DescriptionEn,
                    SortOrder = a.SortOrder, IsActive = true,
                });
            await SaveSeedAsync(catalog, ct);
        }
        if (!await catalog.Set<ChartTemplate>().AnyAsync(ct))
        {
            catalog.AddRange(ChartTemplateSeed.Build());
            await SaveSeedAsync(catalog, ct);
        }
    }

    /// <summary>طلبان متزامنان على قاعدة فارغة: الفهرس الفريد يرفض الثاني، فيُكتفى بما زرعه الأول.</summary>
    private static async Task SaveSeedAsync(ErpDbContext catalog, CancellationToken ct)
    {
        try { await catalog.SaveChangesAsync(ct); }
        catch (DbUpdateException) { catalog.ChangeTracker.Clear(); }
    }

    public static async Task<List<ChartTemplate>> LoadAsync(ErpDbContext catalog, CancellationToken ct)
    {
        await EnsureSeededAsync(catalog, ct);
        return await catalog.Set<ChartTemplate>().AsNoTracking().Include(t => t.Accounts).ToListAsync(ct);
    }

    public static async Task<BusinessActivity?> ActivityAsync(ErpDbContext catalog, string? key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        await EnsureSeededAsync(catalog, ct);
        return await catalog.Set<BusinessActivity>().AsNoTracking().FirstOrDefaultAsync(a => a.Key == key, ct);
    }

    /// <summary>النشاط المرسل إن كان فعّالاً، وإلا التجارة العامة (الطلب بلا نشاط). نشاط غير معروف يُرفض.</summary>
    public static async Task<string> ResolveActivityKeyAsync(ErpDbContext catalog, string? requested, CancellationToken ct)
    {
        var key = string.IsNullOrWhiteSpace(requested) ? BusinessActivities.Default : requested.Trim().ToLowerInvariant();
        var activity = await ActivityAsync(catalog, key, ct);
        if (activity is not { IsActive: true }) throw new ValidationFailedException(Messages.BusinessActivityInvalid);
        return activity.Key;
    }

    /// <summary>
    /// قوالب منشأة بالترتيب: الأساسي (إن طُلب)، ثم قوالب النشاط (الأب قبل الفرعي)، ثم قوالب الموديولات بترتيب الكتالوج. الفعّالة فقط.
    /// </summary>
    public static async Task<List<ChartTemplate>> SelectAsync(ErpDbContext catalog, List<ChartTemplate> all, bool includeBase, string? activityKey,
        IEnumerable<string> modules, CancellationToken ct)
    {
        var active = all.Where(t => t.IsActive).ToList();
        var selected = new List<ChartTemplate>();
        if (includeBase) selected.AddRange(active.Where(t => t.Kind == ChartTemplateKind.Base).OrderBy(t => t.SortOrder));

        if (!string.IsNullOrWhiteSpace(activityKey))
        {
            var chain = new List<string>();
            for (var key = activityKey; key != null && !chain.Contains(key) && chain.Count < 5;)
            {
                chain.Insert(0, key);
                key = (await ActivityAsync(catalog, key, ct))?.ParentKey;
            }
            foreach (var key in chain)
                selected.AddRange(active.Where(t => t.Kind == ChartTemplateKind.Activity && t.ActivityKey == key).OrderBy(t => t.SortOrder));
        }

        var wanted = modules.Select(m => m.Trim().ToLowerInvariant()).ToHashSet();
        foreach (var module in PlatformModules.Licensable.Where(wanted.Contains))
            selected.AddRange(active.Where(t => t.Kind == ChartTemplateKind.Module && t.ModuleKey == module).OrderBy(t => t.SortOrder));
        return selected.Distinct().ToList();
    }

    public static string[] ParseCsv(string? csv)
        => string.IsNullOrWhiteSpace(csv) ? Array.Empty<string>() : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
