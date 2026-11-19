using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

/// <summary>
/// متابعة اعتماد المستندات من شاشاتها لأي مستخدم مسجّل: ما قدّمه (scope=mine) وما ينتظر اعتماده (scope=awaiting، المقيّد بالشاشات
/// التي يملك عليها «اعتماد»).
/// </summary>
[Route("api/v1/approval-tracking")]
public class ApprovalTrackingController : ErpControllerBase
{
    private readonly IApprovalTrackingService _tracking;
    public ApprovalTrackingController(IApprovalTrackingService tracking) => _tracking = tracking;

    [HttpGet, AuthenticatedOnly]
    public async Task<IActionResult> List([FromQuery] string? scope, [FromQuery] string? status, [FromQuery] PaginationParams p, CancellationToken ct)
        => Success(await _tracking.ListAsync(scope ?? "mine", status, p, ct));

    [HttpGet("summary"), AuthenticatedOnly]
    public async Task<IActionResult> Summary(CancellationToken ct) => Success(await _tracking.SummaryAsync(ct));
}
