using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.CarShowroom;

[Route("api/v1/carsalescontracts"), RequireScreen("car-showroom")]
public class CarSalesContractsController : ErpControllerBase
{
    private readonly ICarSaleService _service;
    public CarSalesContractsController(ICarSaleService service) => _service = service;

    [HttpGet] public async Task<IActionResult> List([FromQuery] PaginationParams q, CancellationToken ct) => Success(await _service.ListAsync(q, ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _service.GetAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCarSalesContractDto dto, CancellationToken ct)
    {
        var created = await _service.CreateAsync(dto, ct);
        return Created($"{Request.Path}/{created.Id}", ApiResponse<CarSalesContractDto>.Ok(created, "تم إنشاء العقد").WithStatus(201));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCarSalesContractDto dto, CancellationToken ct)
        => Success(await _service.UpdateAsync(id, dto, ct), "تم التعديل");

    [HttpPost("{id:guid}/advance-status")]
    public async Task<IActionResult> Advance(Guid id, [FromBody] AdvanceSalesContractRequestDto dto, CancellationToken ct)
        => Success(await _service.AdvanceStatusAsync(id, dto, ct), "تم نقل العقد للمرحلة التالية");

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] RejectProcurementRequestDto dto, CancellationToken ct)
        => Success(await _service.CancelAsync(id, dto.Reason, ct), "تم إلغاء العقد");

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await _service.DeleteAsync(id, ct); return Success("تم الحذف"); }
}
