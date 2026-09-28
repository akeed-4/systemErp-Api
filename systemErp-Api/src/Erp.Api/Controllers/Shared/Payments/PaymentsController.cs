using System.Text.Json;
using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

/// <summary>الدفع الإلكتروني عبر Paymob: روابط دفع الفواتير، دفع نقطة البيع، اشتراكات المنشآت، وإشعارات Paymob الموقَّعة.</summary>
[Route("api/v1/payments")]
public class PaymentsController : ErpControllerBase
{
    private readonly IOnlinePaymentService _payments;
    public PaymentsController(IOnlinePaymentService payments) => _payments = payments;

    [HttpPost("InvoiceLink"), RequireScreen("sales", ScreenAction.Create)]
    public async Task<IActionResult> InvoiceLink([FromBody] CreateInvoicePaymentLinkDto dto, CancellationToken ct)
        => Success(await _payments.CreateInvoicePaymentLinkAsync(dto, ct), "تم إنشاء رابط الدفع");

    [HttpPost("pos"), RequireScreen("sales", ScreenAction.Create)]
    public async Task<IActionResult> Pos([FromBody] CreatePosPaymentDto dto, CancellationToken ct)
        => Success(await _payments.CreatePosPaymentAsync(dto, ct));

    [HttpPost("subscription"), RequireScreen("user-permissions", ScreenAction.Edit)]
    public async Task<IActionResult> Subscription([FromBody] CreateSubscriptionCheckoutDto dto, CancellationToken ct)
        => Success(await _payments.CreateSubscriptionCheckoutAsync(dto, ct));

    [HttpGet, RequireScreen("sales")]
    public async Task<IActionResult> List([FromQuery] Guid? referenceId, CancellationToken ct) => Success(await _payments.ListAsync(referenceId, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _payments.GetAsync(id, ct));

    [HttpPost("{id:guid}/cancel"), RequireScreen("sales", ScreenAction.Edit)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) => Success(await _payments.CancelAsync(id, ct), "تم إلغاء عملية الدفع");

    /// <summary>صفحة نتيجة الدفع للعميل بعد العودة من Paymob (بلا دخول).</summary>
    [HttpGet("{id:guid}/public"), AllowAnonymous]
    public async Task<IActionResult> PublicStatus(Guid id, CancellationToken ct) => Success(await _payments.GetPublicStatusAsync(id, ct));

    /// <summary>إشعار Paymob (Transaction processed callback). يُرفض أي إشعار بتوقيع HMAC غير صالح.</summary>
    [HttpPost("paymob/webhook"), AllowAnonymous]
    public async Task<IActionResult> PaymobWebhook([FromBody] JsonElement payload, [FromQuery] string? hmac, CancellationToken ct)
        => await _payments.HandleWebhookAsync(payload, hmac, ct) ? Ok() : Unauthorized();

    // ---------- إعدادات حساب Paymob للمنشأة ----------
    [HttpGet("GatewaySettings"), RequireScreen("user-permissions")]
    public async Task<IActionResult> Settings(CancellationToken ct) => Success(await _payments.GetSettingsAsync(ct));

    [HttpPut("GatewaySettings"), RequireScreen("user-permissions", ScreenAction.Edit)]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdatePaymentGatewaySettingsDto dto, CancellationToken ct)
        => Success(await _payments.UpdateSettingsAsync(dto, ct), "تم حفظ إعدادات بوابة الدفع");
}
