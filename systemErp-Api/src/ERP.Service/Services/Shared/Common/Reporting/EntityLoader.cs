using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Data.ResponseModel;

namespace ERP.Service.Services.Shared;

/// <summary>
/// يطبّق خيارات DevExtreme على استعلام قاعدة البيانات نفسه (EF): الفلترة والفرز والترقيم والملخصات تُنفَّذ في SQL،
/// ولا تُحمَّل إلا صفوف الصفحة المطلوبة. أسماء الحقول في filter/sort هي خصائص مصدر الاستعلام (بلا حساسية لحالة الأحرف).
/// النتيجة كائن LoadResult (data / totalCount / summary) الذي يتوقعه CustomStore، كما في <see cref="ReportLoader"/>.
/// </summary>
public static class EntityLoader
{
    /// <summary>
    /// قائمة كيانات تُحوَّل صفحتها إلى DTO بعد التحميل (مع بيانات إضافية للصفحة إن لزم). select غير مدعوم لأن الناتج
    /// يبقى DTO كاملاً بنفس شكل القائمة العادية، والتجميع للمجموعات المطويّة فقط.
    /// </summary>
    public static async Task<LoadResult> LoadAsync<TEntity, TDto>(IQueryable<TEntity> query, DataSourceLoadOptions? options,
        Func<List<TEntity>, CancellationToken, Task<List<TDto>>> map, CancellationToken ct = default, params SortingInfo[] defaultSort)
        where TEntity : BaseEntity
    {
        options = Prepare(options, new[] { nameof(BaseEntity.Id) }, defaultSort);
        options.Select = null;
        // مجموعات مطويّة فقط (مفاتيح وأعداد، كما يطلبها فلتر رأس العمود ولوحة التجميع)؛ المجموعة المفتوحة تحمل كيانات خاماً لا DTO
        if (options.Group != null && options.Group.Any(g => g.IsExpanded != false)) { options.Group = null; options.GroupSummary = null; }
        var result = await RunAsync(query, options, ct);
        if (result.data is IEnumerable<TEntity> page) result.data = await map(page.ToList(), ct);
        return result;
    }

    public static Task<LoadResult> LoadAsync<TEntity, TDto>(IQueryable<TEntity> query, DataSourceLoadOptions? options,
        Func<TEntity, TDto> map, CancellationToken ct = default, params SortingInfo[] defaultSort)
        where TEntity : BaseEntity
        => LoadAsync(query, options, (page, _) => Task.FromResult(page.Select(map).ToList()), ct, defaultSort);

    /// <summary>استعلام مُسقَط على صفوف عرض جاهزة (مثل أسطر دفتر اليومية): الخيارات تُطبَّق على حقول الصف نفسها.</summary>
    public static Task<LoadResult> LoadRowsAsync<TRow>(IQueryable<TRow> rows, DataSourceLoadOptions? options, string primaryKey,
        CancellationToken ct = default, params SortingInfo[] defaultSort)
        => RunAsync(rows, Prepare(options, new[] { primaryKey }, defaultSort), ct);

    public static SortingInfo Desc(string selector) => new() { Selector = selector, Desc = true };
    public static SortingInfo Asc(string selector) => new() { Selector = selector, Desc = false };

    private static DataSourceLoadOptions Prepare(DataSourceLoadOptions? options, string[] primaryKey, SortingInfo[] defaultSort)
    {
        options ??= new DataSourceLoadOptions();
        options.PrimaryKey = primaryKey; // فرز ثانوي ثابت حتى لا تتكرر الصفوف أو تسقط بين الصفحات
        if (options.Sort == null || options.Sort.Length == 0) options.Sort = defaultSort;
        options.Take = options.Take <= 0 ? PaginationParams.MaxPageSize : Math.Min(options.Take, PaginationParams.MaxPageSize);
        return options;
    }

    private static async Task<LoadResult> RunAsync<T>(IQueryable<T> query, DataSourceLoadOptions options, CancellationToken ct)
    {
        try
        {
            return await DataSourceLoader.LoadAsync(query, options, ct);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or InvalidCastException)
        {
            // فلتر/فرز على حقل غير موجود أو غير قابل للترجمة إلى SQL أو بقيمة غير صالحة → خطأ طلب (400) بدل 500
            throw new ValidationFailedException(string.Format(Messages.InvalidLoadOptions, ex.Message));
        }
    }
}
