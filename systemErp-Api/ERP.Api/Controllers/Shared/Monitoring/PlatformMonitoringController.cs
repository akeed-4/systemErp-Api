using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

/// <summary>
/// مراقبة المنصة: لمدير المنصة وحده (RequirePlatformAdmin يُقيَّم مع كل طلب؛ القراءة بصلاحية العرض والفحص بصلاحية الإدارة).
/// بيانات المنصة فقط (المنشآت، القواعد، أخطاء الخادم، الدخول)؛ لا بيانات أعمال للمنشآت.
/// </summary>
[Route("api/v1/platform/monitoring"), RequirePlatformAdmin]
public class PlatformMonitoringController : ErpControllerBase
{
    private readonly IPlatformMonitoringService _monitoring;
    public PlatformMonitoringController(IPlatformMonitoringService monitoring) => _monitoring = monitoring;

    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken ct) => Success(await _monitoring.OverviewAsync(ct));

    [HttpGet("api-errors/load")]
    public async Task<IActionResult> ApiErrors(DataSourceLoadOptions loadOptions, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Ok(await _monitoring.ApiErrorsAsync(loadOptions, from, to, ct));

    [HttpGet("logins/load")]
    public async Task<IActionResult> Logins(DataSourceLoadOptions loadOptions, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Ok(await _monitoring.LoginEventsAsync(loadOptions, from, to, ct));

    [HttpGet("checks/latest")]
    public async Task<IActionResult> LatestCheck(CancellationToken ct) => Success(await _monitoring.LatestRunAsync(ct));

    [HttpPost("checks/run")]
    public async Task<IActionResult> RunChecks(CancellationToken ct) => Success(await _monitoring.RunChecksAsync(ct));
}
