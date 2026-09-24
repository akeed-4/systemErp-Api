using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Erp.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Last line of defence before SQL is sent. For every tenant-scoped entity it stamps TenantId on insert and throws
/// <see cref="TenantIsolationViolationException"/> if an entity belongs to (or is being moved to) another tenant.
/// Also stamps audit columns. Singleton: all per-scope state comes from the saving context.
/// </summary>
internal sealed class TenantGuardInterceptor(TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Guard(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Guard(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Guard(DbContext? context)
    {
        if (context is not ModuleDbContext moduleContext)
        {
            return;
        }

        var tenant = moduleContext.Dependencies.Tenant;
        var platform = moduleContext.Dependencies.Platform;
        var userId = moduleContext.Dependencies.CurrentUser.UserId;
        var now = clock.GetUtcNow();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.Entity is ITenantScoped scoped)
            {
                GuardTenant(entry, scoped, tenant, platform.IsActive);
            }

            if (entry.Entity is IAuditable auditable)
            {
                if (entry.State == EntityState.Added)
                {
                    auditable.StampCreated(now, userId);
                }
                else if (entry.State == EntityState.Modified)
                {
                    auditable.StampUpdated(now, userId);
                }
            }
        }
    }

    private static void GuardTenant(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry,
        ITenantScoped scoped,
        ITenantContext tenant,
        bool platformScope)
    {
        var entityName = entry.Metadata.ClrType.Name;

        if (!tenant.IsResolved)
        {
            if (platformScope && scoped.TenantId != Guid.Empty)
            {
                return;
            }

            throw new TenantIsolationViolationException(
                $"Cannot save {entityName}: no tenant is resolved for this scope.");
        }

        if (entry.State == EntityState.Added && scoped.TenantId == Guid.Empty)
        {
            scoped.StampTenant(tenant.TenantId);
            return;
        }

        if (scoped.TenantId != tenant.TenantId)
        {
            throw new TenantIsolationViolationException(
                $"Cannot save {entityName}: it belongs to tenant {scoped.TenantId}, but the current tenant is {tenant.TenantId}.");
        }

        if (entry.State == EntityState.Modified)
        {
            var original = entry.Property(nameof(ITenantScoped.TenantId)).OriginalValue;
            if (original is Guid originalTenant && originalTenant != tenant.TenantId)
            {
                throw new TenantIsolationViolationException(
                    $"Cannot save {entityName}: moving rows between tenants is not allowed.");
            }
        }
    }
}
