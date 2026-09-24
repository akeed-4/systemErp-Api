using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.POS;
using ERP.Core.DTOs.POS;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.POS;

[Route("api/v1/pos/returns"), RequireScreen("sales-returns")]
public class PosReturnsController : ErpControllerBase
{
    private readonly IPosSaleReturnService _returns;
    public PosReturnsController(IPosSaleReturnService returns) => _returns = returns;

    [HttpGet] public async Task<IActionResult> List([FromQuery] PaginationParams q, CancellationToken ct) => Success(await _returns.ListAsync(q, ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _returns.GetAsync(id, ct));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePosReturnRequestDto dto, CancellationToken ct) => Success(await _returns.UpdateAsync(id, dto, ct), "تم تحديث المرتجع");

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await _returns.DeleteAsync(id, ct); return Success<object?>(null, "تم حذف المرتجع"); }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePosReturnRequestDto dto, CancellationToken ct) => Success(await _returns.CreateAsync(dto, ct), "تم إنشاء المرتجع");
}
