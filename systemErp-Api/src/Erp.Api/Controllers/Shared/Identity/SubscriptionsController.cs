using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

[Route("api/v1/subscriptions")]
public class SubscriptionsController : ErpControllerBase
{
    private readonly ISubscriptionService _subscriptions;
    public SubscriptionsController(ISubscriptionService subscriptions) => _subscriptions = subscriptions;

    [HttpGet("plans"), AllowAnonymous]
    public IActionResult Plans() => Success(_subscriptions.GetPlans());

    [HttpGet("current")]
    public async Task<IActionResult> Current(CancellationToken ct) => Success(await _subscriptions.GetCurrentAsync(ct));

    [HttpPost("upgrade"), RequireScreen("user-permissions", ScreenAction.Edit)]
    public async Task<IActionResult> Upgrade([FromBody] UpgradeSubscriptionRequestDto request, CancellationToken ct)
        => Success(await _subscriptions.UpgradeAsync(request, ct), "تم تفعيل الباقة");
}
