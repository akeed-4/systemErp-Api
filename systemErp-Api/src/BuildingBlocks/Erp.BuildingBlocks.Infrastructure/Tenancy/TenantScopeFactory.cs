using Erp.BuildingBlocks.Infrastructure.Schema;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Catalog.Contracts;
using Erp.SharedKernel.Errors;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.BuildingBlocks.Infrastructure.Tenancy;

/// <summary>
/// Opens a fresh DI scope bound to one tenant (or to one database for platform work).
/// Used whenever work must run for a tenant other than the current request's: anonymous auth flows,
/// tenant switch, outbox handlers, provisioning.
/// </summary>
public interface ITenantScopeFactory
{
    /// <param name="requireServable">When true, the tenant must be active and on the current schema version.</param>
    Task<AsyncServiceScope> CreateForTenantAsync(Guid tenantId, bool requireServable, CancellationToken cancellationToken);

    AsyncServiceScope CreateForTenant(TenantDescriptor tenant);

    /// <summary>A platform scope on a database with no tenant: the tenant filter is bypassed (audited).</summary>
    AsyncServiceScope CreateForDatabase(string connectionString, string reason);
}

internal sealed class TenantScopeFactory(
    IServiceScopeFactory scopeFactory,
    ITenantDirectory directory,
    ITenantConnectionFactory connections,
    ISchemaVersionProvider schemaVersions) : ITenantScopeFactory
{
    public async Task<AsyncServiceScope> CreateForTenantAsync(Guid tenantId, bool requireServable, CancellationToken cancellationToken)
    {
        var tenant = await directory.FindAsync(tenantId, cancellationToken);
        if (requireServable)
        {
            TenantAccessGuard.EnsureServable(tenant, schemaVersions.ExpectedVersion);
        }
        else if (tenant is null)
        {
            throw TenantAccessGuard.UnknownTenant();
        }

        return CreateForTenant(tenant!);
    }

    public AsyncServiceScope CreateForTenant(TenantDescriptor tenant)
    {
        var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>()
            .Activate(tenant, connections.GetConnectionString(tenant));
        return scope;
    }

    public AsyncServiceScope CreateForDatabase(string connectionString, string reason)
    {
        var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantDbConnection>().UseDatabase(connectionString);
        scope.ServiceProvider.GetRequiredService<IPlatformScope>().Enter(reason);
        return scope;
    }
}

public static class TenantAccessGuard
{
    public static void EnsureServable(TenantDescriptor? tenant, string expectedSchemaVersion)
    {
        if (tenant is null)
        {
            throw UnknownTenant();
        }

        if (tenant.Status != TenantStatus.Active)
        {
            throw new ErpException(
                "tenant_not_active",
                403,
                $"The company account is {tenant.Status.ToString().ToLowerInvariant()} and cannot be used right now.",
                "حساب المنشأة غير مفعّل حالياً ولا يمكن استخدامه.");
        }

        if (!string.Equals(tenant.SchemaVersion, expectedSchemaVersion, StringComparison.Ordinal))
        {
            throw new ErpException(
                "tenant_schema_outdated",
                503,
                "The company database is being upgraded. Please try again shortly.",
                "قاعدة بيانات المنشأة قيد التحديث، يرجى المحاولة بعد قليل.");
        }
    }

    public static ErpException UnknownTenant() =>
        new("tenant_unknown", 403, "The company account does not exist.", "حساب المنشأة غير موجود.");
}
