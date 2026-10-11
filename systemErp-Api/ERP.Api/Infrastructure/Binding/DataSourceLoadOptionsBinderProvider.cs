using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ERP.Api.Infrastructure;

public class DataSourceLoadOptionsBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
        => context.Metadata.ModelType == typeof(DataSourceLoadOptions) ? new DataSourceLoadOptionsBinder() : null;
}
