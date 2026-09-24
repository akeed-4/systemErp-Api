using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Data.ResponseModel;

namespace ERP.Service.Services.Shared;

/// <summary>
/// يطبّق خيارات DevExtreme (فلترة، فرز، تجميع، ملخصات، ترقيم) على صفوف تقرير. كل تقارير النظام تمرّ من هنا
/// فتتصرّف بشكل موحّد: النتيجة كائن LoadResult (data / totalCount / summary / groupCount) الذي يتوقعه CustomStore.
/// </summary>
public static class ReportLoader
{
    public static Task<LoadResult> LoadAsync<T>(IEnumerable<T> rows, DataSourceLoadOptions? options, CancellationToken ct = default)
    {
        options ??= new DataSourceLoadOptions();
        ct.ThrowIfCancellationRequested();
        try
        {
            return Task.FromResult(DataSourceLoader.Load(rows.AsQueryable(), options));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or InvalidCastException)
        {
            // فلتر/فرز على حقل غير موجود أو بقيمة غير صالحة → خطأ طلب (400) بدل 500
            throw new ValidationFailedException($"خيارات التحميل غير صالحة: {ex.Message}");
        }
    }
}
