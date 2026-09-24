using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.CarShowroom;

[Route("api/v1/vehicles"), RequireScreen("car-showroom")]
public class VehiclesController : CrudController<VehicleDto, CreateVehicleDto, UpdateVehicleDto>
{
    private readonly IVehicleService _vehicles;
    public VehiclesController(IVehicleService s) : base(s) => _vehicles = s;

    [HttpGet("available")]
    public async Task<IActionResult> Available([FromQuery] string? search, CancellationToken ct) => Success(await _vehicles.ListAvailableAsync(search, ct));

    [HttpPost("calculate-vat")]
    [RequireScreen("car-showroom", ScreenAction.View)]
    public IActionResult CalculateVat([FromBody] CalculateCarVatRequestDto request) => Success(_vehicles.CalculateVat(request));
}
