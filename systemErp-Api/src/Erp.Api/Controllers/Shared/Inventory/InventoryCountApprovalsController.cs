using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

/// <summary>مستندات اعتماد الجرد: القرار يتطلب صلاحية الاعتماد، والاعتماد النهائي ينشئ التسوية والقيد.</summary>
[Route("api/v1/inventorycountapprovals"), RequireScreen("inventory-counts")]
public class InventoryCountApprovalsController : ErpControllerBase
{
    private readonly IInventoryCountApprovalService _approvals;
    public InventoryCountApprovalsController(IInventoryCountApprovalService approvals) => _approvals = approvals;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] InventoryCountScope? scope, [FromQuery] PaginationParams query, CancellationToken ct)
        => Success(await _approvals.ListAsync(scope, query, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _approvals.GetAsync(id, ct));

    /// <summary>قيد الفروق: المرحَّل بعد الاعتماد، أو معاينة مطابقة (isPreview) قبل الاعتماد.</summary>
    [HttpGet("{id:guid}/journal")]
    public async Task<IActionResult> Journal(Guid id, CancellationToken ct) => Success(await _approvals.JournalAsync(id, ct));

    [HttpPost("{id:guid}/approve"),RequireScreen("inventory-counts", ScreenAction.Approve)]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApprovalDecisionDto decision, CancellationToken ct)
        => Success(await _approvals.ApproveAsync(id, decision, ct), Messages.InventoryCountApproved);

    [HttpPost("{id:guid}/reject"), RequireScreen("inventory-counts", ScreenAction.Approve)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] ApprovalDecisionDto decision, CancellationToken ct)
        => Success(await _approvals.RejectAsync(id, decision, ct), Messages.InventoryCountRejected);
}
