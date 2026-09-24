using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.BuildingBlocks.Infrastructure.Tenancy;
using Erp.BuildingBlocks.Web;
using Erp.Modules.Identity.Contracts;
using Erp.Modules.Permissions.Application;
using Erp.Modules.Permissions.Contracts;
using Erp.Modules.Permissions.Persistence;
using Erp.SharedKernel.Errors;
using Erp.SharedKernel.Security;
using Erp.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Permissions.Endpoints;

/// <summary>The frontend UserRolePermission shape.</summary>
internal sealed record UserRolePermissionDto(Guid TenantId, Guid? UserId, string? RoleId, IReadOnlyList<ScreenPermissionDto> Permissions);

internal sealed record SavePermissionsRequest(IReadOnlyList<ScreenPermissionDto> Permissions);

internal static class PermissionEndpoints
{
    private static readonly string[] Roles =
        [SystemRoles.Owner, SystemRoles.Admin, SystemRoles.GeneralManager, SystemRoles.ChiefAccountant, SystemRoles.SalesRep];

    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/permissions").WithTags("Permissions");
        group.MapGet("screens", ListScreensAsync).RequireAuthorization();
        group.MapGet("me", GetMineAsync).RequireAuthorization();
        group.MapGet("roles/{role}", GetRoleAsync).RequireScreen(ScreenIds.UserPermissions, ScreenAction.View);
        group.MapPut("roles/{role}", SaveRoleAsync).RequireScreen(ScreenIds.UserPermissions, ScreenAction.Edit);
        group.MapGet("users/{userId:guid}", GetUserAsync).RequireScreen(ScreenIds.UserPermissions, ScreenAction.View);
        group.MapPut("users/{userId:guid}", SaveUserAsync).RequireScreen(ScreenIds.UserPermissions, ScreenAction.Edit);
    }

    private static async Task<IResult> ListScreensAsync(PermissionsDbContext db, CancellationToken ct) =>
        ErpResults.Ok(await db.Screens.AsNoTracking().OrderBy(s => s.SortOrder)
            .Select(s => new { id = s.Id, nameAr = s.NameAr, nameEn = s.NameEn }).ToListAsync(ct));

    private static async Task<IResult> GetMineAsync(IEffectivePermissions permissions, ICurrentUser user, ITenantContext tenant, CancellationToken ct) =>
        ErpResults.Ok(new UserRolePermissionDto(tenant.TenantId, user.UserId, user.Role, await permissions.GetForCurrentUserAsync(ct)));

    private static async Task<IResult> GetRoleAsync(string role, PermissionsDbContext db, ITenantContext tenant, CancellationToken ct)
    {
        EnsureRole(role);
        var screens = await db.Screens.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync(ct);
        var rows = await db.RolePermissions.AsNoTracking().Where(p => p.RoleCode == role).ToDictionaryAsync(p => p.ScreenId, ct);
        return ErpResults.Ok(new UserRolePermissionDto(tenant.TenantId, null, role, screens.Select(s => ToDto(s, rows.GetValueOrDefault(s.Id))).ToList()));
    }

    private static async Task<IResult> SaveRoleAsync(
        string role,
        SavePermissionsRequest request,
        PermissionsDbContext db,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        CancellationToken ct)
    {
        EnsureRole(role);
        if (role == SystemRoles.Owner)
        {
            throw ErpException.Conflict("owner_permissions_fixed", "Owner permissions cannot be changed.", "لا يمكن تعديل صلاحيات المالك.");
        }

        await ValidScreensAsync(db, request, ct);
        var rows = await db.RolePermissions.Where(p => p.RoleCode == role).ToDictionaryAsync(p => p.ScreenId, ct);
        foreach (var p in request.Permissions)
        {
            if (!rows.TryGetValue(p.ScreenId, out var row))
            {
                row = new RoleScreenPermission(role, p.ScreenId);
                db.RolePermissions.Add(row);
            }

            row.Set(p.CanView, p.CanCreate, p.CanEdit, p.CanDelete, p.CanApprove);
        }

        // Role changes affect many users; their cached permissions expire within the 60 s TTL.
        await unitOfWork.SaveChangesAsync(ct);
        return await GetRoleAsync(role, db, tenant, ct);
    }

    private static async Task<IResult> GetUserAsync(Guid userId, PermissionsDbContext db, IUserDirectory users, ITenantContext tenant, CancellationToken ct)
    {
        var user = await users.FindAsync(userId, ct) ?? throw ErpException.NotFound("User", "المستخدم");
        var evaluator = new PermissionEvaluator(db, new FixedUser(userId, user.Role), NoCache.Instance);
        return ErpResults.Ok(new UserRolePermissionDto(tenant.TenantId, userId, user.Role, await evaluator.LoadAsync(userId, user.Role, ct)));
    }

    private static async Task<IResult> SaveUserAsync(
        Guid userId,
        SavePermissionsRequest request,
        PermissionsDbContext db,
        IUserDirectory users,
        IUnitOfWork unitOfWork,
        ITenantCache cache,
        ITenantContext tenant,
        CancellationToken ct)
    {
        var user = await users.FindAsync(userId, ct) ?? throw ErpException.NotFound("User", "المستخدم");
        await ValidScreensAsync(db, request, ct);

        var rows = await db.UserPermissions.Where(p => p.UserId == userId).ToDictionaryAsync(p => p.ScreenId, ct);
        foreach (var p in request.Permissions)
        {
            if (!rows.TryGetValue(p.ScreenId, out var row))
            {
                row = new UserScreenPermission(userId, p.ScreenId);
                db.UserPermissions.Add(row);
            }

            row.Set(p.CanView, p.CanCreate, p.CanEdit, p.CanDelete, p.CanApprove);
        }

        await unitOfWork.SaveChangesAsync(ct);
        cache.Remove($"perm:{userId:N}");
        return await GetUserAsync(userId, db, users, tenant, ct);
    }

    private static async Task ValidScreensAsync(PermissionsDbContext db, SavePermissionsRequest request, CancellationToken ct)
    {
        var screens = (await db.Screens.Select(s => s.Id).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var unknown = request.Permissions.Select(p => p.ScreenId).Where(id => !screens.Contains(id)).ToList();
        if (unknown.Count > 0)
        {
            throw ErpException.Validation($"Unknown screen(s): {string.Join(", ", unknown)}.", "توجد شاشات غير معروفة في الطلب.");
        }
    }

    private static void EnsureRole(string role)
    {
        if (!Roles.Contains(role))
        {
            throw ErpException.NotFound("Role", "الدور الوظيفي");
        }
    }

    private static ScreenPermissionDto ToDto(Screen s, RoleScreenPermission? p) =>
        new(s.Id, s.NameAr, s.NameEn, p?.CanView ?? false, p?.CanCreate ?? false, p?.CanEdit ?? false, p?.CanDelete ?? false, p?.CanApprove ?? false);

    private sealed class FixedUser(Guid userId, string role) : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public Guid? UserId => userId;

        public string? Role => role;

        public string? Email => null;

        public string? Name => null;
    }

    private sealed class NoCache : ITenantCache
    {
        public static readonly NoCache Instance = new();

        public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan ttl, CancellationToken cancellationToken) =>
            factory(cancellationToken);

        public void Remove(string key)
        {
        }
    }
}
