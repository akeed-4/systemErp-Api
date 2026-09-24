using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

[Route("api/v1/journalentries"), RequireScreen("accounts")]
public class JournalEntriesController : ErpControllerBase
{
    private readonly IJournalService _journal;
    public JournalEntriesController(IJournalService journal) => _journal = journal;

    [HttpGet] public async Task<IActionResult> List([FromQuery] PaginationParams q, CancellationToken ct) => Success(await _journal.ListAsync(q, ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _journal.GetAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateJournalEntryDto dto, CancellationToken ct)
        => Success(await _journal.CreateManualAsync(dto, ct), "تم ترحيل القيد");

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateJournalEntryDto dto, CancellationToken ct)
        => Success(await _journal.UpdateManualAsync(id, dto, ct), "تم تعديل القيد");

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await _journal.DeleteManualAsync(id, ct); return Success("تم حذف القيد"); }

    [HttpPost("{id:guid}/reverse")]
    public async Task<IActionResult> Reverse(Guid id, CancellationToken ct) => Success(await _journal.ReverseAsync(id, ct), "تم عكس القيد");
}
