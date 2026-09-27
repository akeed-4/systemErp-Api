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
    public async Task<IActionResult> Delete(Guid id, [FromQuery] bool cascade, CancellationToken ct) { await _shifts.DeleteAsync(id, cascade, ct); return Success("تم حذف الوردية"); }

    [HttpPost("open")]
    public async Task<IActionResult> Open([FromBody] OpenShiftRequestDto dto, CancellationToken ct) => Success(await _shifts.OpenAsync(dto, ct), "تم فتح الوردية");

    [HttpPost("close")]
    public async Task<IActionResult> Close([FromBody] CloseShiftRequestDto dto, CancellationToken ct) => Success(await _shifts.CloseAsync(dto, ct), "تم إغلاق الوردية");

    // ---------- الدرج: إيداع/صرف نقدي ----------
    [HttpPost("cash-movements")]
    public async Task<IActionResult> AddCashMovement([FromBody] CashMovementRequestDto dto, CancellationToken ct) => Success(await _shifts.AddCashMovementAsync(dto, ct), "تم تسجيل الحركة النقدية");

    [HttpGet("{id:guid}/cash-movements")]
    public async Task<IActionResult> CashMovements(Guid id, CancellationToken ct) => Success(await _shifts.ListCashMovementsAsync(id, ct));

    [HttpDelete("cash-movements/{id:guid}")]
    public async Task<IActionResult> DeleteCashMovement(Guid id, CancellationToken ct) { await _shifts.DeleteCashMovementAsync(id, ct); return Success("تم إلغاء الحركة النقدية"); }

    // ---------- تقارير X / Z ----------
    [HttpGet("active/report")] public async Task<IActionResult> ActiveReport(CancellationToken ct) => Success(await _shifts.GetReportAsync(null, ct));
    [HttpGet("{id:guid}/report")] public async Task<IActionResult> Report(Guid id, CancellationToken ct) => Success(await _shifts.GetReportAsync(id, ct));
}
