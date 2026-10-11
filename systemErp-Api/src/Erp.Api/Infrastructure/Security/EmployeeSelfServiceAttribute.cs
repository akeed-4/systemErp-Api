using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ERP.Api.Infrastructure;

/// <summary>
/// نقطة نهاية متاحة لدور «موظف» (بوابة الخدمة الذاتية). ما لا يحمل هذه السمة مغلق أمام هذا الدور كلياً،
/// يطبّق ذلك <see cref="EmployeeRoleGateFilter"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class EmployeeSelfServiceAttribute : Attribute
{
}

/// <summary>
/// بوابة دور الموظف (فلتر عام): حساب بدور «موظف» لا يصل إلا إلى ما يحمل <see cref="EmployeeSelfServiceAttribute"/>.
/// قائمة مسموحات لا ممنوعات: controller جديد يُغلق أمام الموظف تلقائياً ما لم يُفتح له صراحةً، حتى لو لم
/// يحمل RequireScreen.
/// </summary>
public class EmployeeRoleGateFilter : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true) return;
        if (user.FindFirst(CurrentUser.RoleIdClaim)?.Value != PermissionService.EmployeeRoleId) return;
        if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any()) return;
        if (context.ActionDescriptor.EndpointMetadata.OfType<EmployeeSelfServiceAttribute>().Any()) return;
        throw new ForbiddenException();
    }
}
