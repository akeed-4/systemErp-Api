using ERP.Api.Controllers.Common;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class CostCentersController : BaseCrudController<CostCenter>
{
    public CostCentersController(ApplicationDbContext context) : base(context)
    {
    }
}
