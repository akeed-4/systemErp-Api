using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.CarShowroom;

/// <summary>
/// سند التكاليف الإضافية: فاتورة مورد خدمات تُوزَّع على السيارات. يتطلب رؤية التكلفة، واعتماده وإلغاؤه صلاحية مستقلة.
/// </summary>
[Route("api/v1/landedcostvouchers"), RequireScreen("car-showroom")]
[RequireModule(PlatformModules.CarShowroom)]
public class LandedCostVouchersController : ErpControllerBase
{
    private readonly ILandedCostVoucherService _service;
    private readonly ICostVisibility _cost;
    private readonly IPermissionService _permissions;
    public LandedCostVouchersController(ILandedCostVoucherService service, ICostVisibility cost, IPermissionService permissions)
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
    public async Task<IActionResult> List([FromQuery] PaginationParams q, CancellationToken ct)
    {
        await RequireCostViewAsync(ct);
        return Success(await _service.ListAsync(q, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        await RequireCostViewAsync(ct);
        return Success(await _service.GetAsync(id, ct));
    }

    /// <summary>معاينة التوزيع قبل الحفظ أو الاعتماد: الحصص لكل سيارة وأثرها على تكلفتها. لا يُحفظ شيء.</summary>
    [HttpPost("preview")]
    [RequireScreen("car-showroom", ScreenAction.View)]
    public async Task<IActionResult> Preview([FromBody] CreateLandedCostVoucherDto dto, CancellationToken ct)
    {
        await RequireCostViewAsync(ct);
        return Success(await _service.PreviewAsync(dto, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateLandedCostVoucherDto dto, CancellationToken ct)
    {
        await RequireCostViewAsync(ct);
        var created = await _service.CreateAsync(dto, ct);
        return Created($"{Request.Path}/{created.Id}", ApiResponse<LandedCostVoucherDto>.Ok(created, Messages.CreatedSuccessfully).WithStatus(201));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateLandedCostVoucherDto dto, CancellationToken ct)
    {
        await RequireCostViewAsync(ct);
        return Success(await _service.UpdateAsync(id, dto, ct), Messages.UpdatedSuccessfully);
    }

    [HttpPost("{id:guid}/approve")]
    [RequireScreen("car-showroom", ScreenAction.View)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        await RequireApprovalAsync(ct);
        return Success(await _service.ApproveAsync(id, ct), Messages.LandedCostVoucherApproved);
    }

    [HttpPost("{id:guid}/cancel")]
    [RequireScreen("car-showroom", ScreenAction.View)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await RequireApprovalAsync(ct);
        return Success(await _service.CancelAsync(id, ct), Messages.LandedCostVoucherCancelled);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await RequireCostViewAsync(ct);
        await _service.DeleteAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }
}
