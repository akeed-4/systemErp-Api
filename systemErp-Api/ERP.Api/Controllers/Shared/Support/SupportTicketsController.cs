using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

/// <summary>تذاكر الدعم الفني للمنشأة الحالية (لأي مستخدم مسجّل فيها، مثل الإشعارات).</summary>
[Route("api/v1/supporttickets")]
public class SupportTicketsController : CrudController<SupportTicketDto, CreateSupportTicketDto, UpdateSupportTicketDto>
{
    private readonly ISupportTicketService _tickets;
    public SupportTicketsController(ISupportTicketService tickets) : base(tickets) => _tickets = tickets;

    [HttpPost("{id:guid}/replies")]
    public async Task<IActionResult> Reply(Guid id, [FromBody] AddSupportTicketReplyDto request, CancellationToken ct)
        => Success(await _tickets.AddReplyAsync(id, request, ct), Messages.ReplySent);

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeSupportTicketStatusDto request, CancellationToken ct)
        => Success(await _tickets.ChangeStatusAsync(id, request, ct), Messages.TicketStatusUpdated);
}
