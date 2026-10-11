using Microsoft.AspNetCore.Mvc.Filters;

namespace ERP.Api.Infrastructure;

/// <summary>
/// يقصر الإجراء على مدراء المنصة: بريدهم ضمن Platform:AdminEmails، أو مُنحوا صلاحية إدارة المنصة من قائمة الصلاحيات.
/// القراءة (GET) تكفيها صلاحية العرض، وأي تعديل يتطلب صلاحية التعديل. تُحسب عند كل طلب، فسحب الصلاحية يسري فوراً
/// دون إعادة إصدار الرمز.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequirePlatformAdminAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        var access = await http.RequestServices.GetRequiredService<IPlatformAccessService>().GetAsync(http.RequestAborted);
        var readOnly = http.Request.Method is "GET" or "HEAD" or "OPTIONS";
        if (!(readOnly ? access.IsPlatformAdmin : access.CanManage)) throw new ForbiddenException();
    }
}
