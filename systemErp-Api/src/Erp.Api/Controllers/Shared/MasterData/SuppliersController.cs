using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

[Route("api/v1/suppliers"), RequireScreen("master-data")]
public class SuppliersController : CrudController<SupplierDto, CreateSupplierDto, UpdateSupplierDto>
{
    public SuppliersController(ISupplierService s) : base(s) { }
}
