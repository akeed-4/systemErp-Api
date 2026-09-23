using ERP.Api.Controllers.Common;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class UnitsOfMeasureController : BaseCrudController<UnitOfMeasure>
{
    public UnitsOfMeasureController(ApplicationDbContext context) : base(context)
    {
    }
}
