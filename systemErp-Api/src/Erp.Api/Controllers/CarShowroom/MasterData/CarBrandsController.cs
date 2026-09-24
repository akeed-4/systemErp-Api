using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.CarShowroom;

[Route("api/v1/carbrands"), RequireScreen("car-showroom")]
public class CarBrandsController : CrudController<CarBrandDto, CreateCarBrandDto, UpdateCarBrandDto>
{
    public CarBrandsController(ICarBrandService s) : base(s) { }
}
