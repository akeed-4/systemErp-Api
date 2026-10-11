using Microsoft.AspNetCore.Authorization;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ERP.Api.Infrastructure;

/// <summary>
/// بوابة الاشتراك (فلتر عام): تمنع المنشأة من استعمال النظام خارج ما اشتركت فيه.
/// - موقوف/ملغى: كل شيء ممنوع عدا الدخول والاشتراك والدفع والدعم.
/// - انتهت الفترة التجريبية المجانية بلا سداد: كل شيء ممنوع (كالموقوف) حتى يُسدَّد الاشتراك.
/// - اشتراك مدفوع منتهٍ: قراءة فقط (GET) حتى تجدّد الاشتراك.
/// - الوحدات: ما يحمل <see cref="RequireModuleAttribute"/> يتطلب أن يشمل الاشتراك إحدى وحداته؛ وما لا يحملها مشترك.
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
        if (info.TrialEnded) throw new ForbiddenException(Messages.TrialEndedPayToContinue);

        var method = context.HttpContext.Request.Method;
        var readOnly = method is "GET" or "HEAD" or "OPTIONS";
        if (info.Status == SubscriptionStatus.Expired && !readOnly)
            throw new ForbiddenException("انتهى الاشتراك. جدّده لمتابعة الإضافة والتعديل (القراءة متاحة).");

        // سمة الإجراء (الأخيرة في الـ metadata) تتقدّم على سمة الـ controller
        var required = context.ActionDescriptor.EndpointMetadata.OfType<RequireModuleAttribute>().LastOrDefault();
        if (required == null || required.AnyOf.Any(info.Allows)) return;
        throw new ForbiddenException($"{string.Join(" / ", required.AnyOf.Select(ModuleName))}: غير مشمولة في اشتراكك.");
    }

    private static string ModuleName(string module) => module switch
    {
        PlatformModules.Accounting => "وحدة التجارة العامة",
        PlatformModules.CarShowroom => "وحدة معارض السيارات",
        PlatformModules.Pos => "وحدة نقاط البيع",
        PlatformModules.Hr => "وحدة شؤون الموظفين",
        _ => module,
    };
}
