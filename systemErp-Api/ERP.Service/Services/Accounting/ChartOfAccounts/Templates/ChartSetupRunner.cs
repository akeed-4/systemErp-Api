using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// مطابقة دليل منشأة قائمة مع اشتراكها ونشاطها (مشترك بين شاشة المنشأة ولوحة المنصة): الموديولات المرخّصة التي لم تُطبَّق قوالبها
/// تُطبَّق (الناقص فقط)، وقالب النشاط بطلب صريح. التطبيق على قاعدة المنشأة نفسها بعد حفظ الاشتراك في الدليل، فلا معاملة موزعة:
/// الفشل يُسجَّل والفرق يبقى ظاهراً فيُعاد بأمان.
/// </summary>
internal static class ChartSetupRunner
{
    public static async Task<ChartSetupReportDto> RunAsync(ErpDbContext db, string[] licensedModules, string? activityKey, bool includeActivity,
        bool preview, CancellationToken ct)
    {
        var log = await db.Set<ChartProvisioning>().AsNoTracking().FirstOrDefaultAsync(ct);
        var all = await ChartTemplateCatalog.LoadAsync(db.Catalog, ct);
        var licensed = licensedModules.Where(m => PlatformModules.Licensable.Contains(m)).ToArray();

        if (includeActivity)
        {
            var activity = await ChartTemplateCatalog.ResolveActivityKeyAsync(db.Catalog, activityKey ?? log?.ActivityKey, ct);
            var templates = await ChartTemplateCatalog.SelectAsync(db.Catalog, all, includeBase: true, activity, licensed, ct);
            var report = await ChartProvisioner.ApplyAsync(db, new ChartProvisioner.Request
            {
                Templates = templates, ActivityKey = activity, SetActivity = true, Modules = licensed, Preview = preview,
            }, ct);
            if (!preview && report.Complete)
            {
                var tenant = await db.Set<Tenant>().FirstAsync(ct);
                if (tenant.ActivityKey != activity) { tenant.ActivityKey = activity; await db.SaveChangesAsync(ct); }
            }
            return report;
        }

        // منشأة قديمة (بلا سجل تأسيس): دليلها كما هو حتى تختار نشاطها وتعتمد قالبه
        if (log == null) throw new ValidationFailedException(Messages.ChartSetupLegacyNeedsActivity);
        var pending = licensed.Except(ChartTemplateCatalog.ParseCsv(log.AppliedModules)).ToArray();
        var moduleTemplates = await ChartTemplateCatalog.SelectAsync(db.Catalog, all, includeBase: false, null, pending, ct);
        return await ChartProvisioner.ApplyAsync(db, new ChartProvisioner.Request
        {
            Templates = moduleTemplates, Modules = pending, Preview = preview,
        }, ct);
    }

    /// <summary>
    /// بعد تغيير موديولات الاشتراك من المنصة (والسياق موجَّه لقاعدة المنشأة): تطبيق الناقص في معاملة مستقلة؛ الفشل لا يُبطل تغيير
    /// الاشتراك المحفوظ، بل يُسجَّل في حالة التأسيس ويُعاد من اللوحة. المنشأة القديمة لا تُمس.
    /// </summary>
    public static async Task<ChartSetupReportDto?> SyncModulesAsync(ErpDbContext db, ITransactionRunner tx, string[] licensedModules, CancellationToken ct)
    {
        if (!await db.Set<ChartProvisioning>().AnyAsync(ct)) return null;
        try
        {
            var report = await tx.RunAsync(token => RunAsync(db, licensedModules, null, includeActivity: false, preview: false, token), ct);
            if (!report.Complete)
                await ChartProvisioner.RecordFailureAsync(db, string.Join(" | ", report.Errors.Concat(
                    report.Items.Where(i => i.Status == ChartSetupItemStatus.Problem && i.Message != null).Select(i => i.Message!))), ct);
            return report;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await ChartProvisioner.RecordFailureAsync(db, ex.Message, CancellationToken.None);
            return null;
        }
    }
}
