using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.POS;
using ERP.Core.DTOs.POS;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.POS;

[Route("api/v1/pos/shifts"), RequireScreen("sales")]
public class PosShiftsController : ErpControllerBase
{
    private readonly IPosShiftService _shifts;
    public PosShiftsController(IPosShiftService shifts) => _shifts = shifts;

    [HttpGet("active")] public async Task<IActionResult> Active(CancellationToken ct) => Success(await _shifts.GetActiveAsync(ct));
    [HttpGet] public async Task<IActionResult> List([FromQuery] PaginationParams q, CancellationToken ct) => Success(await _shifts.ListAsync(q, ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _shifts.GetAsync(id, ct));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePosShiftRequestDto dto, CancellationToken ct) => Success(await _shifts.UpdateAsync(id, dto, ct), "تم تحديث الوردية");

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] bool cascade, CancellationToken ct) { await _shifts.DeleteAsync(id, cascade, ct); return Success<object?>(null, "تم حذف الوردية"); }

    [HttpPost("open")]
    public async Task<IActionResult> Open([FromBody] OpenShiftRequestDto dto, CancellationToken ct) => Success(await _shifts.OpenAsync(dto, ct), "تم فتح الوردية");

    [HttpPost("close")]
    public async Task<IActionResult> Close([FromBody] CloseShiftRequestDto dto, CancellationToken ct) => Success(await _shifts.CloseAsync(dto, ct), "تم إغلاق الوردية");
}
