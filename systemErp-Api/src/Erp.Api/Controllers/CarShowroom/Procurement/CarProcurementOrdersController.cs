using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.CarShowroom;

[Route("api/v1/carprocurementorders"), RequireScreen("car-showroom")]
[RequireModule(PlatformModules.CarShowroom)]
public class CarProcurementOrdersController : ErpControllerBase
{
    private readonly ICarProcurementService _service;
    private readonly IVehicleService _vehicles;
    private readonly IPermissionService _permissions;
    public CarProcurementOrdersController(ICarProcurementService service, IVehicleService vehicles, IPermissionService permissions)
    {
        _service = service; _vehicles = vehicles; _permissions = permissions;
    }

    /// <summary>purchaseCycle اختياري: individual | corporate | bank_lease.</summary>
    [HttpGet] public async Task<IActionResult> List([FromQuery] PaginationParams q, [FromQuery] string? purchaseCycle, CancellationToken ct)
        => Success(await _service.ListAsync(q, purchaseCycle, ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _service.GetAsync(id, ct));

    /// <summary>المركبات الواردة من هذا الطلب (بحث/فرز/صفحات) — لعرض دفعة الاستلام دون تحميل كل المركبات.</summary>
    [HttpGet("{id:guid}/vehicles")]
    public async Task<IActionResult> Vehicles(Guid id, [FromQuery] PaginationParams q, CancellationToken ct)
        => Success(await _vehicles.ListByProcurementOrderAsync(id, q, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCarProcurementOrderDto dto, CancellationToken ct)
    {
        var created = await _service.CreateAsync(dto, ct);
        return Created($"{Request.Path}/{created.Id}", ApiResponse<CarProcurementOrderDto>.Ok(created, Messages.PurchaseOrderCreated).WithStatus(201));
    }

    /// <summary>شراء مركبة واحدة بخطوة واحدة (أمر ببند واحد يمرّ بكل المراحل حتى الفاتورة والقيد). يتطلب صلاحية الاعتماد.</summary>
    [HttpPost("QuickPurchase")]
    public async Task<IActionResult> QuickPurchase([FromBody] QuickCarPurchaseRequestDto dto, CancellationToken ct)
    {
        await Require(ScreenAction.Approve, ct);
        var done = await _service.QuickPurchaseAsync(dto, ct);
        return Created($"{Request.Path}/{done.Id}", ApiResponse<CarProcurementOrderDto>.Ok(done, Messages.VehiclePurchaseInvoiceRecorded).WithStatus(201));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCarProcurementOrderDto dto, CancellationToken ct)
        => Success(await _service.UpdateAsync(id, dto, ct), Messages.Updated);

    [HttpPost("{id:guid}/AdvanceStage")]
    public async Task<IActionResult> Advance(Guid id, [FromBody] AdvanceProcurementRequestDto dto, CancellationToken ct)
    {
        var isApprovalStage = dto.TargetStage is ProcurementStage.RequisitionApproved or ProcurementStage.RfqApproved;
        await Require(isApprovalStage ? ScreenAction.Approve : ScreenAction.Edit, ct);
        return Success(await _service.AdvanceStageAsync(id, dto, ct), Messages.OrderAdvanced);
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectProcurementRequestDto dto, CancellationToken ct)
    {
        await Require(ScreenAction.Approve, ct);
        return Success(await _service.RejectAsync(id, dto, ct), Messages.OrderRejected);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await _service.DeleteAsync(id, ct); return Success(Messages.Deleted); }

    private async Task Require(ScreenAction action, CancellationToken ct)
    {
        if (!await _permissions.HasPermissionAsync("car-showroom", action, ct)) throw new ForbiddenException();
    }
}
