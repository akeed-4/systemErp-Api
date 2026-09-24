using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.CarShowroom;

[Route("api/v1/carmodels"), RequireScreen("car-showroom")]
public class CarModelsController : CrudController<CarModelDto, CreateCarModelDto, UpdateCarModelDto>
{
    public CarModelsController(ICarModelService s) : base(s) { }
}
