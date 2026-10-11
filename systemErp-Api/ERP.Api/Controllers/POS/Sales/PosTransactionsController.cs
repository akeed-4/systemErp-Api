using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.POS;
using ERP.Core.DTOs.POS;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.POS;

[Route("api/v1/pos/transactions"), RequireScreen("sales")]
[RequireModule(PlatformModules.Pos)]
public class PosTransactionsController : ErpControllerBase
{
    private readonly IPosSaleService _sales;
    public PosTransactionsController(IPosSaleService sales) => _sales = sales;

    [HttpGet] public async Task<IActionResult> List([FromQuery] Guid? shiftId, [FromQuery] PaginationParams q, CancellationToken ct) => Success(await _sales.ListAsync(shiftId, q, ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _sales.GetAsync(id, ct));

    [HttpGet("ByInvoice/{invoiceNumber}")]
    public async Task<IActionResult> ByInvoice(string invoiceNumber, CancellationToken ct) => Success(await _sales.GetByInvoiceNumberAsync(invoiceNumber, ct));

    [HttpPost("{id:guid}/void")]
    public async Task<IActionResult> Void(Guid id, CancellationToken ct) => Success(await _sales.VoidAsync(id, ct), Messages.TransactionVoided);

    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout([FromBody] CheckoutRequestDto dto, CancellationToken ct)
    {
        var created = await _sales.CheckoutAsync(dto, ct);
        return Created($"{Request.Path}/{created.Id}", ApiResponse<PosTransactionDto>.Ok(created, Messages.SaleCompleted).WithStatus(201));
    }

    /// <summary>تسعير السلة من السرفر قبل الدفع (عروض/كوبون/ولاء/ضريبة) — بلا أي أثر.</summary>
    [HttpPost("quote")]
    public async Task<IActionResult> Quote([FromBody] CheckoutRequestDto dto, CancellationToken ct) => Success(await _sales.QuoteAsync(dto, ct));

    [HttpPost("{id:guid}/SubmitZatca")]
    public async Task<IActionResult> SubmitZatca(Guid id, CancellationToken ct) => Success(await _sales.SubmitToZatcaAsync(id, ct));
}
