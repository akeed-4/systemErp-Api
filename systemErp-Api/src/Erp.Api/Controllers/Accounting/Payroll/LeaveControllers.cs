using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

[Route("api/v1/leavetypes"), RequireScreen("hr"), RequireModule(PlatformModules.Hr)]
public class LeaveTypesController : CrudController<LeaveTypeDto, CreateLeaveTypeDto, UpdateLeaveTypeDto>
{
    public LeaveTypesController(ILeaveTypeService s) : base(s) { }
}

/// <summary>طلبات الإجازات: تقديم، اعتماد أو رفض (بصلاحية الاعتماد)، إلغاء، وأرصدة الإجازة السنوية.</summary>
[Route("api/v1/leaverequests"), RequireScreen("hr"), RequireModule(PlatformModules.Hr)]
public class LeaveRequestsController : ErpControllerBase
{
    private readonly ILeaveService _leaves;
    public LeaveRequestsController(ILeaveService leaves) => _leaves = leaves;

    /// <summary>القائمة بخيارات DevExtreme وتُعيد <c>LoadResult</c> مباشرة دون مغلّف ApiResponse.</summary>
    [HttpGet("load")]
    public async Task<IActionResult> Load(DataSourceLoadOptions loadOptions, CancellationToken ct) => Ok(await _leaves.LoadAsync(loadOptions, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _leaves.GetAsync(id, ct));

    [HttpGet("balances")]
    public async Task<IActionResult> Balances(CancellationToken ct) => Success(await _leaves.ListBalancesAsync(ct));

    /// <summary>رصيد موظف حتى تاريخ (فارغ = اليوم).</summary>
    [HttpGet("balance/{employeeId:guid}")]
    public async Task<IActionResult> Balance(Guid employeeId, [FromQuery] DateTime? asOf, CancellationToken ct)
        => Success(await _leaves.GetBalanceAsync(employeeId, asOf, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateLeaveRequestDto dto, CancellationToken ct)
    {
        var created = await _leaves.CreateAsync(dto, ct);
        return Created($"{Request.Path}/{created.Id}", ApiResponse<LeaveRequestDto>.Ok(created, Messages.CreatedSuccessfully).WithStatus(201));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateLeaveRequestDto dto, CancellationToken ct)
        => Success(await _leaves.UpdateAsync(id, dto, ct), Messages.UpdatedSuccessfully);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _leaves.DeleteAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }

    [HttpPost("{id:guid}/approve"), RequireScreen("hr", ScreenAction.Approve)]
    public async Task<IActionResult> Approve(Guid id, [FromBody] LeaveDecisionDto? decision, CancellationToken ct)
        => Success(await _leaves.ApproveAsync(id, decision?.Note, ct), Messages.LeaveApproved);

    [HttpPost("{id:guid}/reject"), RequireScreen("hr", ScreenAction.Approve)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] LeaveDecisionDto? decision, CancellationToken ct)
        => Success(await _leaves.RejectAsync(id, decision?.Note, ct), Messages.LeaveRejected);

    [HttpPost("{id:guid}/cancel"), RequireScreen("hr", ScreenAction.Edit)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] LeaveDecisionDto? decision, CancellationToken ct)
        => Success(await _leaves.CancelAsync(id, decision?.Note, ct), Messages.LeaveCancelled);
}
