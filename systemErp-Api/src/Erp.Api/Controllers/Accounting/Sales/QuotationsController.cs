using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

[Route("api/v1/quotations"), RequireScreen("sales")]
public class QuotationsController : CrudController<QuotationDto, CreateQuotationDto, UpdateQuotationDto>
{
    private readonly IQuotationService _quotations;
    public QuotationsController(IQuotationService s) : base(s) => _quotations = s;

    [HttpPost("{id:guid}/convert-to-invoice")]
    public async Task<IActionResult> ToInvoice(Guid id, CancellationToken ct) => Success(await _quotations.ConvertToInvoiceAsync(id, ct), "تم إنشاء فاتورة مسودة");

    [HttpPost("{id:guid}/convert-to-order")]
    public async Task<IActionResult> ToOrder(Guid id, CancellationToken ct) => Success(await _quotations.ConvertToOrderAsync(id, ct), "تم إنشاء الأمر");
}
