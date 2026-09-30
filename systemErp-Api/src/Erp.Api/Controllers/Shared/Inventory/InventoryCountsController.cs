using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

/// <summary>مستندات الجرد للأصناف والمركبات (scope = items | vehicles). الاعتماد والتسوية عبر inventorycountapprovals.</summary>
[Route("api/v1/inventorycounts"), RequireScreen("inventory-counts")]
public class InventoryCountsController : CrudController<InventoryCountDto, CreateInventoryCountDto, UpdateInventoryCountDto>
{
    private readonly IInventoryCountService _counts;
    public InventoryCountsController(IInventoryCountService counts) : base(counts) => _counts = counts;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] InventoryCountScope? scope, [FromQuery] PaginationParams query, CancellationToken ct)
        => Success(await _counts.ListAsync(scope, query, ct));

    [NonAction]
    public override Task<IActionResult> List(PaginationParams query, CancellationToken ct) => List(null, query, ct);

    /// <summary>أسطر الجرد المتوقعة من رصيد النظام الحالي (لتجهيز جرد جديد دون حفظ).</summary>
    [HttpPost("snapshot"), RequireScreen("inventory-counts", ScreenAction.View)]
    public async Task<IActionResult> Snapshot([FromBody] InventoryCountSnapshotRequestDto request, CancellationToken ct)
        => Success(await _counts.SnapshotAsync(request, ct));

    [HttpGet("{id:guid}/approvals")]
    public async Task<IActionResult> Approvals(Guid id, CancellationToken ct) => Success(await _counts.ApprovalsAsync(id, ct));

    [HttpPost("{id:guid}/submit"), RequireScreen("inventory-counts", ScreenAction.Edit)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct)
        => Success(await _counts.SubmitAsync(id, ct), Messages.InventoryCountSubmitted);

    [HttpPost("{id:guid}/withdraw"), RequireScreen("inventory-counts", ScreenAction.Edit)]
    public async Task<IActionResult> Withdraw(Guid id, CancellationToken ct)
        => Success(await _counts.WithdrawAsync(id, ct), Messages.InventoryCountWithdrawn);

    [HttpPost("{id:guid}/cancel"), RequireScreen("inventory-counts", ScreenAction.Edit)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
        => Success(await _counts.CancelAsync(id, ct), Messages.InventoryCountCancelled);
}
