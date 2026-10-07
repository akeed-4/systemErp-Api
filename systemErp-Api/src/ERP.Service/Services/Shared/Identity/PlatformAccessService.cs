using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Service.Data;
using Microsoft.Extensions.Options;

namespace ERP.Service.Services.Shared;

/// <summary>
/// من يدير المنصة: بريد ضمن Platform:AdminEmails (المدير الجذر، صلاحية كاملة)، أو مستخدم مُنح صلاحية
/// <see cref="PermissionService.PlatformScreenId"/> بالاسم من قائمة الصلاحيات. لا تُشتق من الدور ولا من الافتراضيات:
/// مالك أي منشأة لا يملكها ما لم تُمنح له صراحةً من مدير منصة.
/// </summary>
public class PlatformAccessService : IPlatformAccessService
{
    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IOptionsMonitor<PlatformOptions> _options;

    public PlatformAccessService(ErpDbContext db, ICurrentUser user, IOptionsMonitor<PlatformOptions> options)
    {
        _db = db; _user = user; _options = options;
    }

    public async Task<PlatformAccessDto> GetAsync(CancellationToken ct = default)
    {
        if (!_user.IsAuthenticated) return new PlatformAccessDto();
        if (_options.CurrentValue.IsAdmin(_user.Email)) return new PlatformAccessDto { IsPlatformAdmin = true, CanManage = true };
        if (_user.UserId is not { } userId) return new PlatformAccessDto();

        var grant = await _db.Set<UserRolePermission>().AsNoTracking()
            .Where(p => p.UserId == userId)
            .SelectMany(p => p.Permissions)
            .Where(i => i.ScreenId == PermissionService.PlatformScreenId)
            .Select(i => new { i.CanView, i.CanEdit })
            .FirstOrDefaultAsync(ct);
        if (grant == null) return new PlatformAccessDto();
        return new PlatformAccessDto { IsPlatformAdmin = grant.CanView || grant.CanEdit, CanManage = grant.CanEdit };
    }
}
