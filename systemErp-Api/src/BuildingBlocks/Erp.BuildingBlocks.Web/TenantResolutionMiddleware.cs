using System.Security.Claims;
using Erp.BuildingBlocks.Infrastructure.Schema;
using Erp.BuildingBlocks.Infrastructure.Tenancy;
using Erp.Catalog.Contracts;
using Erp.SharedKernel.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Erp.BuildingBlocks.Web;

public static class ErpClaims
{
    public const string TenantId = "tenant_id";
    public const string TenantCode = "tenant_code";
    public const string UserId = "sub";
    public const string Role = "role";
    public const string Email = "email";
    public const string Name = "name";
}

/// <summary>
/// Resolves the tenant of an authenticated request from the JWT tenant_id claim ONLY
/// (X-Tenant-Id, query strings and bodies are ignored), checks it is active and on the current schema,
/// then activates the scope's tenant context. Anonymous requests pass through untouched.
/// </summary>
internal sealed class TenantResolutionMiddleware(RequestDelegate next, ILogger<TenantResolutionMiddleware> logger)
{
    public async Task InvokeAsync(
        HttpContext context,
        ITenantDirectory directory,
        TenantContext tenantContext,
        ITenantConnectionFactory connections,
        ISchemaVersionProvider schemaVersions)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var claim = context.User.FindFirstValue(ErpClaims.TenantId);
        if (!Guid.TryParse(claim, out var tenantId))
        {
            await ErpResults.WriteErrorAsync(context, ErpException.Unauthorized(
                "tenant_claim_missing", "The access token has no company.", "رمز الدخول لا يحتوي على منشأة."));
            return;
        }

        var tenant = await directory.FindAsync(tenantId, context.RequestAborted);
        try
        {
            TenantAccessGuard.EnsureServable(tenant, schemaVersions.ExpectedVersion);
        }
        catch (ErpException ex)
        {
            await ErpResults.WriteErrorAsync(context, ex);
            return;
        }

        tenantContext.Activate(tenant!, connections.GetConnectionString(tenant!));

        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["TenantId"] = tenantId,
            ["TenantCode"] = tenant!.Code,
            ["UserId"] = context.User.FindFirstValue(ErpClaims.UserId),
        }))
        {
            await next(context);
        }
    }
}
