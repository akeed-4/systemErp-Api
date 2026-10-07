using Microsoft.AspNetCore.Authorization;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ERP.Api.Infrastructure;

/// <summary>
/// بوابة الاشتراك (فلتر عام): تمنع المنشأة من استعمال النظام خارج ما اشتركت فيه.
/// - موقوف/ملغى: كل شيء ممنوع عدا الدخول والاشتراك والدفع والدعم.
/// - منتهي: قراءة فقط (GET) حتى تجدّد الاشتراك.
/// - الوحدات: معارض السيارات تتطلب وحدة car_showroom، ونقطة البيع تتطلب وحدة accounting.
/// تُقرأ الحالة من قاعدة البيانات عند كل طلب، فتغييرات مدير المنصة تسري فوراً.
/// </summary>
public class SubscriptionGateFilter : IAsyncAuthorizationFilter
{
    private static readonly HashSet<string> AlwaysAllowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "Auth", "Subscriptions", "Platform", "Payments", "Company", "SupportTickets", "Notifications", "Permissions",
    };

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true) return;
        if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any()) return;
        if (context.ActionDescriptor is not ControllerActionDescriptor cad) return;
        if (AlwaysAllowed.Contains(cad.ControllerName)) return;

        var info = await context.HttpContext.RequestServices.GetRequiredService<ISubscriptionEntitlements>()
            .GetAsync(context.HttpContext.RequestAborted);

        if (!info.HasSubscription) throw new ForbiddenException("لا يوجد اشتراك فعّال لهذه المنشأة.");
        if (info.Status == SubscriptionStatus.Suspended) throw new ForbiddenException("اشتراك المنشأة موقوف. تواصل مع إدارة المنصة.");
        if (info.Status == SubscriptionStatus.Cancelled) throw new ForbiddenException("اشتراك المنشأة ملغى. تواصل مع إدارة المنصة.");

        var method = context.HttpContext.Request.Method;
        var readOnly = method is "GET" or "HEAD" or "OPTIONS";
        if (info.Status == SubscriptionStatus.Expired && !readOnly)
            throw new ForbiddenException("انتهى الاشتراك. جدّده لمتابعة الإضافة والتعديل (القراءة متاحة).");

        var ns = cad.ControllerTypeInfo.Namespace ?? string.Empty;
        if (ns.Contains(".CarShowroom") && !info.Allows(PlatformModules.CarShowroom))
            throw new ForbiddenException("وحدة معارض السيارات غير مشمولة في اشتراكك.");
        if (ns.Contains(".POS") && !info.Allows(PlatformModules.Accounting))
            throw new ForbiddenException("وحدة النظام المحاسبي (ونقطة البيع) غير مشمولة في اشتراكك.");
    }
}
