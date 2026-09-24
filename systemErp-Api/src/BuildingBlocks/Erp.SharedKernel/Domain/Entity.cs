namespace Erp.SharedKernel.Domain;

/// <summary>Base for every persisted entity. Ids are version-7 GUIDs so clustered inserts stay sequential.</summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();
}

/// <summary>Creation/update stamps, filled in by the persistence interceptor.</summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; }

    Guid? CreatedBy { get; }

    DateTimeOffset? UpdatedAt { get; }

    Guid? UpdatedBy { get; }

    void StampCreated(DateTimeOffset at, Guid? by);

    void StampUpdated(DateTimeOffset at, Guid? by);
}

public abstract class AuditableEntity : Entity, IAuditable
{
    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    void IAuditable.StampCreated(DateTimeOffset at, Guid? by)
    {
        CreatedAt = at;
        CreatedBy = by;
    }

    void IAuditable.StampUpdated(DateTimeOffset at, Guid? by)
    {
        UpdatedAt = at;
        UpdatedBy = by;
    }
}

/// <summary>Base for tenant-owned data. TenantId is stamped and guarded by the persistence layer, never by callers.</summary>
public abstract class TenantEntity : AuditableEntity, ITenantScoped
{
    public Guid TenantId { get; private set; }

    void ITenantScoped.StampTenant(Guid tenantId) => TenantId = tenantId;
}
