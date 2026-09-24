using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>فواتير البيع والشراء ومرتجعاتهما. الصلاحية تُحدَّد حسب نوع المستند (sales / sales-returns / purchases).</summary>
[Route("api/v1/invoices")]
public class InvoicesController : ErpControllerBase
{
    private readonly IInvoiceService _invoices;
    private readonly IPermissionService _permissions;

    public InvoicesController(IInvoiceService invoices, IPermissionService permissions)
    {
        _invoices = invoices; _permissions = permissions;
    }

    private static string ScreenFor(InvoiceKind k) => k switch
    {
        InvoiceKind.Sales => "sales", InvoiceKind.SalesReturn => "sales-returns", _ => "purchases",
    };

    private async Task Require(InvoiceKind kind, ScreenAction action, CancellationToken ct)
    {
        if (!await _permissions.HasPermissionAsync(ScreenFor(kind), action, ct)) throw new ForbiddenException();
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] InvoiceKind? kind, [FromQuery] PaginationParams q, CancellationToken ct)
    {
        if (kind.HasValue) await Require(kind.Value, ScreenAction.View, ct);
        else { await Require(InvoiceKind.Sales, ScreenAction.View, ct); await Require(InvoiceKind.Purchase, ScreenAction.View, ct); }
        return Success(await _invoices.ListAsync(kind, q, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var invoice = await _invoices.GetAsync(id, ct);
        await Require(invoice.Kind, ScreenAction.View, ct);
        return Success(invoice);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateInvoiceDto dto, CancellationToken ct)
    {
        await Require(dto.Kind, ScreenAction.Create, ct);
        var created = await _invoices.CreateAsync(dto, ct);
        return Created($"{Request.Path}/{created.Id}", ApiResponse<InvoiceDto>.Ok(created, "تم إنشاء الفاتورة").WithStatus(201));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateInvoiceDto dto, CancellationToken ct)
    {
        await Require(dto.Kind, ScreenAction.Edit, ct);
        return Success(await _invoices.UpdateAsync(id, dto, ct), "تم تعديل الفاتورة");
    }

    [HttpPost("returns")]
    public async Task<IActionResult> CreateReturn([FromBody] CreateReturnInvoiceRequestDto dto, CancellationToken ct)
    {
        var original = await _invoices.GetAsync(dto.OriginalInvoiceId, ct);
        await Require(original.Kind == InvoiceKind.Sales ? InvoiceKind.SalesReturn : InvoiceKind.PurchaseReturn, ScreenAction.Create, ct);
        return Success(await _invoices.CreateReturnAsync(dto, ct), "تم إنشاء المرتجع");
    }

    [HttpPost("{id:guid}/post")]
    public async Task<IActionResult> Post(Guid id, CancellationToken ct)
    {
        await Require((await _invoices.GetAsync(id, ct)).Kind, ScreenAction.Edit, ct);
        return Success(await _invoices.PostDraftAsync(id, ct), "تم ترحيل الفاتورة");
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await Require((await _invoices.GetAsync(id, ct)).Kind, ScreenAction.Delete, ct);
        await _invoices.DeleteAsync(id, ct);
        return Success("تم حذف الفاتورة");
    }

    [HttpPost("{id:guid}/submit-zatca")]
    public async Task<IActionResult> SubmitZatca(Guid id, CancellationToken ct)
    {
        if (!await _permissions.HasPermissionAsync("zatca", ScreenAction.Edit, ct)) throw new ForbiddenException();
        return Success(await _invoices.SubmitToZatcaAsync(id, ct));
    }
}
