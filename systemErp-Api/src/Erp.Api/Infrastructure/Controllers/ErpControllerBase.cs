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
}
