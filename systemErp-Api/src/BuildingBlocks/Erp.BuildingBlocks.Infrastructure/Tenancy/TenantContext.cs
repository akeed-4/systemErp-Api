using Erp.Catalog.Contracts;
using Erp.SharedKernel.Tenancy;

namespace Erp.BuildingBlocks.Infrastructure.Tenancy;

/// <summary>
/// Scoped holder of the current tenant. It can be activated once per scope; activating a different tenant
/// afterwards throws, so a scope (and its unit of work) never spans two tenants.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    private TenantDescriptor? _tenant;
    private string? _connectionString;

    public bool IsResolved => _tenant is not null;

    public TenantDescriptor Descriptor => _tenant ?? throw new TenantNotResolvedException();

    public Guid TenantId => Descriptor.Id;

    public string TenantCode => Descriptor.Code;

    public TenancyMode Mode => Descriptor.Mode;

    public string ConnectionString => _connectionString ?? throw new TenantNotResolvedException();

    public void Activate(TenantDescriptor tenant, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        if (_tenant is not null && _tenant.Id != tenant.Id)
        {
            throw new TenantIsolationViolationException(
                $"This scope already belongs to tenant {_tenant.Code}; it cannot switch to tenant {tenant.Code}.");
        }

        _tenant = tenant;
        _connectionString = connectionString;
    }
}
