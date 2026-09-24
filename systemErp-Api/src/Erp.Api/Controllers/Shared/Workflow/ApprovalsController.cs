using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

[Route("api/v1/approvals")]
public class ApprovalsController : ErpControllerBase
{
    private readonly IApprovalService _approvals;
    public ApprovalsController(IApprovalService approvals) => _approvals = approvals;

    [HttpPost("check"), RequireScreen("dashboard")]
    public async Task<IActionResult> Check([FromBody] ApprovalCheckRequestDto request, CancellationToken ct)
        => Success(await _approvals.CheckAndCreateAsync(request, ct));

    [HttpGet, RequireScreen("dashboard")]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] PaginationParams query, CancellationToken ct)
        => Success(await _approvals.ListRequestsAsync(status, query, ct));

    [HttpGet("{id:guid}"), RequireScreen("dashboard")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _approvals.GetAsync(id, ct));

    [HttpDelete("{id:guid}"), RequireScreen("dashboard", ScreenAction.View)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) => Success(await _approvals.CancelAsync(id, ct), "تم سحب الطلب");

    [HttpPost("{id:guid}/approve"), RequireScreen("approval-policies", ScreenAction.Approve)]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApprovalDecisionDto decision, CancellationToken ct)
        => Success(await _approvals.ApproveAsync(id, decision, ct));

    [HttpPost("{id:guid}/reject"), RequireScreen("approval-policies", ScreenAction.Approve)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] ApprovalDecisionDto decision, CancellationToken ct)
        => Success(await _approvals.RejectAsync(id, decision, ct));
}
