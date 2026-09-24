using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.CarShowroom;

[Route("api/v1/caragents"), RequireScreen("car-showroom")]
public class CarAgentsController : CrudController<CarAgentDto, CreateCarAgentDto, UpdateCarAgentDto>
{
    public CarAgentsController(ICarAgentService s) : base(s) { }
}
