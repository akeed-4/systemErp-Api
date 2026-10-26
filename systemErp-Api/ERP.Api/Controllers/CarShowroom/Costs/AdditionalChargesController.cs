using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.CarShowroom;

/// <summary>أسطر المصاريف والتكاليف الإضافية على مستندات السيارات (أمر شراء، عقد بيع، فاتورة) أو على السيارة مباشرة.</summary>
[Route("api/v1/additionalcharges"), RequireScreen("car-showroom")]
[RequireModule(PlatformModules.CarShowroom)]
public class AdditionalChargesController : ErpControllerBase
{
    private readonly IAdditionalChargeService _service;
    public AdditionalChargesController(IAdditionalChargeService service) => _service = service;

    /// <summary>أسطر مستند (documentType + documentId) أو أسطر سيارة (vehicleId).</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? documentType, [FromQuery] Guid? documentId, [FromQuery] Guid? vehicleId, CancellationToken ct)
        => Success(await _service.ListAsync(documentType, documentId, vehicleId, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _service.GetAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAdditionalChargeLineDto dto, CancellationToken ct)
    {
        var created = await _service.CreateAsync(dto, ct);
        return Created($"{Request.Path}/{created.Id}", ApiResponse<AdditionalChargeLineDto>.Ok(created, Messages.CreatedSuccessfully).WithStatus(201));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAdditionalChargeLineDto dto, CancellationToken ct)
        => Success(await _service.UpdateAsync(id, dto, ct), Messages.UpdatedSuccessfully);

    [HttpPost("{id:guid}/cancel")]
    [RequireScreen("car-showroom", ScreenAction.Cancel, Legacy = ScreenAction.Edit)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] ChargeActionReasonDto? dto, CancellationToken ct)
        => Success(await _service.CancelAsync(id, dto?.Reason, ct), Messages.ChargeLineCancelled);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] string? reason, CancellationToken ct)
    {
        await _service.DeleteAsync(id, reason, ct);
        return Success(Messages.DeletedSuccessfully);
    }
}
