using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

[Route("api/v1/stockmovements"), RequireScreen("purchases")]
public class StockMovementsController : ErpControllerBase
{
    private readonly IInventoryService _inventory;
    public StockMovementsController(IInventoryService inventory) => _inventory = inventory;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? itemId, [FromQuery] PaginationParams query, CancellationToken ct)
        => Success(await _inventory.ListMovementsAsync(itemId, query, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _inventory.GetMovementAsync(id, ct));

    /// <summary>تسوية جردية يدوية (إضافة/حذف). حركات المبيعات والمشتريات تُسجَّل تلقائياً من مستنداتها.</summary>
    [HttpPost("adjust")]
    public async Task<IActionResult> Adjust([FromBody] RecordStockMovementDto request, CancellationToken ct)
    {
        if (request.Type is not (StockMovementType.AdjustmentIn or StockMovementType.AdjustmentOut))
            throw new ValidationFailedException("التسوية اليدوية: adjustment_in أو adjustment_out فقط.");
        request.SourceType = "manual";
        return Success(await _inventory.RecordMovementAsync(request, ct), "تم تسجيل التسوية");
    }

    /// <summary>تعديل تسوية يدوية فقط؛ حركات المستندات تُعدَّل من مستنداتها.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] RecordStockMovementDto request, CancellationToken ct)
        => Success(await _inventory.UpdateMovementAsync(id, request, ct), "تم تعديل التسوية");

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _inventory.DeleteMovementAsync(id, ct);
        return Success("تم حذف التسوية وإعادة احتساب الرصيد");
    }
}
