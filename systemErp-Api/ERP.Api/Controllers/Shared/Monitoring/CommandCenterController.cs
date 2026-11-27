using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

/// <summary>
/// مركز مراقبة المنشأة: صلاحية الشاشة `command-center` (عرض للقراءة، تعديل لتشغيل الفحص ومعالجة التنبيهات).
/// منشأة المستخدم وحدها (المنشأة من الـ JWT). لا تعديل لمستندات ولا حذف لسجلات من هنا.
/// </summary>
[Route("api/v1/command-center"), RequireScreen("command-center", ScreenAction.View)]
public class CommandCenterController : ErpControllerBase
{
    private readonly ICommandCenterService _center;
    private readonly IMonitoringAlertService _alerts;

    public CommandCenterController(ICommandCenterService center, IMonitoringAlertService alerts)
    {
        _center = center; _alerts = alerts;
    }

    /// <summary>المؤشرات المالية والتجارية للفترة (week | month | year).</summary>
    [HttpGet("overview")]
    public async Task<IActionResult> Overview([FromQuery] string? period, CancellationToken ct) => Success(await _center.OverviewAsync(period, ct));

    /// <summary>النبض التشغيلي الخفيف (للتحديث الدوري).</summary>
    [HttpGet("live")]
    public async Task<IActionResult> Live(CancellationToken ct) => Success(await _center.LiveAsync(ct));

    [HttpGet("inventory-alerts")]
    public async Task<IActionResult> InventoryAlerts(CancellationToken ct) => Success(await _center.InventoryAlertsAsync(ct));

    [HttpGet("zatca-attempts/load")]
    public async Task<IActionResult> ZatcaAttempts(DataSourceLoadOptions loadOptions, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Ok(await _center.ZatcaAttemptsAsync(loadOptions, from, to, ct));

    [HttpGet("audit/load")]
    public async Task<IActionResult> Audit(DataSourceLoadOptions loadOptions, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Ok(await _center.AuditActivityAsync(loadOptions, from, to, ct));

    [HttpGet("logins/load")]
    public async Task<IActionResult> Logins(DataSourceLoadOptions loadOptions, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Ok(await _center.LoginEventsAsync(loadOptions, from, to, ct));

    // ---------- الفحص والتنبيهات ----------

    [HttpGet("checks/latest")]
    public async Task<IActionResult> LatestCheck(CancellationToken ct) => Success(await _alerts.LatestRunAsync(ct));

    [HttpPost("checks/run"), RequireScreen("command-center", ScreenAction.Edit)]
    public async Task<IActionResult> RunChecks(CancellationToken ct) => Success(await _alerts.RunChecksAsync(ct));

    [HttpGet("alerts/load")]
    public async Task<IActionResult> Alerts(DataSourceLoadOptions loadOptions, [FromQuery] string? status, CancellationToken ct)
        => Ok(await _alerts.LoadAlertsAsync(loadOptions, status, ct));

    [HttpGet("alerts/{id:guid}")]
    public async Task<IActionResult> Alert(Guid id, CancellationToken ct) => Success(await _alerts.GetAsync(id, ct));

    /// <summary>إقرار، تعيين، بدء، ملاحظة، حل، إغلاق، إعادة فتح.</summary>
    [HttpPost("alerts/{id:guid}/actions"), RequireScreen("command-center", ScreenAction.Edit)]
    public async Task<IActionResult> Act(Guid id, [FromBody] MonitoringAlertActionRequestDto request, CancellationToken ct)
        => Success(await _alerts.ActAsync(id, request, ct));
}
