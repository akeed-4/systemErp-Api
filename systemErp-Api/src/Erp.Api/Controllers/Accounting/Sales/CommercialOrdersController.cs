using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

[Route("api/v1/commercialorders"), RequireScreen("sales")]
public class CommercialOrdersController : CrudController<CommercialOrderDto, CreateCommercialOrderDto, UpdateCommercialOrderDto>
{
    private readonly ICommercialOrderService _orders;
    public CommercialOrdersController(ICommercialOrderService s) : base(s) => _orders = s;

    [HttpPost("{id:guid}/convert-to-invoice")]
    public async Task<IActionResult> ToInvoice(Guid id, CancellationToken ct) => Success(await _orders.ConvertToInvoiceAsync(id, ct), "تم إنشاء فاتورة مسودة");
}
