using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

[Route("api/v1/customers"), RequireScreen("master-data")]
public class CustomersController : CrudController<CustomerDto, CreateCustomerDto, UpdateCustomerDto>
{
    public CustomersController(ICustomerService s) : base(s) { }
}
