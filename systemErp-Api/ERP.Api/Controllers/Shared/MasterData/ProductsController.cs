using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

[Route("api/v1/products"), RequireScreen("master-data")]
public class ProductsController : CrudController<ProductDto, CreateProductDto, UpdateProductDto>
{
    private readonly IProductService _products;
    private readonly IInventoryService _inventory;
    public ProductsController(IProductService s, IInventoryService inventory) : base(s) { _products = s; _inventory = inventory; }

    /// <summary>تشغيلات الصنف بأرصدتها (في مستودع أو الكل) مرتبة بالأقرب انتهاءً: اختيار التشغيلة في البيع والتحويل والجرد.</summary>
    [HttpGet("{id:guid}/batches")]
    public async Task<IActionResult> Batches(Guid id, [FromQuery] Guid? warehouseId, [FromQuery] bool includeEmpty, CancellationToken ct)
        => Success(await _inventory.GetBatchesAsync(id, warehouseId, includeEmpty, ct));

    /// <summary>تعديل تاريخي تشغيلة (تصحيح إدخال)؛ رقمها وصنفها ثابتان.</summary>
    [HttpPut("batches/{batchId:guid}")]
    public async Task<IActionResult> UpdateBatch(Guid batchId, [FromBody] UpdateItemBatchDto request, CancellationToken ct)
        => Success(await _inventory.UpdateBatchAsync(batchId, request, ct));

    [HttpPost("import"), RequireScreen("master-data", ScreenAction.Import, Legacy = ScreenAction.Create)]
    public async Task<IActionResult> Import([FromBody] List<CreateProductDto> items, CancellationToken ct)
        => Success(await _products.ImportAsync(items, ct));

    /// <summary>هل دخل الصنف حركة أو مستنداً (يقفل تكلفته المعيارية في الواجهة لمن لا يملك صلاحية إعادة التقييم).</summary>
    [HttpGet("{id:guid}/usage")]
    public async Task<IActionResult> Usage(Guid id, CancellationToken ct)
        => Success(new { used = await _products.IsUsedAsync(id, ct) });
}
