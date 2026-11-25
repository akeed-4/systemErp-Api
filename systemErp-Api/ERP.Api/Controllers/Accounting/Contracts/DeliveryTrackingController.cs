using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>
/// متابعة التوصيل: شاشة مستقلة بصلاحية لكل إجراء (عرض، خروج = إرسال، تأكيد الاستلام = استلام، إلغاء)؛ الفحص هنا وفي الخدمة.
/// </summary>
[Route("api/v1/delivery-tracking"), RequireScreen("delivery-tracking")]
[RequireModule(PlatformModules.Accounting)]
[RequireFeature(PlanFeatures.CommercialCycle)]
public class DeliveryTrackingController : ErpControllerBase
{
    private readonly IDeliveryTrackingService _tracking;
    public DeliveryTrackingController(IDeliveryTrackingService tracking) => _tracking = tracking;

    [HttpGet] public async Task<IActionResult> List([FromQuery] DeliveryTrackingQueryDto q, CancellationToken ct) => Success(await _tracking.ListAsync(q, ct));

    [HttpGet("{id:guid}/events")]
    public async Task<IActionResult> Events(Guid id, CancellationToken ct) => Success(await _tracking.GetEventsAsync(id, ct));

    [HttpPost("{id:guid}/review-location"), RequireScreen("delivery-tracking", ScreenAction.Review)]
    public async Task<IActionResult> ReviewLocation(Guid id, [FromBody] ReviewDeliveryLocationDto? dto, CancellationToken ct)
        => Success(await _tracking.ReviewLocationAsync(id, dto ?? new ReviewDeliveryLocationDto(), ct));

    [HttpPost("{id:guid}/dispatch"), RequireScreen("delivery-tracking", ScreenAction.Send)]
    public async Task<IActionResult> Dispatch(Guid id, [FromBody] DispatchDeliveryNoteDto? dto, CancellationToken ct)
        => Success(await _tracking.DispatchAsync(id, dto ?? new DispatchDeliveryNoteDto(), ct));

    [HttpPost("{id:guid}/confirm"), RequireScreen("delivery-tracking", ScreenAction.Receive)]
    public async Task<IActionResult> Confirm(Guid id, [FromBody] ConfirmDeliveryDto dto, CancellationToken ct)
        => Success(await _tracking.ConfirmAsync(id, dto, ct));

    [HttpPost("{id:guid}/cancel"), RequireScreen("delivery-tracking", ScreenAction.Cancel)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelDeliveryNoteDto dto, CancellationToken ct)
        => Success(await _tracking.CancelAsync(id, dto, ct));
}

/// <summary>تقارير التوصيل: شاشة مستقلة للقراءة.</summary>
[Route("api/v1/delivery-reports"), RequireScreen("delivery-reports")]
[RequireModule(PlatformModules.Accounting)]
[RequireFeature(PlanFeatures.CommercialCycle)]
public class DeliveryReportsController : ErpControllerBase
{
    private readonly IDeliveryTrackingService _tracking;
    public DeliveryReportsController(IDeliveryTrackingService tracking) => _tracking = tracking;

    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Success(await _tracking.SummaryAsync(from, to, ct));
}
