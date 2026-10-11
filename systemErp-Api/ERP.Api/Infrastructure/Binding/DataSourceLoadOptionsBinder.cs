using DevExtreme.AspNet.Data.Helpers;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ERP.Api.Infrastructure;

/// <summary>
/// يربط خيارات DevExtreme (filter, sort, group, skip, take, totalSummary, groupSummary, requireTotalCount, ...) من
/// الـ query string مباشرة إلى <see cref="DataSourceLoadOptions"/>، بالصيغة التي يرسلها CustomStore/DataSource.
/// </summary>
public class DataSourceLoadOptionsBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var options = new DataSourceLoadOptions();
        DataSourceLoadOptionsParser.Parse(options, key => bindingContext.ValueProvider.GetValue(key).FirstOrDefault());
        bindingContext.Result = ModelBindingResult.Success(options);
        return Task.CompletedTask;
    }
}
