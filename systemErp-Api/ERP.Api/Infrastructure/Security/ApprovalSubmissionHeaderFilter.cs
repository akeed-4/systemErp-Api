using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ERP.Api.Infrastructure;

/// <summary>
/// فلتر عام: إن قدّم الطلب مستنداً للاعتماد ولا يملك المستخدم اعتماده، تُضاف للاستجابة الترويسة <see cref="HeaderName"/>
/// برسالة «تم الحفظ وأُرسل للاعتماد إلى: الأدوار...» (مُرمَّزة URL) فتعرضها الواجهة، دون تعديل أي endpoint.
/// </summary>
public class ApprovalSubmissionHeaderFilter : IAsyncResultFilter
{
    public const string HeaderName = "X-Approval-Submitted";

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        if (!HttpMethods.IsGet(context.HttpContext.Request.Method)
            && services.GetService<DocumentApprovalTracker>() is { Submitted.Count: > 0 })
        {
            try
            {
                var message = await services.GetRequiredService<IApprovalTrackingService>().SubmissionMessageAsync(context.HttpContext.RequestAborted);
                if (!string.IsNullOrEmpty(message) && !context.HttpContext.Response.HasStarted)
                    context.HttpContext.Response.Headers[HeaderName] = Uri.EscapeDataString(message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // الرسالة إضافية: لا تُفشل عملية نجحت وحُفظت
            }
        }
        await next();
    }
}
