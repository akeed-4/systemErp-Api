using ERP.Api.Controllers.Common;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class ProductsController : BaseCrudController<Product>
{
    public ProductsController(ApplicationDbContext context) : base(context)
    {
    }
}
