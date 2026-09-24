using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.CarShowroom;

[Route("api/v1/cartrims"), RequireScreen("car-showroom")]
public class CarTrimsController : CrudController<CarTrimDto, CreateCarTrimDto, UpdateCarTrimDto>
{
    public CarTrimsController(ICarTrimService s) : base(s) { }
}
