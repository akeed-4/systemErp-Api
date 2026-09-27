using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

[Route("api/v1/products"), RequireScreen("master-data")]
public class ProductsController : CrudController<ProductDto, CreateProductDto, UpdateProductDto>
{
    private readonly IProductService _products;
    public ProductsController(IProductService s) : base(s) => _products = s;

    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] List<CreateProductDto> items, CancellationToken ct)
        => Success(await _products.ImportAsync(items, ct));
}
