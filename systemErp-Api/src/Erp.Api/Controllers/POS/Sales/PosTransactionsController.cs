using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.POS;
using ERP.Core.DTOs.POS;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.POS;

[Route("api/v1/pos/transactions"), RequireScreen("sales")]
public class PosTransactionsController : ErpControllerBase
{
    private readonly IPosSaleService _sales;
    public PosTransactionsController(IPosSaleService sales) => _sales = sales;

    [HttpGet] public async Task<IActionResult> List([FromQuery] Guid? shiftId, [FromQuery] PaginationParams q, CancellationToken ct) => Success(await _sales.ListAsync(shiftId, q, ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _sales.GetAsync(id, ct));

    [HttpGet("by-invoice/{invoiceNumber}")]
    public async Task<IActionResult> ByInvoice(string invoiceNumber, CancellationToken ct) => Success(await _sales.GetByInvoiceNumberAsync(invoiceNumber, ct));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePosTransactionRequestDto dto, CancellationToken ct) => Success(await _sales.UpdateAsync(id, dto, ct), "تم تحديث المعاملة");

    [HttpPost("{id:guid}/void")]
    public async Task<IActionResult> Void(Guid id, CancellationToken ct) => Success(await _sales.VoidAsync(id, ct), "تم إلغاء المعاملة");

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await _sales.DeleteAsync(id, ct); return Success<object?>(null, "تم حذف المعاملة"); }

    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout([FromBody] CheckoutRequestDto dto, CancellationToken ct)
    {
        var created = await _sales.CheckoutAsync(dto, ct);
        return Created($"{Request.Path}/{created.Id}", ApiResponse<PosTransactionDto>.Ok(created, "تمت عملية البيع").WithStatus(201));
    }

    [HttpPost("{id:guid}/submit-zatca")]
    public async Task<IActionResult> SubmitZatca(Guid id, CancellationToken ct) => Success(await _sales.SubmitToZatcaAsync(id, ct));
}
