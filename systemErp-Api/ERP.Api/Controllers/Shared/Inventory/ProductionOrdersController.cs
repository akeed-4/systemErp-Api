using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

/// <summary>مستندات تحويل وتصنيع المنتجات (المنتج الصناعي من مواده وخدماته ومصاريفه غير المباشرة).</summary>
[Route("api/v1/production-orders"), RequireScreen("production-orders")]
public class ProductionOrdersController : ErpControllerBase
{
    private readonly IProductionOrderService _orders;
    public ProductionOrdersController(IProductionOrderService orders) => _orders = orders;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PaginationParams query, CancellationToken ct) => Success(await _orders.ListAsync(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _orders.GetAsync(id, ct));

    /// <summary>أسطر مقترحة من مكونات المنتج بالكمية المنتجة، مع أرصدة المواد في مستودع الصرف.</summary>
    [HttpGet("template")]
    public async Task<IActionResult> Template([FromQuery] Guid productId, [FromQuery] decimal quantity, [FromQuery] Guid? warehouseId, CancellationToken ct)
        => Success(await _orders.TemplateAsync(productId, quantity, warehouseId, ct));

    [HttpPost, RequireScreen("production-orders", ScreenAction.Create)]
    public async Task<IActionResult> Create([FromBody] SaveProductionOrderDto request, CancellationToken ct)
    {
        if (request.Post) await EnsurePostAllowedAsync(ct);
        return Success(await _orders.CreateAsync(request, ct));
    }

    [HttpPut("{id:guid}"), RequireScreen("production-orders", ScreenAction.Edit)]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveProductionOrderDto request, CancellationToken ct)
    {
        if (request.Post) await EnsurePostAllowedAsync(ct);
        return Success(await _orders.UpdateAsync(id, request, ct));
    }

    [HttpDelete("{id:guid}"), RequireScreen("production-orders", ScreenAction.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _orders.DeleteAsync(id, ct);
        return Success("تم حذف مسودة التصنيع.");
    }

    [HttpPost("{id:guid}/post"), RequireScreen("production-orders", ScreenAction.Post)]
    public async Task<IActionResult> Post(Guid id, CancellationToken ct) => Success(await _orders.PostAsync(id, ct), "تم ترحيل مستند التصنيع.");

    [HttpPost("{id:guid}/cancel"), RequireScreen("production-orders", ScreenAction.Cancel)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelProductionOrderDto request, CancellationToken ct)
        => Success(await _orders.CancelAsync(id, request.Reason, ct), "تم إلغاء مستند التصنيع وعكس أثره.");

    private async Task EnsurePostAllowedAsync(CancellationToken ct)
    {
        var permissions = HttpContext.RequestServices.GetRequiredService<IPermissionService>();
        if (!await permissions.HasPermissionAsync("production-orders", ScreenAction.Post, ct))
            throw new ForbiddenException("لا تملك صلاحية ترحيل مستندات التصنيع.");
    }
}
