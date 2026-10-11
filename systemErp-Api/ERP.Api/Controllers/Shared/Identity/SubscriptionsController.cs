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
    public async Task<IActionResult> Plans(CancellationToken ct) => Success(await _subscriptions.GetPlansAsync(ct));

    [HttpGet("current")]
    public async Task<IActionResult> Current(CancellationToken ct) => Success(await _subscriptions.GetCurrentAsync(ct));

    // الاشتراك والتجديد والترقية بالدفع فقط: POST /payments/subscription (لا تفعيل ذاتي بلا دفع).
}
