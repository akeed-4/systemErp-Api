using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.CarShowroom;

[Route("api/v1/carcolors"), RequireScreen("car-showroom")]
public class CarColorsController : CrudController<CarColorDto, CreateCarColorDto, UpdateCarColorDto>
{
    public CarColorsController(ICarColorService s) : base(s) { }
}
