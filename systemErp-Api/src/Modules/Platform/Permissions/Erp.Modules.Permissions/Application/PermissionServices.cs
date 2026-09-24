using Erp.BuildingBlocks.Infrastructure.Modules;
using Erp.BuildingBlocks.Infrastructure.Tenancy;
using Erp.Modules.Permissions.Contracts;
using Erp.Modules.Permissions.Persistence;
using Erp.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Permissions.Application;

/// <summary>The frontend's getDefaultPermissionsForRole (permission.service.ts), used to seed each new tenant.</summary>
internal static class DefaultPermissions
{
    public static (bool View, bool Create, bool Edit, bool Delete, bool Approve) For(string role, string screenId)
    {
        bool view = true, create = true, edit = true, delete = true, approve = true;

        if (role == SystemRoles.SalesRep)
        {
            approve = false;
            delete = false;
            if (screenId is ScreenIds.Accounts or ScreenIds.ApprovalPolicies or ScreenIds.UserPermissions or ScreenIds.Reports)
            {
                view = false;
                create = false;
                edit = false;
            }
        }
        else if (role is SystemRoles.ChiefAccountant or SystemRoles.GeneralManager)
        {
            if (screenId == ScreenIds.UserPermissions)
            {
                delete = false;
            }
        }
        else if (role is not (SystemRoles.Owner or SystemRoles.Admin) && screenId == ScreenIds.UserPermissions)
        {
            view = create = edit = delete = approve = false;
        }

        return (view, create, edit, delete, approve);
    }
}

internal sealed class PermissionsSeeder(PermissionsDbContext db) : IModuleSeeder
{
    public int Order => 30;

    public async Task SeedTenantAsync(TenantSeedContext context, CancellationToken cancellationToken)
    {
        if (await db.RolePermissions.AnyAsync(cancellationToken))
        {
            return;
        }

        var screens = await db.Screens.Select(s => s.Id).ToListAsync(cancellationToken);
        string[] roles = [SystemRoles.Owner, SystemRoles.Admin, SystemRoles.GeneralManager, SystemRoles.ChiefAccountant, SystemRoles.SalesRep];
        foreach (var role in roles)
        {
            foreach (var screen in screens)
            {
                var p = DefaultPermissions.For(role, screen);
                var row = new RoleScreenPermission(role, screen);
                row.Set(p.View, p.Create, p.Edit, p.Delete, p.Approve);
                db.RolePermissions.Add(row);
            }
        }
    }
}

internal sealed class ScreenReferenceSeeder(PermissionsDbContext db) : IReferenceDataSeeder
{
    public async Task SeedAsync(GlobalReferenceData data, CancellationToken cancellationToken)
    {
        var existing = await db.Screens.ToDictionaryAsync(s => s.Id, cancellationToken);
        foreach (var screen in data.Screens)
        {
            if (!existing.TryGetValue(screen.Id, out var row))
            {
                row = new Screen { Id = screen.Id };
                db.Screens.Add(row);
            }

            row.NameAr = screen.NameAr;
            row.NameEn = screen.NameEn;
            row.SortOrder = screen.SortOrder;
        }
    }
}

/// <summary>
/// Resolves effective permissions: a user override wins for its screen, otherwise the role's row applies.
/// Owners always have every permission so a tenant can never lock itself out. Cached per tenant and user.
/// </summary>
internal sealed class PermissionEvaluator(PermissionsDbContext db, ICurrentUser currentUser, ITenantCache cache)
    : IScreenPermissionChecker, IEffectivePermissions
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public async Task<bool> IsAllowedAsync(string screenId, ScreenAction action, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            return false;
        }

        if (currentUser.Role == SystemRoles.Owner)
        {
            return true;
        }

        var permission = (await GetForCurrentUserAsync(cancellationToken)).FirstOrDefault(p => p.ScreenId == screenId);
        return permission is not null && action switch
        {
            ScreenAction.View => permission.CanView,
            ScreenAction.Create => permission.CanCreate,
            ScreenAction.Edit => permission.CanEdit,
            ScreenAction.Delete => permission.CanDelete,
            ScreenAction.Approve => permission.CanApprove,
            _ => false,
        };
    }

    public Task<IReadOnlyList<ScreenPermissionDto>> GetForCurrentUserAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? Guid.Empty;
        var role = currentUser.Role ?? string.Empty;
        return cache.GetOrCreateAsync(CacheKey(userId), ct => LoadAsync(userId, role, ct), CacheTtl, cancellationToken);
    }

    public void Invalidate(Guid userId) => cache.Remove(CacheKey(userId));

    public async Task<IReadOnlyList<ScreenPermissionDto>> LoadAsync(Guid userId, string role, CancellationToken ct)
    {
        var screens = await db.Screens.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync(ct);
        var roleRows = await db.RolePermissions.AsNoTracking().Where(p => p.RoleCode == role).ToDictionaryAsync(p => p.ScreenId, ct);
        var userRows = await db.UserPermissions.AsNoTracking().Where(p => p.UserId == userId).ToDictionaryAsync(p => p.ScreenId, ct);

        return screens.Select(s =>
        {
            ScreenGrant? grant = userRows.TryGetValue(s.Id, out var u) ? u : roleRows.GetValueOrDefault(s.Id);
            var owner = role == SystemRoles.Owner;
            return new ScreenPermissionDto(
                s.Id,
                s.NameAr,
                s.NameEn,
                owner || (grant?.CanView ?? false),
                owner || (grant?.CanCreate ?? false),
                owner || (grant?.CanEdit ?? false),
                owner || (grant?.CanDelete ?? false),
                owner || (grant?.CanApprove ?? false));
        }).ToList();
    }

    private static string CacheKey(Guid userId) => $"perm:{userId:N}";
}
