using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ERP.Api.Infrastructure;

[ApiController]
[Authorize]
public abstract class ErpControllerBase : ControllerBase
{
    protected OkObjectResult Success<T>(T data, string? message = null) => Ok(ApiResponse<T>.Ok(data, message));
    protected OkObjectResult Success(string message) => Ok(ApiResponse<object>.Ok(new { }, message));

    /// <summary>
    /// بوابة الاعتماد: إن انطبقت سياسة اعتماد على العملية تُحجز بطلب اعتماد وتُعاد 202 بالطلب (لا تُنفَّذ إلا عند اعتمادها النهائي)؛
    /// وإلا null فيكمل الإجراء التنفيذ. يُستدعى بعد فحص صلاحية الإجراء حتى لا يحجز عمليةً من لا يملكها.
    /// </summary>
    protected async Task<IActionResult?> HoldForApprovalAsync(string handler, string action, Guid? documentId, object? payload, CancellationToken ct)
    {
        var gate = HttpContext.RequestServices.GetRequiredService<IApprovalGate>();
        var request = await gate.TryHoldAsync(handler, action, documentId, payload, ct);
        if (request == null) return null;
        return StatusCode(StatusCodes.Status202Accepted,
            ApiResponse<ApprovalRequestDto>.Ok(request, string.Format(Messages.OperationSentForApproval, request.PolicyNameAr)).WithStatus(202));
    }
}
