using System.Security.Claims;
using Erp.SharedKernel.Errors;
using Erp.SharedKernel.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Erp.BuildingBlocks.Web;

public sealed class ScreenPermissionRequirement(string screenId, ScreenAction action) : IAuthorizationRequirement
{
    public string ScreenId { get; } = screenId;

    public ScreenAction Action { get; } = action;
}

/// <summary>Builds "Screen:{screenId}:{action}" policies on demand, e.g. Screen:master-data:create.</summary>
internal sealed class ScreenPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public const string Prefix = "Screen:";

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return await base.GetPolicyAsync(policyName);
        }

        var parts = policyName.Split(':');
        if (parts.Length != 3 || !Enum.TryParse<ScreenAction>(parts[2], ignoreCase: true, out var action))
        {
            return null;
        }

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new ScreenPermissionRequirement(parts[1], action))
            .Build();
    }
}

internal sealed class ScreenPermissionHandler(IScreenPermissionChecker checker, IHttpContextAccessor accessor)
    : AuthorizationHandler<ScreenPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ScreenPermissionRequirement requirement)
    {
        var cancellationToken = accessor.HttpContext?.RequestAborted ?? CancellationToken.None;
        if (await checker.IsAllowedAsync(requirement.ScreenId, requirement.Action, cancellationToken))
        {
            context.Succeed(requirement);
        }
    }
}

/// <summary>Writes 401/403 from the authorization pipeline in the same ApiResponse + RFC 7807 shape as other errors.</summary>
internal sealed class ErpAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            await ErpResults.WriteErrorAsync(context, ErpException.Unauthorized(
                "unauthenticated", "Sign in to continue.", "يرجى تسجيل الدخول للمتابعة."));
            return;
        }

        if (authorizeResult.Forbidden)
        {
            await ErpResults.WriteErrorAsync(context, ErpException.Forbidden(
                "permission_denied", "You do not have permission for this action.", "ليست لديك صلاحية لتنفيذ هذا الإجراء."));
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId => Guid.TryParse(Principal?.FindFirstValue(ErpClaims.UserId), out var id) ? id : null;

    public string? Role => Principal?.FindFirstValue(ErpClaims.Role);

    public string? Email => Principal?.FindFirstValue(ErpClaims.Email);

    public string? Name => Principal?.FindFirstValue(ErpClaims.Name);
}

public static class EndpointAuthorizationExtensions
{
    /// <summary>Requires the frontend screen permission (screenId × action) for this endpoint.</summary>
    public static TBuilder RequireScreen<TBuilder>(this TBuilder builder, string screenId, ScreenAction action)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization($"{ScreenPolicyProvider.Prefix}{screenId}:{action}");

    public static TBuilder RequireRoles<TBuilder>(this TBuilder builder, params string[] roles)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(p => p.RequireAuthenticatedUser().RequireClaim(ErpClaims.Role, roles));
}
