using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

[Route("api/v1/products"), RequireScreen("master-data")]
public class ProductsController : CrudController<ProductDto, CreateProductDto, UpdateProductDto>
{
    public ProductsController(IProductService s) : base(s) { }
}
