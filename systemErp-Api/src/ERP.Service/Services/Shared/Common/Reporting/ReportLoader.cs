using System.Collections;
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Data.ResponseModel;

namespace ERP.Service.Services.Shared;

/// <summary>
/// يطبّق خيارات DevExtreme (فلترة، فرز، تجميع، ملخصات، ترقيم) على صفوف تقرير. كل تقارير النظام تمرّ من هنا
/// فتتصرّف بشكل موحّد: النتيجة كائن LoadResult (data / totalCount / summary / groupCount) الذي يتوقعه CustomStore.
/// المصدر IQueryable من EF يُنفَّذ كاملًا في قاعدة البيانات (WHERE / ORDER BY / OFFSET-FETCH / COUNT / SUM)،
/// أما IEnumerable فللتقارير التي تُحسب في الذاكرة بعد تجميع مسبق (مثل الأرصدة الجارية).
/// </summary>
public static class ReportLoader
{
    /// <summary>
    /// حدّ أعلى لعدد الصفوف في الرد الواحد حتى لو لم يطلب العميل ترقيمًا، حماية لذاكرة الخادم والمتصفح.
    /// totalCount يبقى العدد الحقيقي فيعرف العميل أن النتيجة مقطوعة.
    /// </summary>
    public const int MaxTake = 5000;

    /// <summary>
    /// مصدر EF: كل شيء يُترجم إلى SQL ويُنفَّذ بشكل غير متزامن.
    /// <paramref name="defaultSort"/> = الترتيب الافتراضي للتقرير عند عدم طلب فرز ("-Field" تنازلي). لازم لأن DevExtreme
    /// يضيف عند الترقيم بلا فرز ترتيبًا ضمنيًا خاصًا به (أول عضو) فيُسقط OrderBy الاستعلام ويصبح الترتيب عشوائيًا.
    /// </summary>
    public static async Task<LoadResult> LoadAsync<T>(IQueryable<T> source, DataSourceLoadOptions? options, CancellationToken ct = default, params string[] defaultSort)
    {
        options = Normalize(options);
        if ((options.Sort == null || options.Sort.Length == 0) && defaultSort.Length > 0)
            options.Sort = defaultSort.Select(f => new SortingInfo { Selector = f.TrimStart('-'), Desc = f.StartsWith('-') }).ToArray();
        ct.ThrowIfCancellationRequested();
        try
        {
            var result = await DataSourceLoader.LoadAsync(source, options, ct);
            Materialize(result);
            return result;
        }
        catch (Exception ex) when (IsBadOptions(ex))
        {
            throw BadOptions(ex);
        }
    }

    /// <summary>صفوف محسوبة مسبقًا في الذاكرة (مزوّد LINQ-to-Objects لا يدعم التنفيذ غير المتزامن).</summary>
    public static Task<LoadResult> LoadAsync<T>(IEnumerable<T> rows, DataSourceLoadOptions? options, CancellationToken ct = default)
    {
        options = Normalize(options);
        ct.ThrowIfCancellationRequested();
        try
        {
            var result = DataSourceLoader.Load(rows.AsQueryable(), options);
            Materialize(result);
            return Task.FromResult(result);
        }
        catch (Exception ex) when (IsBadOptions(ex))
        {
            throw BadOptions(ex);
        }
    }

    // فلتر/فرز على حقل غير موجود أو بقيمة غير صالحة → خطأ طلب (400) بدل 500
    private static bool IsBadOptions(Exception ex)
        => ex is ArgumentException or InvalidOperationException or FormatException or InvalidCastException;

    private static ValidationFailedException BadOptions(Exception ex)
        => new($"خيارات التحميل غير صالحة: {ex.Message}");

    /// <summary>يحوّل data إلى قائمة ثابتة حتى يمكن تعديل صفوف الصفحة (مثل إثراء حقول نصية) دون إعادة تنفيذ الاستعلام.</summary>
    public static IList Materialize(LoadResult result)
    {
        if (result.data is IList list) return list;
        var materialized = (result.data ?? Array.Empty<object>()).Cast<object>().ToList();
        result.data = materialized;
        return materialized;
    }

    private static DataSourceLoadOptions Normalize(DataSourceLoadOptions? options)
    {
        options ??= new DataSourceLoadOptions();
        options.Skip = Math.Max(0, options.Skip);
        if (options.Take <= 0)
        {
            // طلب بلا ترقيم: نقطع عند الحد ونُرجع العدد الحقيقي ليعرف العميل إن كانت النتيجة ناقصة
            options.Take = MaxTake;
            options.RequireTotalCount = true;
        }
        else options.Take = Math.Min(options.Take, MaxTake);
        return options;
    }
}
