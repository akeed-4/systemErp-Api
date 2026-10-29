using ERP.Api.Infrastructure;
using ERP.Core.Contracts.POS;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.POS;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.POS;

[Route("api/v1/pos/terminals"), RequireModule(PlatformModules.Pos)]
public class PosTerminalsController : ErpControllerBase
{
    private readonly IPosTerminalService _terminals;
    public PosTerminalsController(IPosTerminalService terminals) => _terminals = terminals;

    [HttpGet("available"), RequireScreen("sales")]
    public async Task<IActionResult> Available(CancellationToken ct) => Success(await _terminals.ListAsync(true, ct));

    [HttpGet, RequireScreen("sales", ScreenAction.Edit)]
    public async Task<IActionResult> List(CancellationToken ct) => Success(await _terminals.ListAsync(false, ct));

    [HttpGet("users"), RequireScreen("sales", ScreenAction.Edit)]
    public async Task<IActionResult> Users(CancellationToken ct) => Success(await _terminals.UsersAsync(ct));

    [HttpPost, RequireScreen("sales", ScreenAction.Edit)]
    public async Task<IActionResult> Create([FromBody] SavePosTerminalDto dto, CancellationToken ct)
        => Success(await _terminals.SaveAsync(null, dto, ct));

    [HttpPut("{id:guid}"), RequireScreen("sales", ScreenAction.Edit)]
    public async Task<IActionResult> Update(Guid id, [FromBody] SavePosTerminalDto dto, CancellationToken ct)
        => Success(await _terminals.SaveAsync(id, dto, ct));
}
