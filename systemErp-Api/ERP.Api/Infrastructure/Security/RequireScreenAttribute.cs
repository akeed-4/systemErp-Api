using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ERP.Api.Infrastructure;

/// <summary>
/// صلاحية شاشة (مثل sales/view). إن لم يُحدَّد الإجراء يُستنتج من طريقة HTTP:
/// GET=view، POST=create، PUT/PATCH=edit، DELETE=delete. تُوضع على الـ controller أو الإجراء.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequireScreenAttribute : Attribute, IAsyncAuthorizationFilter
{
    public string ScreenId { get; }
    private readonly ScreenAction? _action;

    public RequireScreenAttribute(string screenId) => ScreenId = screenId;
    public RequireScreenAttribute(string screenId, ScreenAction action) : this(screenId) => _action = action;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any()) return;

        // عند وجود سمة على الإجراء تتقدّم على سمة الـ controller.
        var all = context.ActionDescriptor.EndpointMetadata.OfType<RequireScreenAttribute>().ToList();
        if (all.Count > 1 && !ReferenceEquals(all.Last(), this)) return;

        var action = _action ?? context.HttpContext.Request.Method switch
        {
            "GET" or "HEAD" or "OPTIONS" => ScreenAction.View,
            "POST" => ScreenAction.Create,
            "PUT" or "PATCH" => ScreenAction.Edit,
            "DELETE" => ScreenAction.Delete,
            _ => ScreenAction.View,
        };

        var permissions = context.HttpContext.RequestServices.GetRequiredService<IPermissionService>();
        if (!await permissions.HasPermissionAsync(ScreenId, action, context.HttpContext.RequestAborted))
            throw new ForbiddenException();
    }
}
