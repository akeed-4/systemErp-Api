namespace Erp.Modules.Permissions.Contracts;

/// <summary>The frontend ScreenPermission shape.</summary>
public sealed record ScreenPermissionDto(
    string ScreenId,
    string ScreenNameAr,
    string ScreenNameEn,
    bool CanView,
    bool CanCreate,
    bool CanEdit,
    bool CanDelete,
    bool CanApprove);

/// <summary>Effective permissions of the current user, for other modules that need more than a yes/no check.</summary>
public interface IEffectivePermissions
{
    Task<IReadOnlyList<ScreenPermissionDto>> GetForCurrentUserAsync(CancellationToken cancellationToken);
}
