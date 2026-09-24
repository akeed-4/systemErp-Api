namespace Erp.SharedKernel.Tenancy;

public enum TenancyMode
{
    Shared,
    Dedicated,
}

/// <summary>
/// The tenant the current scope works for. Resolved from the JWT tenant_id claim for authenticated requests,
/// from the catalog login index for anonymous auth flows, or explicitly for background work.
/// </summary>
public interface ITenantContext
{
    bool IsResolved { get; }

    /// <summary>Throws <see cref="TenantNotResolvedException"/> when no tenant is resolved.</summary>
    Guid TenantId { get; }

    string TenantCode { get; }

    TenancyMode Mode { get; }

    /// <summary>The effective connection string of the tenant's database (shared or dedicated).</summary>
    string ConnectionString { get; }
}

public sealed class TenantNotResolvedException : InvalidOperationException
{
    public TenantNotResolvedException()
        : base("No tenant is resolved for the current scope.")
    {
    }
}

/// <summary>Raised whenever an operation would read or write another tenant's data, or mix two tenants in one unit of work.</summary>
public sealed class TenantIsolationViolationException : InvalidOperationException
{
    public TenantIsolationViolationException(string message)
        : base(message)
    {
    }
}
