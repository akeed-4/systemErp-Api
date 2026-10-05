using System.Security.Claims;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace ERP.Api.Infrastructure;

/// <summary>
/// يقصر الإجراء على مدراء المنصة (بريدهم ضمن Platform:AdminEmails). يُقرأ البريد من مطالبة الـ JWT ويُقارن بالإعدادات
/// عند كل طلب، فسحب الصلاحية يسري فوراً دون إعادة إصدار الرمز.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequirePlatformAdminAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var options = context.HttpContext.RequestServices.GetRequiredService<IOptionsMonitor<PlatformOptions>>().CurrentValue;
        var email = context.HttpContext.User.FindFirstValue("email") ?? context.HttpContext.User.FindFirstValue(ClaimTypes.Email);
        if (!options.IsAdmin(email)) throw new ForbiddenException();
    }
}
