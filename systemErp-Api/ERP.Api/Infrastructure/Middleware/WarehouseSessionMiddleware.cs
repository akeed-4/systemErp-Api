using ERP.Core.Contracts.Shared;

namespace ERP.Api.Infrastructure;

/// <summary>يضبط موقع الجلسة (المعرض المختار عند الدخول) لكل طلب مصادَق داخل منشأة، بعد تحديد المنشأة.</summary>
public class WarehouseSessionMiddleware
{
    private readonly RequestDelegate _next;
    public WarehouseSessionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITenantContext tenant, IWarehouseSessionService warehouses)
    {
        if (context.User.Identity?.IsAuthenticated == true && tenant.TenantId.HasValue)
            await warehouses.InitializeAsync(context.RequestAborted);
        await _next(context);
    }
}
