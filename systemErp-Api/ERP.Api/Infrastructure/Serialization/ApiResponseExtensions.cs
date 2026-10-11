using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ERP.Api.Infrastructure;

public static class ApiResponseExtensions
{
    public static ApiResponse<T> WithStatus<T>(this ApiResponse<T> r, int status) { r.StatusCode = status; return r; }
}
