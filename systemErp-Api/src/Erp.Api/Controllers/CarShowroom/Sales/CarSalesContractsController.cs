using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.CarShowroom;

[Route("api/v1/carsalescontracts"), RequireScreen("car-showroom")]
[RequireModule(PlatformModules.CarShowroom)]
public class CarSalesContractsController : ErpControllerBase
{
    private readonly ICarSaleService _service;
    private readonly IPermissionService _permissions;
    public CarSalesContractsController(ICarSaleService service, IPermissionService permissions)
    {
        _service = service; _permissions = permissions;
    }

    /// <summary>cycleType اختياري بصيغة snake_case (individual | corporate | bank_lease | installment).</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PaginationParams q, [FromQuery] string? cycleType, CancellationToken ct)
    {
        CarSalesCycleType? cycle = null;
        if (!string.IsNullOrWhiteSpace(cycleType))
        {
            if (!Enum.TryParse<CarSalesCycleType>(cycleType.Replace("_", ""), true, out var parsed))
                throw new ValidationFailedException(Messages.InvalidSalesCycleType);
            cycle = parsed;
        }
        return Success(await _service.ListAsync(q, cycle, ct));
    }

    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _service.GetAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCarSalesContractDto dto, CancellationToken ct)
    {
        var created = await _service.CreateAsync(dto, ct);
        return Created($"{Request.Path}/{created.Id}", ApiResponse<CarSalesContractDto>.Ok(created, Messages.ContractCreated).WithStatus(201));
    }

    /// <summary>بيع سريع: إنشاء العقد ثم اعتماده وتخصيصه وتسليمه وفوترته في معاملة واحدة. يتطلب صلاحية الاعتماد.</summary>
    [HttpPost("QuickSale")]
    public async Task<IActionResult> QuickSale([FromBody] QuickSaleRequestDto dto, CancellationToken ct)
    {
        await Require(ScreenAction.Approve, ct);
        var done = await _service.QuickSaleAsync(dto, ct);
        return Created($"{Request.Path}/{done.Id}", ApiResponse<CarSalesContractDto>.Ok(done, Messages.ContractApprovedAndInvoiced).WithStatus(201));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCarSalesContractDto dto, CancellationToken ct)
        => Success(await _service.UpdateAsync(id, dto, ct), Messages.Updated);

    [HttpPost("{id:guid}/AdvanceStatus")]
    public async Task<IActionResult> Advance(Guid id, [FromBody] AdvanceSalesContractRequestDto dto, CancellationToken ct)
    {
        await Require(dto.TargetStatus == SalesContractStatus.Approved ? ScreenAction.Approve : ScreenAction.Edit, ct);
        return Success(await _service.AdvanceStatusAsync(id, dto, ct), Messages.ContractAdvanced);
    }

    /// <summary>يُكمل العقد القائم من مرحلته الحالية حتى الفوترة (بيانات التسليم الفارغة تُملأ من المشتري). يتطلب صلاحية الاعتماد.</summary>
    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id, [FromBody] CompleteSalesContractRequestDto? dto, CancellationToken ct)
    {
        await Require(ScreenAction.Approve, ct);
        return Success(await _service.CompleteAsync(id, dto, ct), Messages.ContractCompletedAndInvoiced);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] RejectProcurementRequestDto dto, CancellationToken ct)
    {
        await Require(ScreenAction.Approve, ct);
        return Success(await _service.CancelAsync(id, dto.Reason, ct), Messages.ContractCancelled);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await _service.DeleteAsync(id, ct); return Success(Messages.Deleted); }

    private async Task Require(ScreenAction action, CancellationToken ct)
    {
        if (!await _permissions.HasPermissionAsync("car-showroom", action, ct)) throw new ForbiddenException();
    }
}
