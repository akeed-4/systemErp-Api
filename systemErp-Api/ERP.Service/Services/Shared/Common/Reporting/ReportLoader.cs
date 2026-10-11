using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Data.ResponseModel;

namespace ERP.Service.Services.Shared;

/// <summary>
/// يطبّق خيارات DevExtreme (فلترة، فرز، تجميع، ملخصات، ترقيم) على استعلام التقرير نفسه، فتُنفَّذ كلها في SQL ولا تُحمَّل
/// إلا الصفوف المطلوبة. كل تقارير النظام تمرّ من هنا فتتصرّف بشكل موحّد: النتيجة كائن LoadResult
/// (data / totalCount / summary / groupCount) الذي يتوقعه CustomStore. بخلاف <see cref="EntityLoader"/> لا يُفرض حدّ للصفحة:
/// التقرير بلا take يعود كاملاً.
/// </summary>
public static class ReportLoader
{
    public static async Task<LoadResult> LoadAsync<T>(IQueryable<T> rows, DataSourceLoadOptions? options, string primaryKey,
        CancellationToken ct = default, params SortingInfo[] defaultSort)
    {
        options ??= new DataSourceLoadOptions();
        options.PrimaryKey = new[] { primaryKey }; // فرز ثانوي ثابت حتى لا تتكرر الصفوف أو تسقط بين الصفحات
        if (options.Sort == null || options.Sort.Length == 0) options.Sort = defaultSort;
        try
        {
            return await DataSourceLoader.LoadAsync(rows, options, ct);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or InvalidCastException)
        {
            // فلتر/فرز على حقل غير موجود أو غير قابل للترجمة إلى SQL أو بقيمة غير صالحة → خطأ طلب (400) بدل 500
            throw new ValidationFailedException(string.Format(Messages.InvalidLoadOptions, ex.Message));
        }
    }
}
