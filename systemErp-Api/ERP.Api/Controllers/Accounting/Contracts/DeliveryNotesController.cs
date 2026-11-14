using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

[Route("api/v1/deliverynotes"), RequireScreen("sales"), ScopedRecord("delivery-note")]
[RequireModule(PlatformModules.Accounting)]
[RequireFeature(PlanFeatures.CommercialCycle)]
public class DeliveryNotesController : ErpControllerBase
{
    private readonly IDeliveryNoteService _notes;
    public DeliveryNotesController(IDeliveryNoteService notes) => _notes = notes;

    [HttpGet] public async Task<IActionResult> List([FromQuery] PaginationParams q, CancellationToken ct) => Success(await _notes.ListAsync(q, ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _notes.GetAsync(id, ct));

    // سندات التسليم ومرتجعاتها تخضع لسياسات الاعتماد (تُحجز العملية حتى اعتمادها)
    private const string Approval = "delivery-notes";
    private const string ReturnApproval = "delivery-returns";

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDeliveryNoteDto dto, CancellationToken ct)
    {
        if (await HoldForApprovalAsync(Approval, ApprovalDocumentCatalog.Create, null, dto, ct) is { } held) return held;
        return Success(await _notes.CreateAsync(dto, ct), Messages.DeliveryNoteCreated);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDeliveryNoteDto dto, CancellationToken ct)
    {
        if (await HoldForApprovalAsync(Approval, ApprovalDocumentCatalog.Edit, id, dto, ct) is { } held) return held;
        return Success(await _notes.UpdateAsync(id, dto, ct), Messages.DeliveryNoteUpdated);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (await HoldForApprovalAsync(Approval, ApprovalDocumentCatalog.Delete, id, null, ct) is { } held) return held;
        await _notes.DeleteAsync(id, ct);
        return Success(Messages.DeliveryNoteDeleted);
    }

    [HttpGet("returns/{id:guid}")]
    public async Task<IActionResult> GetReturn(Guid id, CancellationToken ct) => Success(await _notes.GetReturnAsync(id, ct));

    [HttpDelete("returns/{id:guid}")]
    public async Task<IActionResult> DeleteReturn(Guid id, CancellationToken ct)
    {
        if (await HoldForApprovalAsync(ReturnApproval, ApprovalDocumentCatalog.Delete, id, null, ct) is { } held) return held;
        await _notes.DeleteReturnAsync(id, ct);
        return Success(Messages.DeliveryReturnDeleted);
    }

    [HttpGet("returns")]
    public async Task<IActionResult> ListReturns([FromQuery] PaginationParams q, CancellationToken ct) => Success(await _notes.ListReturnsAsync(q, ct));

    [HttpPost("returns")]
    public async Task<IActionResult> CreateReturn([FromBody] CreateDeliveryReturnNoteDto dto, CancellationToken ct)
    {
        if (await HoldForApprovalAsync(ReturnApproval, ApprovalDocumentCatalog.Create, null, dto, ct) is { } held) return held;
        return Success(await _notes.CreateReturnAsync(dto, ct), Messages.DeliveryReturnCreated);
    }

    [HttpPost("{id:guid}/MilestoneInvoice")]
    public async Task<IActionResult> MilestoneInvoice(Guid id, [FromBody] CreateDeliveryInvoiceRequestDto dto, CancellationToken ct)
        => Success(await _notes.CreateMilestoneInvoiceAsync(id, dto.MilestoneId, dto.Date, ct), Messages.Invoiced);

    [HttpPost("{id:guid}/Invoice")]
    public async Task<IActionResult> Invoice(Guid id, [FromBody] CreateDeliveryInvoiceRequestDto? dto, CancellationToken ct)
        => Success(await _notes.CreateInvoiceAsync(id, dto?.Date, dto?.SupplierId, ct), Messages.InvoiceIssued);
}
