namespace Erp.SharedKernel.Domain;

/// <summary>
/// Marks tenant-owned rows. Kept in shared AND dedicated databases so a tenant can move between them
/// without schema changes. The global query filter and the SaveChanges guard both key on it.
/// </summary>
public interface ITenantScoped
{
    Guid TenantId { get; }

    void StampTenant(Guid tenantId);
}

public interface ISoftDeletable
{
    bool IsDeleted { get; }
}

/// <summary>
/// Platform-owned reference rows (roles, screens) copied read-only from the catalog into every tenant database.
/// They carry no TenantId and are exempt from the tenant filter.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GlobalReferenceDataAttribute : Attribute;
