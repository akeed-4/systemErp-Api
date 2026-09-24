using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ERP.Api.Infrastructure;

/// <summary>يحوّل الاستثناءات إلى استجابة ApiResponse موحّدة (400/401/403/404/409/500) دون كشف تفاصيل داخلية في الإنتاج.</summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _log;
    private readonly IHostEnvironment _env;
    private readonly JsonSerializerOptions _json;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> log, IHostEnvironment env,
        Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> json)
    {
        _next = next; _log = log; _env = env; _json = json.Value.SerializerOptions;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ErpException ex)
        {
            var errors = ex is ValidationFailedException v ? v.Errors.ToList() : null;
            await Write(context, ex.StatusCode, ex.Message, errors);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            await Write(context, 409, "تم تعديل السجل من مستخدم آخر، أعد التحميل وحاول مجدداً.");
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = 499;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
            await Write(context, 500, "حدث خطأ غير متوقع.", _env.IsDevelopment() ? new List<string> { ex.Message } : null);
        }
    }

    private async Task Write(HttpContext context, int status, string message, List<string>? errors = null)
    {
        if (context.Response.HasStarted) return;
        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(JsonSerializer.Serialize(ApiResponse<object>.Fail(status, message, errors), _json));
    }
}
