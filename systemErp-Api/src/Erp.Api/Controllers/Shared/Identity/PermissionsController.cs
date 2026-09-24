using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

[Route("api/v1/permissions")]
[RequireScreen("user-permissions")]
public class PermissionsController : ErpControllerBase
{
    private readonly IPermissionService _permissions;
    public PermissionsController(IPermissionService permissions) => _permissions = permissions;

    [HttpGet("screens"), AllowAnonymous]
    public IActionResult Screens() => Success(_permissions.GetScreens());

    /// <summary>صلاحيات المستخدم الحالي (للواجهة لإخفاء الشاشات) - متاحة لأي مستخدم مسجّل.</summary>
    [HttpGet("me")]
    [RequireScreen("dashboard")]
    public async Task<IActionResult> Mine([FromServices] ICurrentUser me, CancellationToken ct)
        => Success(await _permissions.GetForUserOrRoleAsync(me.UserId, me.RoleId, ct));

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] Guid? userId, [FromQuery] string? roleId, CancellationToken ct)
        => Success(await _permissions.GetForUserOrRoleAsync(userId, roleId, ct));

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SavePermissionsRequestDto request, CancellationToken ct)
    {
        await _permissions.SaveAsync(request, ct);
        return Success("تم حفظ تحديثات الصلاحيات بنجاح");
    }
}
