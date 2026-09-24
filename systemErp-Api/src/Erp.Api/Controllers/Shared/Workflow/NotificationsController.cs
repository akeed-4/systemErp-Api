using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

[Route("api/v1/notifications")]
public class NotificationsController : ErpControllerBase
{
    private readonly INotificationService _notifications;
    public NotificationsController(INotificationService notifications) => _notifications = notifications;

    [HttpGet] public async Task<IActionResult> Mine(CancellationToken ct) => Success(await _notifications.ListMineAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Notify([FromBody] NotifyRequestDto request, CancellationToken ct)
        => Success(await _notifications.NotifyAsync(request, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _notifications.GetAsync(id, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await _notifications.DeleteAsync(id, ct); return Success("تم"); }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct) { await _notifications.MarkAsReadAsync(id, ct); return Success("تم"); }

    [HttpPost("read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken ct) { await _notifications.MarkAllAsReadAsync(ct); return Success("تم"); }

    [HttpDelete]
    public async Task<IActionResult> Clear(CancellationToken ct) { await _notifications.ClearAllAsync(ct); return Success("تم"); }
}
