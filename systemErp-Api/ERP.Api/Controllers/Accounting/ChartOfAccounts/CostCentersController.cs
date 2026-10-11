using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

[Route("api/v1/costcenters"), RequireScreen("accounts")]
public class CostCentersController : CrudController<CostCenterDto, CreateCostCenterDto, UpdateCostCenterDto>
{
    public CostCentersController(ICostCenterService s) : base(s) { }
}
