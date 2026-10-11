using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

[Route("api/v1/unitsofmeasure"), RequireScreen("master-data")]
public class UnitsOfMeasureController : CrudController<UnitOfMeasureDto, CreateUnitOfMeasureDto, UpdateUnitOfMeasureDto>
{
    public UnitsOfMeasureController(IUnitOfMeasureService s) : base(s) { }
}
