using System.Diagnostics;
using Erp.SharedKernel.Errors;
using Erp.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Erp.BuildingBlocks.Web;

/// <summary>Turns every exception into ApiResponse + RFC 7807 (English and Arabic). Unknown errors never leak details.</summary>
internal sealed class ErpExceptionHandler(ILogger<ErpExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var error = exception switch
        {
            ErpException erp => erp,
            TenantIsolationViolationException => new ErpException(
                "tenant_isolation_violation", 403, "Access to another company's data is not allowed.", "لا يسمح بالوصول إلى بيانات منشأة أخرى."),
            TenantNotResolvedException => new ErpException(
                "tenant_not_resolved", 401, "Sign in to continue.", "يرجى تسجيل الدخول للمتابعة."),
            DbUpdateConcurrencyException => new ErpException(
                "concurrency_conflict", 409, "The record was changed by someone else. Reload and try again.", "تم تعديل السجل من مستخدم آخر، يرجى إعادة التحميل والمحاولة مجدداً."),
            BadHttpRequestException bad => new ErpException(
                "bad_request", bad.StatusCode, "The request is not valid.", "الطلب غير صالح."),
            _ => null,
        };

        if (error is null)
        {
            logger.LogError(exception, "Unhandled exception");
            error = new ErpException("server_error", 500, "An unexpected error occurred.", "حدث خطأ غير متوقع.");
        }
        else if (exception is TenantIsolationViolationException)
        {
            logger.LogWarning(exception, "Tenant isolation violation blocked");
        }

        await ErpResults.WriteErrorAsync(httpContext, error);
        return true;
    }
}

/// <summary>
/// Turns expected business errors (ErpException) thrown by endpoints into the error response directly, so they are not
/// logged as unhandled exceptions. Anything else still reaches <see cref="ErpExceptionHandler"/>.
/// </summary>
internal sealed class ErpExceptionEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (ErpException ex)
        {
            return new ErrorResult(ex);
        }
    }

    private sealed class ErrorResult(ErpException error) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext) => ErpResults.WriteErrorAsync(httpContext, error);
    }
}

public static class ErpResults
{
    public static IResult Ok<T>(T data, string? message = null) => Results.Json(ApiResponse.Ok(data, message));

    public static IResult Created<T>(string location, T data, string? message = null) =>
        new LocatedResult(Results.Json(ApiResponse.Ok(data, message, 201), statusCode: 201), location);

    public static Task WriteErrorAsync(HttpContext httpContext, ErpException error)
    {
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        var problem = new ErpProblem(
            Type: $"https://errors.systemerp.sa/{error.Code}",
            Title: ReasonTitle(error.Status),
            TitleAr: ReasonTitleAr(error.Status),
            Status: error.Status,
            Detail: error.Message,
            DetailAr: error.MessageAr,
            Code: error.Code,
            Instance: httpContext.Request.Path,
            TraceId: traceId,
            Errors: error.FieldErrors);

        var body = new ApiResponse<object>
        {
            Success = false,
            Message = error.MessageAr,
            Errors = error.FieldErrors?.SelectMany(p => p.Value.Select(v => $"{p.Key}: {v}")).ToList() ?? [error.Message],
            StatusCode = error.Status,
            Problem = problem,
        };

        httpContext.Response.StatusCode = error.Status;
        return httpContext.Response.WriteAsJsonAsync(body);
    }

    private static string ReasonTitle(int status) => status switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        503 => "Service Unavailable",
        _ => "Server Error",
    };

    private static string ReasonTitleAr(int status) => status switch
    {
        400 => "طلب غير صالح",
        401 => "غير مصرح",
        403 => "ممنوع",
        404 => "غير موجود",
        409 => "تعارض",
        503 => "الخدمة غير متاحة مؤقتاً",
        _ => "خطأ في الخادم",
    };

    private sealed class LocatedResult(IResult inner, string location) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.Location = location;
            return inner.ExecuteAsync(httpContext);
        }
    }
}
