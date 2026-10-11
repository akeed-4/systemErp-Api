using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ERP.Api.Infrastructure;

/// <summary>
/// يربط قيم Enum القادمة من الـ query string/route بصيغة snake_case نفسها المستخدمة في أجسام JSON (انظر JsonConfiguration):
/// [FromQuery] لا يمرّ عبر System.Text.Json فلا يرى JsonStringEnumConverter تلقائياً، فيرفض قيماً مثل "purchase_return"
/// رغم قبولها في جسم الطلب. يقبل أيضاً اسم العنصر الفعلي (PascalCase) أو رقمه حفاظاً على التوافق.
/// </summary>
public class SnakeCaseEnumModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        var type = Nullable.GetUnderlyingType(context.Metadata.ModelType) ?? context.Metadata.ModelType;
        return type.IsEnum ? new SnakeCaseEnumModelBinder(type) : null;
    }
}

public class SnakeCaseEnumModelBinder : IModelBinder
{
    private readonly Type _enumType;
    public SnakeCaseEnumModelBinder(Type enumType) => _enumType = enumType;

    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var result = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (result == ValueProviderResult.None || string.IsNullOrWhiteSpace(result.FirstValue))
            return Task.CompletedTask; // غير مُرسَل: يبقى القيمة الافتراضية (null لو Nullable<Enum>) كما في الربط الافتراضي

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, result);
        var raw = result.FirstValue!;

        var pascal = string.Concat(raw.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));

        if (Enum.TryParse(_enumType, pascal, ignoreCase: true, out var parsed) || Enum.TryParse(_enumType, raw, ignoreCase: true, out parsed))
        {
            bindingContext.Result = ModelBindingResult.Success(parsed);
        }
        else
        {
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, $"القيمة '{raw}' غير صالحة.");
            bindingContext.Result = ModelBindingResult.Failed();
        }
        return Task.CompletedTask;
    }
}
