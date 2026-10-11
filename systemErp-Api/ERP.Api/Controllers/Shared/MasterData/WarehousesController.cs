using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

[Route("api/v1/warehouses"), RequireScreen("master-data")]
public class WarehousesController : CrudController<WarehouseDto, CreateWarehouseDto, UpdateWarehouseDto>
{
    public WarehousesController(IWarehouseService s) : base(s) { }
}
