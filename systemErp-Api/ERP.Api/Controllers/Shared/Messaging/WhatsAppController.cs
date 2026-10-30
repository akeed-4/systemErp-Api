using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

/// <summary>
/// تكامل واتساب للأعمال (إضافة whatsapp في الاشتراك): ربط رقم المنشأة عبر Meta Embedded Signup وإدارته، وإشعارات Meta الموقَّعة.
/// الربط والفصل والإدارة بصلاحيات شاشة whatsapp (إدارية افتراضياً).
/// </summary>
[Route("api/v1/whatsapp"), RequireModule(PlatformModules.WhatsApp)]
public class WhatsAppController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.WhatsAppScreenId;
    private const string SendScreen = PermissionCatalog.WhatsAppSendScreenId;

    private readonly IWhatsAppService _whatsApp;
    private readonly IWhatsAppWebhookService _webhooks;
    private readonly IPermissionService _permissions;

    public WhatsAppController(IWhatsAppService whatsApp, IWhatsAppWebhookService webhooks, IPermissionService permissions)
    {
        _whatsApp = whatsApp; _webhooks = webhooks; _permissions = permissions;
    }

    /// <summary>حالة الربط (بلا أسرار): لمن يدير التكامل أو يرسل عبره (زر الإرسال يحتاج معرفة الحالة).</summary>
    [HttpGet("integration"), AuthenticatedOnly]
    public async Task<IActionResult> Integration(CancellationToken ct)
    {
        if (!await _permissions.HasPermissionAsync(Screen, ScreenAction.View, ct) && !await _permissions.HasPermissionAsync(SendScreen, ScreenAction.View, ct))
            throw new ForbiddenException();
        return Success(await _whatsApp.GetIntegrationAsync(ct));
    }

    [HttpGet("signup-config"), RequireScreen(Screen, ScreenAction.Connect)]
    public async Task<IActionResult> SignupConfig(CancellationToken ct) => Success(await _whatsApp.GetSignupConfigAsync(ct));

    [HttpPost("connect"), RequireScreen(Screen, ScreenAction.Connect)]
    public async Task<IActionResult> Connect([FromBody] ConnectWhatsAppDto dto, CancellationToken ct)
        => Success(await _whatsApp.ConnectAsync(dto, ct), Messages.WhatsAppConnected);

    [HttpPost("sync"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> Sync(CancellationToken ct) => Success(await _whatsApp.SyncAsync(ct), Messages.WhatsAppSynced);

    [HttpDelete("integration"), RequireScreen(Screen, ScreenAction.Disconnect)]
    public async Task<IActionResult> Disconnect(CancellationToken ct) => Success(await _whatsApp.DisconnectAsync(ct), Messages.WhatsAppDisconnected);

    [HttpGet("templates"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Templates(CancellationToken ct) => Success(await _whatsApp.ListTemplatesAsync(ct));

    [HttpPost("templates/invoice"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> EnsureInvoiceTemplate(CancellationToken ct)
        => Success(await _whatsApp.EnsureInvoiceTemplateAsync(ct), Messages.WhatsAppTemplateSubmitted);

    // ---------- الإرسال وسجله ----------

    [HttpPost("test"), RequireScreen(SendScreen, ScreenAction.Send)]
    public async Task<IActionResult> SendTest([FromBody] SendWhatsAppTestDto dto, CancellationToken ct)
        => Success(await _whatsApp.SendTestAsync(dto, ct), Messages.WhatsAppSent);

    /// <summary>فاتورة مبيعات مرحّلة PDF (يتحقق السرفر من عرض المبيعات ونطاق السجل).</summary>
    [HttpPost("sales-invoices/{id:guid}/send"), RequireScreen(SendScreen, ScreenAction.Send)]
    public async Task<IActionResult> SendSalesInvoice(Guid id, [FromBody] SendWhatsAppDocumentDto dto, CancellationToken ct)
        => Success(await _whatsApp.SendSalesInvoiceAsync(id, dto, ct), Messages.WhatsAppSent);

    [HttpPost("messages/{id:guid}/resend"), RequireScreen(SendScreen, ScreenAction.Send)]
    public async Task<IActionResult> Resend(Guid id, [FromBody] ResendWhatsAppDto dto, CancellationToken ct)
        => Success(await _whatsApp.ResendAsync(id, dto, ct), Messages.WhatsAppSent);

    [HttpGet("messages"), AuthenticatedOnly]
    public async Task<IActionResult> MessageLog([FromQuery] string? documentType, [FromQuery] Guid? documentId, CancellationToken ct)
    {
        if (!await _permissions.HasPermissionAsync(Screen, ScreenAction.View, ct) && !await _permissions.HasPermissionAsync(SendScreen, ScreenAction.View, ct))
            throw new ForbiddenException();
        return Success(await _whatsApp.ListMessagesAsync(documentType, documentId, ct));
    }

    // ---------- إشعارات Meta (بلا دخول) ----------

    /// <summary>مصافحة اشتراك الـ Webhook: يُعاد hub.challenge نصاً إن طابق رمز التحقق.</summary>
    [HttpGet("webhook"), AllowAnonymous]
    public async Task<IActionResult> VerifyWebhook([FromQuery(Name = "hub.mode")] string? mode, [FromQuery(Name = "hub.verify_token")] string? token,
        [FromQuery(Name = "hub.challenge")] string? challenge, CancellationToken ct)
        => await _webhooks.VerifySubscriptionAsync(mode, token, challenge, ct) is { } echo ? Content(echo, "text/plain") : StatusCode(403);

    /// <summary>أحداث الرسائل والقوالب. التوقيع (X-Hub-Signature-256) يُتحقق منه على الجسم الخام قبل أي معالجة.</summary>
    [HttpPost("webhook"), AllowAnonymous]
    public async Task<IActionResult> Webhook(CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);
        return await _webhooks.HandleAsync(buffer.ToArray(), Request.Headers["X-Hub-Signature-256"].FirstOrDefault(), ct) ? Ok() : Unauthorized();
    }
}
