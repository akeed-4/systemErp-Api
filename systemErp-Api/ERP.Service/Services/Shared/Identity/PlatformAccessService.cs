using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Service.Data;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ERP.Service.Services.Shared;

/// <summary>
/// من يدير المنصة: معرّف ضمن Platform:AdminUserIds (المدير الجذر، صلاحية كاملة؛ وبريد ضمن Platform:AdminEmails بديلاً مؤقتاً)، أو مستخدم مُنح صلاحية
/// <see cref="PermissionService.PlatformScreenId"/> بالاسم من قائمة الصلاحيات. لا تُشتق من الدور ولا من الافتراضيات:
/// مالك أي منشأة لا يملكها ما لم تُمنح له صراحةً من مدير منصة.
/// </summary>
public class PlatformAccessService : IPlatformAccessService
{
    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IOptionsMonitor<PlatformOptions> _options;
    private readonly ILogger<PlatformAccessService> _log;
    /// <summary>المدراء المعرَّفون بالبريد وقد سُجّل تنبيه نقلهم (مرة لكل مستخدم في عمر العملية).</summary>
    private static readonly ConcurrentDictionary<Guid, byte> LegacyLogged = new();

    public PlatformAccessService(ErpDbContext db, ICurrentUser user, IOptionsMonitor<PlatformOptions> options, ILogger<PlatformAccessService> log)
    {
        _db = db; _user = user; _options = options; _log = log;
    }

    public async Task<PlatformAccessDto> GetAsync(CancellationToken ct = default)
    {
        if (!_user.IsAuthenticated) return new PlatformAccessDto();
        var options = _options.CurrentValue;
        // الأساسي: معرّف المستخدم ضمن Platform:AdminUserIds
        if (options.IsAdminUser(_user.UserId)) return new PlatformAccessDto { IsPlatformAdmin = true, CanManage = true };
        // البديل المؤقت: البريد ضمن Platform:AdminEmails (يُسجَّل مرة لكل مستخدم ليُنقل إلى AdminUserIds)
        if (options.IsAdmin(_user.Email))
        {
            if (_user.UserId is { } legacy && LegacyLogged.TryAdd(legacy, 0))
                _log.LogWarning("Platform admin {UserId} was identified by Platform:AdminEmails; add this id to Platform:AdminUserIds.", legacy);
            return new PlatformAccessDto { IsPlatformAdmin = true, CanManage = true };
        }
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
