using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

/// <summary>أدوار المنشأة (أساسية ومخصّصة): القائمة بعدد المستخدمين والصلاحيات والنطاق، والإضافة والنسخ والتعديل والتعطيل.</summary>
[Route("api/v1/roles")]
[RequireScreen("user-permissions")]
public class RolesController : ErpControllerBase
{
    private readonly IRoleService _roles;
    public RolesController(IRoleService roles) => _roles = roles;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Success(await _roles.ListAsync(ct));

    [HttpGet("{code}")]
    public async Task<IActionResult> Get(string code, CancellationToken ct) => Success(await _roles.GetAsync(code, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveRoleDto dto, CancellationToken ct)
        => Success(await _roles.CreateAsync(dto, ct), Messages.RoleSaved);

    [HttpPost("{code}/copy"), RequireScreen("user-permissions", ScreenAction.Create)]
    public async Task<IActionResult> Copy(string code, [FromBody] SaveRoleDto dto, CancellationToken ct)
        => Success(await _roles.CopyAsync(code, dto, ct), Messages.RoleCopied);

    [HttpPut("{code}")]
    public async Task<IActionResult> Update(string code, [FromBody] SaveRoleDto dto, CancellationToken ct)
        => Success(await _roles.UpdateAsync(code, dto, ct), Messages.RoleSaved);

    [HttpPost("{code}/activate"), RequireScreen("user-permissions", ScreenAction.Edit)]
    public async Task<IActionResult> Activate(string code, CancellationToken ct)
        => Success(await _roles.SetActiveAsync(code, true, ct), Messages.RoleStatusChanged);

    [HttpPost("{code}/deactivate"), RequireScreen("user-permissions", ScreenAction.Edit)]
    public async Task<IActionResult> Deactivate(string code, CancellationToken ct)
        => Success(await _roles.SetActiveAsync(code, false, ct), Messages.RoleStatusChanged);
}
