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
    /// <summary>القائمة بخيارات DevExtreme وتُعيد <c>LoadResult</c> مباشرة دون مغلّف ApiResponse.</summary>
    [HttpGet("load")] public async Task<IActionResult> Load(DataSourceLoadOptions loadOptions, CancellationToken ct) => Ok(await _journal.LoadAsync(loadOptions, ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _journal.GetAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateJournalEntryDto dto, CancellationToken ct)
        => Success(await _journal.CreateManualAsync(dto, ct), Messages.JournalEntryPosted);

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateJournalEntryDto dto, CancellationToken ct)
        => Success(await _journal.UpdateManualAsync(id, dto, ct), Messages.JournalEntryUpdated);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await _journal.DeleteManualAsync(id, ct); return Success(Messages.JournalEntryDeleted); }

    [HttpPost("{id:guid}/reverse")]
    public async Task<IActionResult> Reverse(Guid id, CancellationToken ct) => Success(await _journal.ReverseAsync(id, ct), Messages.JournalEntryReversed);
}
