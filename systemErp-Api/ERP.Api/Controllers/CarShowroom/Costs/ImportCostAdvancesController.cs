using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.CarShowroom;

/// <summary>
/// تكاليف الاستيراد المقدمة لأوامر الشراء. كسند التكاليف الإضافية: يتطلب رؤية التكلفة، واعتماده وإلغاؤه صلاحية اعتماد السندات.
/// </summary>
[Route("api/v1/importcostadvances"), RequireScreen("car-showroom")]
[RequireModule(PlatformModules.CarShowroom)]
public class ImportCostAdvancesController : ErpControllerBase
{
    private readonly IImportCostAdvanceService _service;
    private readonly ICostVisibility _cost;
    private readonly IPermissionService _permissions;
    public ImportCostAdvancesController(IImportCostAdvanceService service, ICostVisibility cost, IPermissionService permissions)
    {
        _service = service; _cost = cost; _permissions = permissions;
    }

    private async Task RequireCostViewAsync(CancellationToken ct)
    {
        if (!await _cost.CanViewAsync(ct)) throw new ForbiddenException();
    }

    private async Task RequireApprovalAsync(CancellationToken ct)
    {
        if (!await _permissions.HasPermissionAsync(PermissionService.LandedCostApprovalScreenId, ScreenAction.Approve, ct)) throw new ForbiddenException();
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PaginationParams q, [FromQuery] Guid? procurementOrderId, CancellationToken ct)
    {
        await RequireCostViewAsync(ct);
        return Success(await _service.ListAsync(q, procurementOrderId, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        await RequireCostViewAsync(ct);
        return Success(await _service.GetAsync(id, ct));
    }

    /// <summary>المستندات المفتوحة لأمر شراء (لزر السحب في سند التكاليف الإضافية).</summary>
    [HttpGet("open")]
    public async Task<IActionResult> Open([FromQuery] Guid procurementOrderId, CancellationToken ct)
    {
        await RequireCostViewAsync(ct);
        return Success(await _service.OpenForOrderAsync(procurementOrderId, ct));
    }

    /// <summary>رصيد تكاليف الاستيراد تحت التسوية حسب أمر الشراء.</summary>
    [HttpGet("balances")]
    public async Task<IActionResult> Balances(CancellationToken ct)
    {
        await RequireCostViewAsync(ct);
        return Success(await _service.BalancesAsync(ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateImportCostAdvanceDto dto, CancellationToken ct)
    {
        await RequireCostViewAsync(ct);
        var created = await _service.CreateAsync(dto, ct);
        return Created($"{Request.Path}/{created.Id}", ApiResponse<ImportCostAdvanceDto>.Ok(created, Messages.CreatedSuccessfully).WithStatus(201));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateImportCostAdvanceDto dto, CancellationToken ct)
    {
        await RequireCostViewAsync(ct);
        return Success(await _service.UpdateAsync(id, dto, ct), Messages.UpdatedSuccessfully);
    }

    [HttpPost("{id:guid}/approve")]
    [RequireScreen("car-showroom", ScreenAction.View)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        await RequireApprovalAsync(ct);
        return Success(await _service.ApproveAsync(id, ct), Messages.ImportCostAdvanceApproved);
    }

    [HttpPost("{id:guid}/cancel")]
    [RequireScreen("car-showroom", ScreenAction.View)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await RequireApprovalAsync(ct);
        return Success(await _service.CancelAsync(id, ct), Messages.ImportCostAdvanceCancelled);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await RequireCostViewAsync(ct);
        await _service.DeleteAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }
}
