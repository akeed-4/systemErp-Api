using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

[Route("api/v1/stockmovements"), RequireScreen("purchases")]
public class StockMovementsController : ErpControllerBase
{
    private readonly IInventoryService _inventory;
    private readonly IStockAdjustmentService _adjustments;
    public StockMovementsController(IInventoryService inventory, IStockAdjustmentService adjustments) { _inventory = inventory; _adjustments = adjustments; }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? itemId, [FromQuery] Guid? warehouseId, [FromQuery] PaginationParams query, CancellationToken ct)
        => Success(await _inventory.ListMovementsAsync(itemId, warehouseId, query, ct));

    /// <summary>أرصدة الأصناف في المستودعات: لصنف (توزيعه على المستودعات) أو لمستودع (محتواه) أو للكل.</summary>
    [HttpGet("WarehouseStock")]
    public async Task<IActionResult> WarehouseStock([FromQuery] Guid? itemId, [FromQuery] Guid? warehouseId, CancellationToken ct)
        => Success(await _inventory.GetWarehouseStockAsync(itemId, warehouseId, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _inventory.GetMovementAsync(id, ct));

    /// <summary>تسوية جردية يدوية (إضافة/حذف). حركات المبيعات والمشتريات تُسجَّل تلقائياً من مستنداتها.</summary>
    [HttpPost("adjust")]
    public async Task<IActionResult> Adjust([FromBody] RecordStockMovementDto request, CancellationToken ct)
        => Success(await _adjustments.AdjustAsync(request, ct), Messages.AdjustmentRecorded);

    /// <summary>تعديل تسوية يدوية فقط (يُعكس قيدها ويُرحَّل الجديد)؛ حركات المستندات تُعدَّل من مستنداتها.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] RecordStockMovementDto request, CancellationToken ct)
        => Success(await _adjustments.UpdateAsync(id, request, ct), Messages.AdjustmentUpdated);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _adjustments.DeleteAsync(id, ct);
        return Success(Messages.AdjustmentDeletedAndStockRecalculated);
    }
}
