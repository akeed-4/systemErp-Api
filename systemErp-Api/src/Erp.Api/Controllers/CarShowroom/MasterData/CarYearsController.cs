using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.CarShowroom;

[Route("api/v1/caryears"), RequireScreen("car-showroom")]
public class CarYearsController : CrudController<CarYearModelDto, CreateCarYearModelDto, UpdateCarYearModelDto>
{
    public CarYearsController(ICarYearService s) : base(s) { }
}
