using Microsoft.AspNetCore.Http;
using ERP.Application.Interfaces;

namespace ERP.Infrastructure.MultiTenancy;

public class TenantService : ITenantService
{
    /// <summary>
    /// معرّف المستأجر التجريبي الافتراضي المستخدم عند عدم توفر مصادقة حقيقية بعد (المرحلة صفر).
    /// يُستبدل بربط المستخدم الحالي بمستأجره الفعلي عند بناء نظام المصادقة الحقيقي.
    /// </summary>
    public static readonly Guid DemoTenantId = new("11111111-1111-1111-1111-111111111111");

    public Guid? CurrentTenantId { get; private set; } = DemoTenantId;

    public void SetCurrentTenant(Guid tenantId)
    {
        CurrentTenantId = tenantId;
    }
}

public class TenantResolverMiddleware
{
    private readonly RequestDelegate _next;
    public const string TenantHeaderKey = "X-Tenant-Id";

    public TenantResolverMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantService tenantService)
    {
        if (context.Request.Headers.TryGetValue(TenantHeaderKey, out var tenantHeaderValues))
        {
            var tenantIdRaw = tenantHeaderValues.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(tenantIdRaw) && Guid.TryParse(tenantIdRaw.Trim(), out var tenantId))
            {
                tenantService.SetCurrentTenant(tenantId);
            }
        }

        await _next(context);
    }
}
