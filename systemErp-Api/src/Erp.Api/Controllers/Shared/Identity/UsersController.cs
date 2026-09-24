using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

[Route("api/v1/users")]
[RequireScreen("user-permissions")]
public class UsersController : ErpControllerBase
{
    private readonly IUserService _users;
    public UsersController(IUserService users) => _users = users;

    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Success(await _users.ListAsync(ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _users.GetAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequestDto request, CancellationToken ct)
        => Success(await _users.CreateAsync(request, ct), "تم إنشاء المستخدم");

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequestDto request, CancellationToken ct)
        => Success(await _users.UpdateAsync(id, request, ct), "تم تعديل المستخدم");

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await _users.DeactivateAsync(id, ct);
        return Success("تم تعطيل المستخدم");
    }
}
