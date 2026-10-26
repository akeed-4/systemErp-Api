using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

[Route("api/v1/products"), RequireScreen("master-data")]
public class ProductsController : CrudController<ProductDto, CreateProductDto, UpdateProductDto>
{
    private readonly IProductService _products;
    public ProductsController(IProductService s) : base(s) => _products = s;

    [HttpPost("import"), RequireScreen("master-data", ScreenAction.Import, Legacy = ScreenAction.Create)]
    public async Task<IActionResult> Import([FromBody] List<CreateProductDto> items, CancellationToken ct)
        => Success(await _products.ImportAsync(items, ct));

    /// <summary>هل دخل الصنف حركة أو مستنداً (يقفل تكلفته المعيارية في الواجهة لمن لا يملك صلاحية إعادة التقييم).</summary>
    [HttpGet("{id:guid}/usage")]
    public async Task<IActionResult> Usage(Guid id, CancellationToken ct)
        => Success(new { used = await _products.IsUsedAsync(id, ct) });
}
