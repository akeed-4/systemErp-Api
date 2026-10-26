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
/// <see cref="Legacy"/>: العمود الذي كان يحمي النقطة قبل الإجراءات المستقلة؛ يُفحص بدل الإجراء ما دامت صلاحيات الشاشة
/// محفوظة بالأعمدة الخمسة (لم تُحفظ من المحرر الجديد)، فلا يتغير سلوك البيانات القائمة عند فصل الإجراء.
/// </summary>
/// <summary>إجراء لأي مستخدم مسجّل (صلاحياته هو، الكتالوج): يتجاوز سمة الشاشة على الـ controller، ويبقى [Authorize].</summary>
[AttributeUsage(AttributeTargets.Method)]
public class AuthenticatedOnlyAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequireScreenAttribute : Attribute, IAsyncAuthorizationFilter
{
    public string ScreenId { get; }
    private readonly ScreenAction? _action;
    private ScreenAction? _legacy;

    public RequireScreenAttribute(string screenId) => ScreenId = screenId;
    public RequireScreenAttribute(string screenId, ScreenAction action) : this(screenId) => _action = action;

    public ScreenAction Legacy { get => _legacy ?? ScreenAction.View; set => _legacy = value; }

    /// <summary>الإجراء الصريح إن حُدِّد (لكتالوج نقاط الـ API).</summary>
    public ScreenAction? Action => _action;

    public static ScreenAction FromMethod(string method) => method switch
    {
        "GET" or "HEAD" or "OPTIONS" => ScreenAction.View,
        "POST" => ScreenAction.Create,
        "PUT" or "PATCH" => ScreenAction.Edit,
        "DELETE" => ScreenAction.Delete,
        _ => ScreenAction.View,
    };

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any()) return;
        if (context.ActionDescriptor.EndpointMetadata.OfType<AuthenticatedOnlyAttribute>().Any()) return;

        // عند وجود سمة على الإجراء تتقدّم على سمة الـ controller.
        var all = context.ActionDescriptor.EndpointMetadata.OfType<RequireScreenAttribute>().ToList();
        if (all.Count > 1 && !ReferenceEquals(all.Last(), this)) return;

        var action = _action ?? FromMethod(context.HttpContext.Request.Method);
        var permissions = context.HttpContext.RequestServices.GetRequiredService<IPermissionService>();
        if (!await permissions.HasPermissionAsync(ScreenId, action, _legacy, context.HttpContext.RequestAborted))
            throw new ForbiddenException();
    }
}
