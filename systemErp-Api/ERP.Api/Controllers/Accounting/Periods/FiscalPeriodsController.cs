using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>الفترات المالية: تاريخ إقفال الدفاتر وإقفال السنة المالية.</summary>
[Route("api/v1/fiscalperiods"), RequireScreen("accounts")]
public class FiscalPeriodsController : ErpControllerBase
{
    private readonly IFiscalPeriodService _periods;
    public FiscalPeriodsController(IFiscalPeriodService periods) => _periods = periods;

    [HttpGet] public async Task<IActionResult> Get(CancellationToken ct) => Success(await _periods.GetAsync(ct));

    [HttpPut("lock")]
    public async Task<IActionResult> SetLock([FromBody] SetPeriodLockDto dto, CancellationToken ct)
        => Success(await _periods.SetLockAsync(dto, ct), Messages.PeriodLockSaved);

    [HttpPost("close-year")]
    public async Task<IActionResult> CloseYear([FromBody] CloseYearRequestDto dto, CancellationToken ct)
        => Success(await _periods.CloseYearAsync(dto, ct), Messages.FiscalYearClosed);
}
