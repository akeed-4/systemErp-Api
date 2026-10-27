using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

[Route("api/v1/subscriptions")]
public class SubscriptionsController : ErpControllerBase
{
    private readonly ISubscriptionService _subscriptions;
    private readonly IPromoCodeService _promos;
    public SubscriptionsController(ISubscriptionService subscriptions, IPromoCodeService promos)
    {
        _subscriptions = subscriptions; _promos = promos;
    }

    [HttpGet("plans"), AllowAnonymous]
    public async Task<IActionResult> Plans(CancellationToken ct) => Success(await _subscriptions.GetPlansAsync(ct));

    [HttpGet("current")]
    public async Task<IActionResult> Current(CancellationToken ct) => Success(await _subscriptions.GetCurrentAsync(ct));

    [HttpGet("usage")]
    public async Task<IActionResult> Usage(CancellationToken ct) => Success(await _subscriptions.GetUsageAsync(ct));

    /// <summary>تسعير الباقة قبل الدفع مع رمز خصم اختياري (الرمز غير الصالح يُرفض بسببه).</summary>
    [HttpPost("quote"), RequireScreen("user-permissions", ScreenAction.Edit)]
    public async Task<IActionResult> Quote([FromBody] SubscriptionQuoteRequestDto dto, CancellationToken ct) => Success(await _promos.QuoteAsync(dto, ct));

    // الاشتراك والتجديد والترقية بالدفع فقط: POST /payments/subscription (لا تفعيل ذاتي بلا دفع).
}
