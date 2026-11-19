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
            await Write(context, 409, Messages.RecordModifiedByAnotherUser);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            // فهرس فريد سبقت إليه عملية متزامنة (قيد المستند، رقم الفاتورة...): المعاملة تراجعت كلها، فهو تعارض لا خطأ خادم
            _log.LogWarning(ex, "Unique constraint conflict for {Method} {Path}", context.Request.Method, context.Request.Path);
            await Write(context, 409, Messages.RecordModifiedByAnotherUser);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            _log.LogWarning(ex, "Foreign key violation for {Method} {Path}", context.Request.Method, context.Request.Path);
            await Write(context, 409, "لا يمكن إتمام العملية: السجل مرتبط بسجلات أخرى أو يشير إلى سجل غير موجود.",
                _env.IsDevelopment() ? new List<string> { ex.InnerException?.Message ?? ex.Message } : null);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = 499;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
            await Write(context, 500, Messages.UnexpectedError, _env.IsDevelopment() ? new List<string> { ex.Message } : null);
        }
    }

    /// <summary>SQL Server 547 = تعارض مع قيد FOREIGN KEY/REFERENCE.</summary>
    private static bool IsForeignKeyViolation(Microsoft.EntityFrameworkCore.DbUpdateException ex)
    {
        var text = ex.InnerException?.Message ?? ex.Message;
        return text.Contains("FOREIGN KEY constraint", StringComparison.OrdinalIgnoreCase)
            || text.Contains("REFERENCE constraint", StringComparison.OrdinalIgnoreCase);
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
