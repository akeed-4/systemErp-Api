using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Service.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ERP.Service.Services.Shared;

/// <summary>
/// إشعارات المزوّد (بلا دخول). التوقيع يُتحقق منه قبل أي قراءة، ثم تُعرف المنشأة من رقمها (أو حسابها لإشعار القوالب) في الدليل،
/// وتُعالج أحداث كل منشأة في نطاق خدمات مستقل مضبوط عليها، فلا يمسّ إشعار منشأة بيانات غيرها ولو جمع الطلب أكثر من منشأة.
/// الإشعار المكرر أو المتأخر لا ينزل بالحالة (المزوّد يعيد الإرسال أياماً، ولا يضمن الترتيب).
/// </summary>
public class WhatsAppWebhookService : IWhatsAppWebhookService
{
    private readonly ErpDbContext _db;
    private readonly ISecretProtector _secrets;
    private readonly IWhatsAppProvider _provider;
    private readonly WhatsAppOptions _server;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<WhatsAppWebhookService> _log;

    public WhatsAppWebhookService(ErpDbContext db, ISecretProtector secrets, IWhatsAppProvider provider, IOptions<WhatsAppOptions> server,
        IServiceScopeFactory scopes, ILogger<WhatsAppWebhookService> log)
    {
        _db = db; _secrets = secrets; _provider = provider; _server = server.Value; _scopes = scopes; _log = log;
    }

