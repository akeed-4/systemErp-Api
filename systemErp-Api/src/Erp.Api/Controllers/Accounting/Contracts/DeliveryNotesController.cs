using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

[Route("api/v1/deliverynotes"), RequireScreen("sales")]
public class DeliveryNotesController : ErpControllerBase
{
    private readonly IDeliveryNoteService _notes;
    public DeliveryNotesController(IDeliveryNoteService notes) => _notes = notes;

    [HttpGet] public async Task<IActionResult> List([FromQuery] PaginationParams q, CancellationToken ct) => Success(await _notes.ListAsync(q, ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _notes.GetAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDeliveryNoteDto dto, CancellationToken ct) => Success(await _notes.CreateAsync(dto, ct), "تم إنشاء بيان التسليم");

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDeliveryNoteDto dto, CancellationToken ct)
        => Success(await _notes.UpdateAsync(id, dto, ct), "تم تعديل بيان التسليم");

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await _notes.DeleteAsync(id, ct); return Success("تم حذف بيان التسليم"); }

    [HttpGet("returns/{id:guid}")]
    public async Task<IActionResult> GetReturn(Guid id, CancellationToken ct) => Success(await _notes.GetReturnAsync(id, ct));

    [HttpDelete("returns/{id:guid}")]
    public async Task<IActionResult> DeleteReturn(Guid id, CancellationToken ct) { await _notes.DeleteReturnAsync(id, ct); return Success("تم حذف مرتجع التسليم"); }

    [HttpGet("returns")]
    public async Task<IActionResult> ListReturns([FromQuery] PaginationParams q, CancellationToken ct) => Success(await _notes.ListReturnsAsync(q, ct));

    [HttpPost("returns")]
    public async Task<IActionResult> CreateReturn([FromBody] CreateDeliveryReturnNoteDto dto, CancellationToken ct)
        => Success(await _notes.CreateReturnAsync(dto, ct), "تم إنشاء مرتجع التسليم");

    [HttpPost("{id:guid}/milestone-invoice")]
    public async Task<IActionResult> MilestoneInvoice(Guid id, [FromBody] CreateDeliveryInvoiceRequestDto dto, CancellationToken ct)
        => Success(await _notes.CreateMilestoneInvoiceAsync(id, dto.MilestoneId, ct), "تمت الفوترة");
}
