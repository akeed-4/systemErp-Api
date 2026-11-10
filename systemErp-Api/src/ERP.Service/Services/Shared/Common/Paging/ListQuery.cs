using System.Linq.Expressions;
using ERP.Core.DTOs.Shared;

namespace ERP.Service.Services.Shared;

/// <summary>
/// أدوات مشتركة لقوائم PaginationParams المرقّمة في قاعدة البيانات: نهاية فترة شاملة، وفرز بقائمة أعمدة مسموحة.
/// </summary>
public static class ListQuery
{
    /// <summary>
    /// حدّ النهاية الحصري لمرشّح EndDate. التاريخ بلا وقت (00:00) يعني "حتى نهاية ذلك اليوم"،
    /// فتُطابَق المستندات المؤرخة في اليوم نفسه بأي ساعة. أما التاريخ بوقت صريح فيبقى شاملًا لتلك اللحظة.
    /// الاستخدام: <c>q.Where(x =&gt; x.Date &lt; end)</c>.
    /// </summary>
    public static DateTime? EndExclusive(PaginationParams p) => EndExclusive(p.EndDate);

    /// <summary>نفس قاعدة <see cref="EndExclusive(PaginationParams)"/> لمرشّحات DateTo في التقارير.</summary>
    public static DateTime? EndExclusive(DateTime? endDate)
        => endDate is not { } end ? null
            : end.TimeOfDay == TimeSpan.Zero ? end.Date.AddDays(1) : end.AddTicks(1);

    /// <summary>
    /// يطبّق SortBy/IsDescending من قائمة أعمدة مسموحة (بلا حساسية لحالة الأحرف)، ثم مفتاح ثابت لترقيم مستقر.
    /// SortBy فارغ = الترتيب الافتراضي للقائمة. عمود غير مسموح = خطأ تحقق (400) بدل تجاهل صامت.
    /// </summary>
    public static IOrderedQueryable<T> Sort<T>(
        IQueryable<T> source, PaginationParams p,
        IReadOnlyDictionary<string, Expression<Func<T, object>>> columns,
        Func<IQueryable<T>, IOrderedQueryable<T>> defaultOrder,
        Expression<Func<T, object>> tieBreaker)
    {
        if (string.IsNullOrWhiteSpace(p.SortBy)) return defaultOrder(source);
        var key = columns.Keys.FirstOrDefault(k => string.Equals(k, p.SortBy.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ValidationFailedException($"لا يمكن الفرز حسب الحقل '{p.SortBy}'. الحقول المتاحة: {string.Join(", ", columns.Keys)}.");
        var ordered = p.IsDescending ? source.OrderByDescending(columns[key]) : source.OrderBy(columns[key]);
        return p.IsDescending ? ordered.ThenByDescending(tieBreaker) : ordered.ThenBy(tieBreaker);
    }
}
