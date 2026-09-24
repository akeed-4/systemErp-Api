using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.CarShowroom;

[Route("api/v1/carprocurementorders"), RequireScreen("car-showroom")]
public class CarProcurementOrdersController : ErpControllerBase
{
    private readonly ICarProcurementService _service;
    public CarProcurementOrdersController(ICarProcurementService service) => _service = service;

    [HttpGet] public async Task<IActionResult> List([FromQuery] PaginationParams q, CancellationToken ct) => Success(await _service.ListAsync(q, ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _service.GetAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCarProcurementOrderDto dto, CancellationToken ct)
    {
        var created = await _service.CreateAsync(dto, ct);
        return Created($"{Request.Path}/{created.Id}", ApiResponse<CarProcurementOrderDto>.Ok(created, "تم إنشاء طلب الشراء").WithStatus(201));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCarProcurementOrderDto dto, CancellationToken ct)
        => Success(await _service.UpdateAsync(id, dto, ct), "تم التعديل");

    [HttpPost("{id:guid}/advance-stage")]
    public async Task<IActionResult> Advance(Guid id, [FromBody] AdvanceProcurementRequestDto dto, CancellationToken ct)
        => Success(await _service.AdvanceStageAsync(id, dto, ct), "تم نقل الأمر للمرحلة التالية");

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectProcurementRequestDto dto, CancellationToken ct)
        => Success(await _service.RejectAsync(id, dto, ct), "تم رفض الأمر");

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await _service.DeleteAsync(id, ct); return Success("تم الحذف"); }
}
