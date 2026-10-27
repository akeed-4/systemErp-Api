using ERP.Service.Data;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ERP.Api.Infrastructure;

/// <summary>
/// نطاق الوصول لمستندات الـ controller (نوعها من <see cref="ScopedRecordTypes"/>): قوائم القراءة تُرشَّح بنطاق المستخدم،
/// وأي عملية على سجل محدد (مسار فيه {id}) تُرفض بـ 403 إن كان السجل خارج نطاقه. يعمل بعد سمات الصلاحية.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class ScopedRecordAttribute : Attribute, IAsyncActionFilter
{
    public string RecordType { get; }
    public ScopedRecordAttribute(string recordType) => RecordType = recordType;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var type = ScopedRecordTypes.Find(RecordType) ?? throw new InvalidOperationException($"Unknown scoped record type '{RecordType}'.");
        var services = context.HttpContext.RequestServices;
        var permissions = services.GetRequiredService<IPermissionService>();
        var ct = context.HttpContext.RequestAborted;

        if (context.RouteData.Values.TryGetValue("id", out var raw) && Guid.TryParse(raw?.ToString(), out var id))
            await permissions.EnsureRecordInScopeAsync(type.Key, id, ct);

        if (HttpMethods.IsGet(context.HttpContext.Request.Method))
        {
            var db = services.GetRequiredService<ErpDbContext>();
            db.ApplyDataScope(await permissions.GetDataScopeAsync(type.ScreenId, ct));
            // موقع الجلسة وإعداد العرض (حسب المعرض/حسب المستخدم) فوق نطاق الدور
            db.ApplyWarehouseScope();
        }

        await next();
    }
}