    public async Task<string?> VerifySubscriptionAsync(string? mode, string? verifyToken, string? challenge, CancellationToken ct = default)
    {
        var (app, _) = await PlatformWhatsApp.ResolveAsync(_db, _server, _secrets, ct);
        if (mode != "subscribe" || string.IsNullOrEmpty(app.VerifyToken) || string.IsNullOrEmpty(verifyToken) || string.IsNullOrEmpty(challenge)) return null;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(verifyToken), Encoding.UTF8.GetBytes(app.VerifyToken)) ? challenge : null;
    }

    public async Task<bool> HandleAsync(byte[] body, string? signature, CancellationToken ct = default)
    {
        var (app, _) = await PlatformWhatsApp.ResolveAsync(_db, _server, _secrets, ct);
        if (!_provider.VerifySignature(app, body, signature)) return false;

        WhatsAppWebhookBatch batch;
        try { batch = _provider.ParseWebhook(body); }
        catch (JsonException e)
        {
            // موقَّع لكنه غير مقروء: يُقبل حتى لا يعيده المزوّد أياماً
            _log.LogWarning(e, "Unreadable WhatsApp webhook payload");
            return true;
        }
        if (batch.Statuses.Count == 0 && batch.Templates.Count == 0) return true;

        var phoneIds = batch.Statuses.Select(s => s.PhoneNumberId).Where(p => p.Length > 0).Distinct().ToList();
        var wabaIds = batch.Templates.Select(t => t.WabaId).Where(w => w.Length > 0).Distinct().ToList();
        var links = await _db.Catalog.Set<TenantWhatsAppIntegration>().IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.Status != WhatsAppIntegrationStatus.Disconnected && (phoneIds.Contains(i.PhoneNumberId) || wabaIds.Contains(i.WabaId)))
            .Select(i => new { i.TenantId, i.PhoneNumberId, i.WabaId }).ToListAsync(ct);

        foreach (var tenant in links.GroupBy(l => l.TenantId))
        {
            var phones = tenant.Select(l => l.PhoneNumberId).ToHashSet();
            var wabas = tenant.Select(l => l.WabaId).ToHashSet();
            using var scope = _scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenant.Key);
            await ApplyAsync(scope.ServiceProvider,
                batch.Statuses.Where(s => phones.Contains(s.PhoneNumberId)).ToList(),
                batch.Templates.Where(t => wabas.Contains(t.WabaId)).ToList(), ct);
        }
        return true;
    }

    private static async Task ApplyAsync(IServiceProvider services, List<WhatsAppStatusEvent> statuses, List<WhatsAppTemplateEvent> templates, CancellationToken ct)
    {
        var db = services.GetRequiredService<ErpDbContext>();
        var failed = new List<WhatsAppMessage>();

        var ids = statuses.Select(s => s.MessageId).Where(id => id.Length > 0).Distinct().ToList();
        var messages = ids.Count == 0 ? new List<WhatsAppMessage>()
            : await db.Set<WhatsAppMessage>().Where(m => m.ProviderMessageId != null && ids.Contains(m.ProviderMessageId)).ToListAsync(ct);
        foreach (var e in statuses.OrderBy(s => s.Timestamp))
        {
            var m = messages.FirstOrDefault(x => x.ProviderMessageId == e.MessageId);
            if (m == null) continue;
            var wasFailed = m.Status == WhatsAppMessageStatus.Failed;
            Apply(m, e);
            if (!wasFailed && m.Status == WhatsAppMessageStatus.Failed) failed.Add(m);
        }

        if (templates.Count > 0)
        {
            var integration = await db.Set<TenantWhatsAppIntegration>().FirstOrDefaultAsync(ct);
            var update = templates.LastOrDefault(t => t.TemplateName == integration?.InvoiceTemplateName
                                                      && WhatsAppService.SameLanguage(t.Language, integration.InvoiceTemplateLanguage ?? WhatsAppService.DefaultLanguage));
            if (integration != null && update != null)
            {
                integration.InvoiceTemplateStatus = update.Status;
                if (update.Reason != null) integration.LastError = string.Format(Messages.WhatsAppTemplateRejected, update.Reason);
                else if (update.Status == "APPROVED") integration.LastError = null;
            }

            // نصوص رسالة الفاتورة المخصصة: الاعتماد يجعل أحدثها هو المرسَل، والرفض يُعرض سببه للمستخدم
            var names = templates.Select(t => t.TemplateName).Distinct().ToList();
            var custom = await db.Set<WhatsAppInvoiceTemplate>().Where(t => names.Contains(t.Name)).ToListAsync(ct);
            foreach (var t in custom)
                if (templates.LastOrDefault(e => e.TemplateName == t.Name && WhatsAppService.SameLanguage(e.Language, t.Language)) is { } e)
                    WhatsAppService.SetStatus(t, e.Status, e.Reason);
        }
        await db.SaveChangesAsync(ct);

        // من أرسل يُبلَّغ بفشل التسليم
        var notifications = services.GetRequiredService<INotificationService>();
        foreach (var m in failed.Where(m => m.CreatedByUserId != null))
        {
            try
            {
                await notifications.NotifyAsync(new NotifyRequestDto
                {
                    RecipientUserId = m.CreatedByUserId!.Value,
                    Title = string.Format(Messages.WhatsAppDeliveryFailedTitle, m.DocumentNumber ?? m.RecipientPhone),
                    Body = m.ErrorMessage ?? string.Empty,
                    Type = "system_alert",
                    RelatedDocType = m.DocumentType, RelatedDocId = m.DocumentId, RelatedDocNumber = m.DocumentNumber,
                }, ct);
            }
            catch (NotFoundException) { } // المستخدم حُذف
        }
    }

    private static int Rank(WhatsAppMessageStatus s) => s switch
    {
        WhatsAppMessageStatus.Sent => 1,
        WhatsAppMessageStatus.Delivered => 2,
        WhatsAppMessageStatus.Read => 3,
        _ => 0,
    };

    /// <summary>
    /// يطبّق الحالة دون نزول: المسلَّم لا يعود مرسلاً، والفشل لا يغلب تسليماً أو قراءة. طابع كل حالة يُثبت أول مرة فقط.
    /// </summary>
    internal static void Apply(WhatsAppMessage m, WhatsAppStatusEvent e)
    {
        var incoming = e.Status switch
        {
            "sent" => WhatsAppMessageStatus.Sent,
            "delivered" => WhatsAppMessageStatus.Delivered,
            "read" => WhatsAppMessageStatus.Read,
            "failed" => WhatsAppMessageStatus.Failed,
            _ => (WhatsAppMessageStatus?)null,
        };
        if (incoming == null) return;
        if (e.PricingCategory != null) m.PricingCategory = e.PricingCategory;
        if (e.Billable != null) m.Billable = e.Billable;

        if (incoming == WhatsAppMessageStatus.Failed)
        {
            if (m.Status is WhatsAppMessageStatus.Delivered or WhatsAppMessageStatus.Read or WhatsAppMessageStatus.Failed) return;
            m.Status = WhatsAppMessageStatus.Failed;
            m.FailedAt = e.Timestamp;
            m.ErrorCode = e.ErrorCode;
            m.ErrorMessage = e.ErrorMessage is { Length: > 1000 } long_ ? long_[..1000] : e.ErrorMessage;
            return;
        }
        if (m.Status == WhatsAppMessageStatus.Failed) return;
        switch (incoming)
        {
            case WhatsAppMessageStatus.Sent: m.SentAt ??= e.Timestamp; break;
            case WhatsAppMessageStatus.Delivered: m.DeliveredAt ??= e.Timestamp; m.SentAt ??= e.Timestamp; break;
            case WhatsAppMessageStatus.Read: m.ReadAt ??= e.Timestamp; m.DeliveredAt ??= e.Timestamp; m.SentAt ??= e.Timestamp; break;
        }
        if (Rank(incoming.Value) > Rank(m.Status)) m.Status = incoming.Value;
    }
}
