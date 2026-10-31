using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Service.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ERP.Service.Services.Shared;

/// <summary>
/// تكامل واتساب للمنشأة الحالية. الربط من ناتج Embedded Signup: يُستبدل الرمز المؤقت برمز المنشأة (من الخادم)، ويُتحقق أن الرقم
/// يتبع الحساب وأنه غير مربوط بمنشأة أخرى، ثم يُشترك التطبيق في إشعارات الحساب ويُسجَّل الرقم (عدا Coexistence)، ويُنشأ قالب الفاتورة.
/// الرمز يُحفظ مشفّراً ولا يخرج من هنا إلا إلى المزوّد. كل عملية تتحقق من استحقاق الاشتراك قبل الاتصال بالمزوّد.
/// </summary>
public class WhatsAppService : IWhatsAppService
{
    /// <summary>قالب الفاتورة: Utility برأس مستند ونص بثلاثة متغيرات (العميل، رقم المستند، المبلغ).</summary>
    public const string InvoiceTemplateName = "erp_sales_invoice_document";
    public const string DefaultLanguage = "ar";
    public static readonly IReadOnlyDictionary<string, string> InvoiceTemplateBodies = new Dictionary<string, string>
    {
        ["ar"] = "مرحباً {{1}}، مرفق لكم الفاتورة رقم {{2}} بمبلغ {{3}}. شكراً لتعاملكم معنا.",
        ["en"] = "Hello {{1}}, please find attached invoice {{2}} for {{3}}. Thank you for your business.",
    };

    private static readonly Regex GraphId = new(@"^\d{1,32}$");

    private readonly ErpDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _user;
    private readonly ISubscriptionEntitlements _entitlements;
    private readonly ISecretProtector _secrets;
    private readonly IWhatsAppProvider _provider;
    private readonly IInvoicePdfRenderer _pdf;
    private readonly IAuditService _audit;
    private readonly IPermissionService _permissions;
    private readonly WhatsAppOptions _server;
    private readonly ILogger<WhatsAppService> _log;

    public WhatsAppService(ErpDbContext db, ITenantContext tenant, ICurrentUser user, ISubscriptionEntitlements entitlements, ISecretProtector secrets,
        IWhatsAppProvider provider, IInvoicePdfRenderer pdf, IAuditService audit, IPermissionService permissions, IOptions<WhatsAppOptions> server,
        ILogger<WhatsAppService> log)
    {
        _db = db; _tenant = tenant; _user = user; _entitlements = entitlements; _secrets = secrets;
        _provider = provider; _pdf = pdf; _audit = audit; _permissions = permissions; _server = server.Value; _log = log;
    }

    public const string SalesInvoiceDocument = "sales-invoice";
    /// <summary>سجل بقي «جارٍ» أطول من هذا (توقف الخادم أثناء الإرسال) يُعدّ مجهول النتيجة فلا يحجب الإرسال للأبد.</summary>
    private static readonly TimeSpan StaleSending = TimeSpan.FromMinutes(5);

    // ================= الحالة والإعداد =================

    public async Task<WhatsAppIntegrationDto> GetIntegrationAsync(CancellationToken ct = default)
        => ToDto(await _db.Set<TenantWhatsAppIntegration>().AsNoTracking().FirstOrDefaultAsync(ct), (await AppAsync(ct)).IsConfigured);

    public async Task<WhatsAppSignupConfigDto> GetSignupConfigAsync(CancellationToken ct = default)
    {
        await EnsureEntitledAsync(ct);
        var app = await AppAsync(ct);
        return new WhatsAppSignupConfigDto { IsConfigured = app.IsConfigured, AppId = app.AppId, ConfigId = app.ConfigId, GraphVersion = app.GraphVersion };
    }

    // ================= الربط =================

    public async Task<WhatsAppIntegrationDto> ConnectAsync(ConnectWhatsAppDto r, CancellationToken ct = default)
    {
        await EnsureEntitledAsync(ct);
        var wabaId = r.WabaId?.Trim() ?? string.Empty;
        var phoneId = r.PhoneNumberId?.Trim() ?? string.Empty;
        var businessId = r.BusinessId?.Trim() ?? string.Empty;
        // الحساب والرقم من حدث جلسة Embedded Signup؛ إن لم يصل الحدث (فارغان) يُكتشفان من صلاحيات الرمز بعد استبداله
        var manualToken = r.AccessToken?.Trim() ?? string.Empty;
        if ((string.IsNullOrWhiteSpace(r.Code) && manualToken.Length == 0) || (wabaId.Length > 0 && !GraphId.IsMatch(wabaId)) || (phoneId.Length > 0 && !GraphId.IsMatch(phoneId))
            || (businessId.Length > 0 && !GraphId.IsMatch(businessId)))
            throw new ValidationFailedException(Messages.WhatsAppSignupDataInvalid);
        var app = await AppAsync(ct);
        if (!app.IsConfigured) throw new ConflictException(Messages.WhatsAppPlatformNotConfigured);

        var tenantId = _tenant.TenantId ?? throw new UnauthorizedAppException();
        if (phoneId.Length > 0) await EnsureNumberFreeAsync(phoneId, tenantId, ct);

        var integration = await _db.Set<TenantWhatsAppIntegration>().FirstOrDefaultAsync(ct);
        string token, pin = string.Empty;
        WhatsAppPhoneInfo phone;
        try
        {
            // رمز يدوي (تجربة رقم Meta الاختباري في بيئة التطوير؛ الـ controller يرفضه خارجها) بدل استبدال رمز Embedded Signup
            token = manualToken.Length > 0 ? manualToken : await _provider.ExchangeCodeAsync(app, r.Code.Trim(), r.RedirectUri, ct);
            if (phoneId.Length == 0)
            {
                // بلا حدث الجلسة: أرقام كل الحسابات الممنوحة (قد يمنح العميل حساب الاختبار مع حسابه)، ويُختار الرقم الذي كتبه العميل،
                // أو الوحيد إن لم يكتب؛ وإلا تُعرض الأرقام المتاحة ليكتب أحدها
                var wabas = wabaId.Length > 0 ? new[] { wabaId } : await _provider.ListSharedWabaIdsAsync(app, token, ct);
                var found = new List<(string WabaId, WhatsAppPhoneInfo Phone)>();
                foreach (var waba in wabas)
                    foreach (var id in await _provider.ListPhoneNumberIdsAsync(app, token, waba, ct))
                        found.Add((waba, await _provider.GetPhoneNumberAsync(app, token, id, ct)));
                var hint = WhatsAppPhone.Normalize(r.PhoneNumber);
                var match = hint == null ? found : found.Where(f => WhatsAppPhone.Normalize(f.Phone.DisplayPhoneNumber) == hint).ToList();
                if (match.Count != 1)
                {
                    _log.LogWarning("WhatsApp connect for tenant {Tenant}: accounts [{Wabas}], numbers [{Numbers}], hint {Hint}",
                        tenantId, string.Join(",", wabas), string.Join(",", found.Select(f => f.Phone.DisplayPhoneNumber)), hint);
                    throw new ValidationFailedException(string.Format(Messages.WhatsAppSessionAmbiguous,
                        found.Count == 0 ? "—" : string.Join("، ", found.Select(f => f.Phone.DisplayPhoneNumber)),
                        wabas.Count == 0 ? "—" : string.Join("، ", wabas)));
                }
                (wabaId, phone) = match[0];
                phoneId = phone.Id;
                await EnsureNumberFreeAsync(phoneId, tenantId, ct);
            }
            else
            {
                if (wabaId.Length == 0) throw new ValidationFailedException(Messages.WhatsAppSignupDataInvalid);
                if (!(await _provider.ListPhoneNumberIdsAsync(app, token, wabaId, ct)).Contains(phoneId))
                    throw new ValidationFailedException(Messages.WhatsAppNumberNotInAccount);
                phone = await _provider.GetPhoneNumberAsync(app, token, phoneId, ct);
            }
            await _provider.SubscribeAppAsync(app, token, wabaId, ct);
            // رقم تطبيق الأعمال (Coexistence) يعمل على التطبيق والـ API معاً ولا يُسجَّل؛ وإعادة ربط الرقم نفسه تستعمل رمزه السابق
            if (!r.IsCoexistence && manualToken.Length == 0)
            {
                pin = integration?.PhoneNumberId == phoneId && !string.IsNullOrEmpty(integration.RegistrationPinEncrypted)
                    ? _secrets.Unprotect(integration.RegistrationPinEncrypted) : NewPin();
                await _provider.RegisterPhoneAsync(app, token, phoneId, pin, ct);
            }
        }
        catch (WhatsAppProviderException e)
        {
            _log.LogWarning("WhatsApp connect failed for tenant {Tenant}: {Code} {Message}", tenantId, e.Code, e.Message);
            throw new ConflictException(string.Format(Messages.WhatsAppProviderError, e.Message));
        }

        if (integration == null) _db.Add(integration = new TenantWhatsAppIntegration());
        integration.Provider = _provider.Name;
        integration.Status = WhatsAppIntegrationStatus.Connected;
        integration.BusinessId = businessId;
        integration.WabaId = wabaId;
        integration.PhoneNumberId = phoneId;
        integration.IsCoexistence = r.IsCoexistence;
        integration.AccessTokenEncrypted = _secrets.Protect(token);
        integration.RegistrationPinEncrypted = pin.Length > 0 ? _secrets.Protect(pin) : null;
        integration.ConnectedByUserId = _user.UserId;
        integration.ConnectedAt = DateTime.UtcNow;
        integration.DisconnectedAt = null;
        integration.LastError = null;
        Apply(integration, phone);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("WHATSAPP_CONNECTED", nameof(TenantWhatsAppIntegration), integration.Id.ToString(),
            $"{integration.DisplayPhoneNumber} — WABA {wabaId}{(r.IsCoexistence ? " (coexistence)" : string.Empty)}", ct);

        // القالب لا يُفشل الربط: يُعرض خطؤه ويُعاد إنشاؤه من شاشة القوالب
        await TryEnsureTemplatesAsync(integration, app, token, ct);
        await _db.SaveChangesAsync(ct);
        return ToDto(integration, true);
    }

    public async Task<WhatsAppIntegrationDto> SyncAsync(CancellationToken ct = default)
    {
        await EnsureEntitledAsync(ct);
        var (integration, app, token) = await ActiveAsync(ct, allowInvalidToken: true);
        try
        {
            Apply(integration, await _provider.GetPhoneNumberAsync(app, token, integration.PhoneNumberId, ct));
            ApplyTemplates(integration, await _provider.ListTemplatesAsync(app, token, integration.WabaId, ct));
            integration.Status = WhatsAppIntegrationStatus.Connected;
            integration.LastError = null;
        }
        catch (WhatsAppProviderException e)
        {
            await RecordProviderErrorAsync(integration, e, ct);
            throw new ConflictException(string.Format(Messages.WhatsAppProviderError, e.Message));
        }
        await _db.SaveChangesAsync(ct);
        return ToDto(integration, app.IsConfigured);
    }

    public async Task<WhatsAppIntegrationDto> DisconnectAsync(CancellationToken ct = default)
    {
        // الفصل متاح حتى بعد سحب الإضافة من الاشتراك: إيقاف الاستعمال لا يحتاج استحقاقاً
        var integration = await _db.Set<TenantWhatsAppIntegration>().FirstOrDefaultAsync(ct);
        if (integration == null || integration.Status == WhatsAppIntegrationStatus.Disconnected)
            throw new ConflictException(Messages.WhatsAppNotConnected);
        var app = await AppAsync(ct);
        if (app.IsConfigured && !string.IsNullOrEmpty(integration.AccessTokenEncrypted))
        {
            // إلغاء الاشتراك في الإشعارات أفضل جهد: الرمز قد يكون مسحوباً من Meta أصلاً
            try { await _provider.UnsubscribeAppAsync(app, _secrets.Unprotect(integration.AccessTokenEncrypted), integration.WabaId, ct); }
            catch (WhatsAppProviderException e) { _log.LogInformation("WhatsApp unsubscribe skipped: {Code} {Message}", e.Code, e.Message); }
        }
        integration.Status = WhatsAppIntegrationStatus.Disconnected;
        integration.AccessTokenEncrypted = string.Empty;
        integration.DisconnectedAt = DateTime.UtcNow;
        integration.LastError = null;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("WHATSAPP_DISCONNECTED", nameof(TenantWhatsAppIntegration), integration.Id.ToString(), integration.DisplayPhoneNumber, ct);
        return ToDto(integration, app.IsConfigured);
    }

    // ================= القوالب =================

    public async Task<List<WhatsAppTemplateDto>> ListTemplatesAsync(CancellationToken ct = default)
    {
        await EnsureEntitledAsync(ct);
        var (integration, app, token) = await ActiveAsync(ct);
        IReadOnlyList<WhatsAppTemplateInfo> templates;
        try { templates = await _provider.ListTemplatesAsync(app, token, integration.WabaId, ct); }
        catch (WhatsAppProviderException e)
        {
            await RecordProviderErrorAsync(integration, e, ct);
            throw new ConflictException(string.Format(Messages.WhatsAppProviderError, e.Message));
        }
        ApplyTemplates(integration, templates);
        await _db.SaveChangesAsync(ct);
        return templates.Select(t => new WhatsAppTemplateDto
        {
            Name = t.Name, Language = t.Language, Status = t.Status, Category = t.Category, IsInvoiceTemplate = t.Name == InvoiceTemplateName,
        }).OrderByDescending(t => t.IsInvoiceTemplate).ThenBy(t => t.Name).ThenBy(t => t.Language).ToList();
    }

    public async Task<WhatsAppIntegrationDto> EnsureInvoiceTemplateAsync(CancellationToken ct = default)
    {
        await EnsureEntitledAsync(ct);
        var (integration, app, token) = await ActiveAsync(ct);
        try { await EnsureTemplatesAsync(integration, app, token, ct); }
        catch (WhatsAppProviderException e)
        {
            await RecordProviderErrorAsync(integration, e, ct);
            throw new ConflictException(string.Format(Messages.WhatsAppProviderError, e.Message));
        }
        integration.LastError = null;
        await _db.SaveChangesAsync(ct);
        return ToDto(integration, app.IsConfigured);
    }

    private async Task TryEnsureTemplatesAsync(TenantWhatsAppIntegration integration, WhatsAppAppCredentials app, string token, CancellationToken ct)
    {
        try { await EnsureTemplatesAsync(integration, app, token, ct); }
        catch (WhatsAppProviderException e)
        {
            _log.LogWarning("WhatsApp invoice template not created: {Code} {Message}", e.Code, e.Message);
            integration.LastError = string.Format(Messages.WhatsAppTemplateCreateFailed, e.Message);
        }
    }

    /// <summary>ينشئ قالب الفاتورة بالعربية والإنجليزية إن لم يوجد، ويثبّت حالته (اعتماد Meta يصل لاحقاً بإشعار).</summary>
    private async Task EnsureTemplatesAsync(TenantWhatsAppIntegration integration, WhatsAppAppCredentials app, string token, CancellationToken ct)
    {
        var existing = (await _provider.ListTemplatesAsync(app, token, integration.WabaId, ct)).ToList();
        var company = await CompanyNameAsync(ct);
        foreach (var (language, body) in InvoiceTemplateBodies)
        {
            if (existing.Any(t => t.Name == InvoiceTemplateName && SameLanguage(t.Language, language))) continue;
            var examples = language == "ar" ? new[] { "شركة المثال", "INV-1001", "1,150.00 SAR" } : new[] { "Example Co.", "INV-1001", "1,150.00 SAR" };
            existing.Add(await _provider.CreateDocumentTemplateAsync(app, token, integration.WabaId,
                new WhatsAppTemplateDefinition(InvoiceTemplateName, language, body, examples, _pdf.RenderTestDocument(company)), ct));
        }
        ApplyTemplates(integration, existing);
    }

    private static void ApplyTemplates(TenantWhatsAppIntegration integration, IEnumerable<WhatsAppTemplateInfo> templates)
    {
        var invoice = templates.FirstOrDefault(t => t.Name == InvoiceTemplateName && SameLanguage(t.Language, DefaultLanguage))
                      ?? templates.FirstOrDefault(t => t.Name == InvoiceTemplateName);
        if (invoice == null) return;
        integration.InvoiceTemplateName = invoice.Name;
        integration.InvoiceTemplateLanguage = invoice.Language;
        integration.InvoiceTemplateStatus = invoice.Status;
    }

    internal static bool SameLanguage(string a, string b) => string.Equals(a.Split('_')[0], b.Split('_')[0], StringComparison.OrdinalIgnoreCase);

    // ================= نص رسالة الفاتورة =================

    public async Task<WhatsAppInvoiceTemplatesDto> ListInvoiceTemplatesAsync(CancellationToken ct = default)
    {
        await EnsureEntitledAsync(ct);
        var (integration, app, token) = await ActiveAsync(ct);
        IReadOnlyList<WhatsAppTemplateInfo> templates;
        try { templates = await _provider.ListTemplatesAsync(app, token, integration.WabaId, ct); }
        catch (WhatsAppProviderException e)
        {
            await RecordProviderErrorAsync(integration, e, ct);
            throw new ConflictException(string.Format(Messages.WhatsAppProviderError, e.Message));
        }
        ApplyTemplates(integration, templates);
        var custom = await SyncInvoiceTemplatesAsync(templates, ct);
        await _db.SaveChangesAsync(ct);

        var company = await CompanyNameAsync(ct);
        var active = InvoiceLanguages.Select(l => ActiveCustom(custom, l)?.Id).Where(id => id != null).ToHashSet();
        return new WhatsAppInvoiceTemplatesDto
        {
            Items = custom.OrderByDescending(t => t.CreatedAt).Select(t => ToDto(t, active.Contains(t.Id))).ToList(),
            Variables = WhatsAppInvoiceText.Keys.ToList(),
            DefaultBodies = WhatsAppInvoiceText.DefaultBodies.ToDictionary(p => p.Key, p => p.Value),
            DefaultStatuses = InvoiceLanguages.ToDictionary(l => l,
                l => templates.FirstOrDefault(t => t.Name == InvoiceTemplateName && SameLanguage(t.Language, l))?.Status ?? string.Empty),
            Samples = InvoiceLanguages.ToDictionary(l => l, l => WhatsAppInvoiceText.Samples(l, company).ToDictionary(p => p.Key, p => p.Value)),
        };
    }

    public async Task<WhatsAppInvoiceTemplateDto> CreateInvoiceTemplateAsync(CreateWhatsAppInvoiceTemplateDto r, CancellationToken ct = default)
    {
        await EnsureEntitledAsync(ct);
        var language = r.Language?.Trim().ToLowerInvariant() is { } l && InvoiceLanguages.Contains(l)
            ? l : throw new ValidationFailedException(Messages.WhatsAppTemplateLanguageInvalid);
        var compiled = WhatsAppInvoiceText.Compile(r.Body, r.Footer);
        var (integration, app, token) = await ActiveAsync(ct);

        // اسم جديد لكل نص: تعديل القالب المعتمد يعيده للمراجعة فيتوقف الإرسال، أما الجديد فيُراجَع والمعتمد الحالي يعمل
        var name = $"erp_invoice_{DateTime.UtcNow:yyMMddHHmmss}_{RandomNumberGenerator.GetHexString(4, lowercase: true)}";
        var company = await CompanyNameAsync(ct);
        var samples = WhatsAppInvoiceText.Samples(language, company);
        WhatsAppTemplateInfo created;
        try
        {
            created = await _provider.CreateDocumentTemplateAsync(app, token, integration.WabaId,
                new WhatsAppTemplateDefinition(name, language, compiled.MetaBody, WhatsAppInvoiceText.Values(compiled.Keys, samples),
                    _pdf.RenderTestDocument(company), compiled.Footer), ct);
        }
        catch (WhatsAppProviderException e)
        {
            await RecordProviderErrorAsync(integration, e, ct);
            throw new ConflictException(string.Format(Messages.WhatsAppTemplateCreateFailed, e.Message));
        }

        var template = new WhatsAppInvoiceTemplate
        {
            Name = name, Language = language, Body = compiled.Body, Footer = compiled.Footer, VariableKeys = string.Join(',', compiled.Keys),
            CreatedByName = _user.Name,
        };
        SetStatus(template, created.Status, null);
        _db.Add(template);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("WHATSAPP_TEMPLATE_SUBMITTED", nameof(WhatsAppInvoiceTemplate), template.Id.ToString(), $"{name} ({language})", ct);
        var custom = await _db.Set<WhatsAppInvoiceTemplate>().AsNoTracking().ToListAsync(ct);
        return ToDto(template, ActiveCustom(custom, language)?.Id == template.Id);
    }

    public async Task DeleteInvoiceTemplateAsync(Guid id, CancellationToken ct = default)
    {
        await EnsureEntitledAsync(ct);
        var template = await _db.Set<WhatsAppInvoiceTemplate>().FirstOrDefaultAsync(t => t.Id == id, ct)
                       ?? throw new NotFoundException(Messages.WhatsAppTemplateNotFound);
        var (integration, app, token) = await ActiveAsync(ct);
        try { await _provider.DeleteTemplateAsync(app, token, integration.WabaId, template.Name, ct); }
        catch (WhatsAppProviderException e) when (!e.IsAuth && !e.IsTransport)
        {
            // محذوف من Meta أصلاً (من WhatsApp Manager): يُحذف من هنا أيضاً
            _log.LogInformation("WhatsApp template {Name} delete at Meta skipped: {Code} {Message}", template.Name, e.Code, e.Message);
        }
        catch (WhatsAppProviderException e)
        {
            await RecordProviderErrorAsync(integration, e, ct);
            throw new ConflictException(string.Format(Messages.WhatsAppProviderError, e.Message));
        }
        _db.Remove(template);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("WHATSAPP_TEMPLATE_DELETED", nameof(WhatsAppInvoiceTemplate), template.Id.ToString(), $"{template.Name} ({template.Language})", ct);
    }

    private static readonly string[] InvoiceLanguages = { "ar", "en" };

    /// <summary>يحدّث حالات النصوص المخصصة من قائمة Meta (متعقَّبة، فيحفظها المستدعي).</summary>
    private async Task<List<WhatsAppInvoiceTemplate>> SyncInvoiceTemplatesAsync(IReadOnlyList<WhatsAppTemplateInfo> templates, CancellationToken ct)
    {
        var custom = await _db.Set<WhatsAppInvoiceTemplate>().ToListAsync(ct);
        foreach (var t in custom)
            if (templates.FirstOrDefault(x => x.Name == t.Name && SameLanguage(x.Language, t.Language)) is { } info) SetStatus(t, info.Status, null);
        return custom;
    }

    /// <summary>ما يُرسل الآن باللغة: أحدث نص مخصص معتمد.</summary>
    private static WhatsAppInvoiceTemplate? ActiveCustom(IEnumerable<WhatsAppInvoiceTemplate> custom, string language)
        => custom.Where(t => t.Status == "APPROVED" && SameLanguage(t.Language, language)).OrderByDescending(t => t.CreatedAt).FirstOrDefault();

    internal static void SetStatus(WhatsAppInvoiceTemplate t, string status, string? reason)
    {
        if (string.IsNullOrWhiteSpace(status)) return;
        if (status == "APPROVED") { t.ApprovedAt ??= DateTime.UtcNow; t.Reason = null; }
        else if (reason != null) t.Reason = reason.Length > 1000 ? reason[..1000] : reason;
        t.Status = status;
    }

    private static WhatsAppInvoiceTemplateDto ToDto(WhatsAppInvoiceTemplate t, bool isActive) => new()
    {
        Id = t.Id, Name = t.Name, Language = t.Language, Body = t.Body, Footer = t.Footer, Status = t.Status, Reason = t.Reason,
        IsActive = isActive, CreatedByName = t.CreatedByName, CreatedAt = t.CreatedAt, ApprovedAt = t.ApprovedAt,
    };

    // ================= الإرسال =================

    /// <summary>Values: قيم كل المتغيرات المتاحة بأسمائها؛ القالب المستعمل يحدد أيها يُرسل وبأي ترتيب.</summary>
    private sealed record SendPlan(WhatsAppMessageKind Kind, string Phone, string Language, string? DocumentType, Guid? DocumentId, string? DocumentNumber,
        Guid? CustomerId, IReadOnlyDictionary<string, string> Values, Func<CancellationToken, Task<RenderedDocument>> Render, bool HelloWorld = false);

    /// <summary>
    /// القالب الذي يُرسل به: أحدث نص مخصص معتمد للغة، وإلا القالب الافتراضي. Language: لغة القالب لدى Meta إن خالفت لغة الخطة،
    /// وDocument = false لقالب بلا رأس مستند (hello_world).
    /// </summary>
    private sealed record ResolvedTemplate(string Name, string[] Keys, string? Language = null, bool Document = true);

    /// <summary>قالب Meta الجاهز المعتمد في كل حساب: يختبر الربط والتسليم قبل اعتماد قالب الفاتورة (نص فقط بلا PDF).</summary>
    public const string HelloWorldTemplate = "hello_world";

    public async Task<WhatsAppMessageDto> SendTestAsync(SendWhatsAppTestDto r, CancellationToken ct = default)
    {
        await EnsureEntitledAsync(ct);
        var key = Key(r.IdempotencyKey);
        if (await ReplayAsync(key, ct) is { } replay) return replay;
        var plan = await TestPlanAsync(r.Phone, r.Language, ct) with { HelloWorld = r.Template?.Trim() == HelloWorldTemplate };
        return await SendAsync(plan, key, resendOf: null, confirmDuplicate: true, ct);
    }

    public async Task<WhatsAppMessageDto> SendSalesInvoiceAsync(Guid invoiceId, SendWhatsAppDocumentDto r, CancellationToken ct = default)
    {
        await EnsureEntitledAsync(ct);
        var key = Key(r.IdempotencyKey);
        if (await ReplayAsync(key, ct) is { } replay) return replay;
        var plan = await InvoicePlanAsync(invoiceId, r.Phone, r.Language, ct);
        return await SendAsync(plan, key, resendOf: null, r.ConfirmDuplicate, ct);
    }

    public async Task<WhatsAppMessageDto> ResendAsync(Guid messageId, ResendWhatsAppDto r, CancellationToken ct = default)
    {
        await EnsureEntitledAsync(ct);
        var key = Key(r.IdempotencyKey);
        if (await ReplayAsync(key, ct) is { } replay) return replay;
        var original = await _db.Set<WhatsAppMessage>().AsNoTracking().FirstOrDefaultAsync(m => m.Id == messageId, ct)
                       ?? throw new NotFoundException(Messages.WhatsAppMessageNotFound);
        if (original.Status is not (WhatsAppMessageStatus.Failed or WhatsAppMessageStatus.Unknown)) throw new ConflictException(Messages.WhatsAppCannotResend);

        var plan = original.Kind == WhatsAppMessageKind.Test
            ? await TestPlanAsync(original.RecipientPhone, original.TemplateLanguage, ct) with { HelloWorld = original.TemplateName == HelloWorldTemplate }
            : original.DocumentType == SalesInvoiceDocument && original.DocumentId is { } invoiceId
                ? await InvoicePlanAsync(invoiceId, original.RecipientPhone, original.TemplateLanguage, ct)
                : throw new ConflictException(Messages.WhatsAppCannotResend);
        return await SendAsync(plan, key, original.Id, confirmDuplicate: true, ct);
    }

    public async Task<List<WhatsAppMessageDto>> ListMessagesAsync(string? documentType, Guid? documentId, CancellationToken ct = default)
    {
        var q = _db.Set<WhatsAppMessage>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(documentType)) q = q.Where(m => m.DocumentType == documentType);
        if (documentId != null) q = q.Where(m => m.DocumentId == documentId);
        return (await q.OrderByDescending(m => m.CreatedAt).Take(200).ToListAsync(ct)).Select(ToDto).ToList();
    }

    private async Task<SendPlan> TestPlanAsync(string? phone, string? language, CancellationToken ct)
    {
        var to = WhatsAppPhone.Normalize(phone) ?? throw new ValidationFailedException(Messages.WhatsAppPhoneInvalid);
        var lang = Language(language);
        var company = await CompanyNameAsync(ct);
        var values = new Dictionary<string, string>
        {
            [WhatsAppInvoiceText.CustomerName] = lang == "ar" ? "عميل تجريبي" : "Test customer",
            [WhatsAppInvoiceText.InvoiceNumber] = "TEST-0001",
            [WhatsAppInvoiceText.Amount] = "0.00 SAR",
            [WhatsAppInvoiceText.InvoiceDate] = SaudiTime.FromUtc(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            [WhatsAppInvoiceText.CompanyName] = TemplateText(await CompanyNameAsync(lang, ct)),
        };
        return new SendPlan(WhatsAppMessageKind.Test, to, lang, null, null, null, null, values, _ => Task.FromResult(_pdf.RenderTestDocument(company)));
    }

    /// <summary>الفاتورة مرحّلة وضمن نطاق المستخدم وصلاحية عرض المبيعات؛ الرقم من الطلب ثم الفاتورة ثم بطاقة العميل.</summary>
    private async Task<SendPlan> InvoicePlanAsync(Guid invoiceId, string? phone, string? language, CancellationToken ct)
    {
        if (!await _permissions.HasPermissionAsync("sales", ScreenAction.View, ct)) throw new ForbiddenException();
        var invoice = await _db.Set<SalesInvoice>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
                      ?? throw new NotFoundException(Messages.InvoiceNotFound);
        await _permissions.EnsureRecordInScopeAsync(SalesInvoiceDocument, invoiceId, ct);
        if (invoice.Status != "posted") throw new ValidationFailedException(Messages.WhatsAppInvoiceNotPosted);

        var raw = !string.IsNullOrWhiteSpace(phone) ? phone : !string.IsNullOrWhiteSpace(invoice.PartyPhone) ? invoice.PartyPhone
            : invoice.PartyId is { } partyId ? await _db.Set<Customer>().AsNoTracking().Where(c => c.Id == partyId).Select(c => c.Phone).FirstOrDefaultAsync(ct) : null;
        if (string.IsNullOrWhiteSpace(raw)) throw new ValidationFailedException(Messages.WhatsAppPhoneMissing);
        var to = WhatsAppPhone.Normalize(raw) ?? throw new ValidationFailedException(Messages.WhatsAppPhoneInvalid);

        var lang = Language(language);
        var values = new Dictionary<string, string>
        {
            [WhatsAppInvoiceText.CustomerName] = TemplateText(invoice.PartyName),
            [WhatsAppInvoiceText.InvoiceNumber] = invoice.InvoiceNumber,
            [WhatsAppInvoiceText.Amount] = $"{invoice.GrandTotal.ToString("#,##0.00", System.Globalization.CultureInfo.InvariantCulture)} {invoice.CurrencyCode}",
            [WhatsAppInvoiceText.InvoiceDate] = invoice.IssueDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            [WhatsAppInvoiceText.CompanyName] = TemplateText(await CompanyNameAsync(lang, ct)),
        };
        return new SendPlan(WhatsAppMessageKind.Document, to, lang, SalesInvoiceDocument, invoice.Id, invoice.InvoiceNumber, invoice.PartyId,
            values, token => _pdf.RenderSalesInvoiceAsync(invoice.Id, token));
    }

    /// <summary>
    /// السجل يُحفظ «جارياً» قبل الاتصال بالمزوّد، فإرسالان متزامنان للمستند والرقم يصطدمان بفهرس فريد. أي خطأ بعده يُثبَّت على السجل
    /// (فشل، أو مجهول النتيجة إن انقطع الاتصال) فلا يبقى معلّقاً، ثم يُبلَّغ المستخدم.
    /// </summary>
    private async Task<WhatsAppMessageDto> SendAsync(SendPlan plan, string key, Guid? resendOf, bool confirmDuplicate, CancellationToken ct)
    {
        var (integration, app, token) = await ActiveAsync(ct);
        var template = plan.HelloWorld
            ? await ResolveHelloWorldAsync(integration, app, token, ct)
            : await ResolveTemplateAsync(integration, app, token, plan.Language, ct);
        if (plan.DocumentId != null) await EnsureNotDuplicateAsync(plan, confirmDuplicate, ct);

        var message = new WhatsAppMessage
        {
            IntegrationId = integration.Id, Kind = plan.Kind, Status = WhatsAppMessageStatus.Sending,
            DocumentType = plan.DocumentType, DocumentId = plan.DocumentId, DocumentNumber = plan.DocumentNumber, CustomerId = plan.CustomerId,
            RecipientPhone = plan.Phone, TemplateName = template.Name, TemplateLanguage = template.Language ?? plan.Language,
            IdempotencyKey = key, ResendOfId = resendOf, CreatedByUserId = _user.UserId, CreatedByName = _user.Name,
        };
        _db.Add(message);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            _db.Entry(message).State = EntityState.Detached;
            if (await ReplayAsync(key, ct) is { } replay) return replay; // الطلب نفسه سبق في طلب متزامن
            throw new ConflictException(Messages.WhatsAppSendInProgress);
        }

        try
        {
            if (template.Document)
            {
                var document = await plan.Render(ct);
                message.FileName = document.FileName;
                message.MediaId = await _provider.UploadMediaAsync(app, token, integration.PhoneNumberId, document, ct);
                message.ProviderMessageId = await _provider.SendDocumentTemplateAsync(app, token, integration.PhoneNumberId,
                    new WhatsAppTemplateMessage(plan.Phone, template.Name, plan.Language, message.MediaId, document.FileName,
                        WhatsAppInvoiceText.Values(template.Keys, plan.Values)), ct);
            }
            else
            {
                message.ProviderMessageId = await _provider.SendDocumentTemplateAsync(app, token, integration.PhoneNumberId,
                    new WhatsAppTemplateMessage(plan.Phone, template.Name, message.TemplateLanguage, string.Empty, string.Empty, Array.Empty<string>()), ct);
            }
            message.Status = WhatsAppMessageStatus.Sent;
            message.SentAt = DateTime.UtcNow;
        }
        catch (WhatsAppProviderException e)
        {
            message.Status = e.IsTransport ? WhatsAppMessageStatus.Unknown : WhatsAppMessageStatus.Failed;
            message.FailedAt = e.IsTransport ? null : DateTime.UtcNow;
            message.ErrorCode = e.Code;
            message.ErrorMessage = e.Message.Length > 1000 ? e.Message[..1000] : e.Message;
            if (e.IsAuth) { integration.Status = WhatsAppIntegrationStatus.TokenInvalid; integration.LastError = message.ErrorMessage; }
            await _db.SaveChangesAsync(CancellationToken.None);
            throw new ConflictException(e.IsTransport ? Messages.WhatsAppSendOutcomeUnknown : string.Format(Messages.WhatsAppSendFailed, e.Message));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            message.Status = WhatsAppMessageStatus.Failed;
            message.FailedAt = DateTime.UtcNow;
            message.ErrorMessage = e.Message.Length > 1000 ? e.Message[..1000] : e.Message;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("WHATSAPP_SENT", nameof(WhatsAppMessage), message.Id.ToString(),
            $"{plan.DocumentNumber ?? plan.Kind.ToString()} → {plan.Phone}", ct);
        return ToDto(message);
    }

    /// <summary>
    /// قالب معتمد لدى Meta باللغة المطلوبة (يُقرأ من Meta فتُحدَّث الحالات المحفوظة معه): أحدث نص مخصص معتمد، وإلا الافتراضي.
    /// </summary>
    private async Task<ResolvedTemplate> ResolveTemplateAsync(TenantWhatsAppIntegration integration, WhatsAppAppCredentials app, string token, string language,
        CancellationToken ct)
    {
        IReadOnlyList<WhatsAppTemplateInfo> templates;
        try { templates = await _provider.ListTemplatesAsync(app, token, integration.WabaId, ct); }
        catch (WhatsAppProviderException e)
        {
            await RecordProviderErrorAsync(integration, e, ct);
            throw new ConflictException(string.Format(Messages.WhatsAppProviderError, e.Message));
        }
        ApplyTemplates(integration, templates);
        var custom = await SyncInvoiceTemplatesAsync(templates, ct);
        await _db.SaveChangesAsync(ct);

        if (ActiveCustom(custom, language) is { } active) return new ResolvedTemplate(active.Name, WhatsAppInvoiceText.SplitKeys(active.VariableKeys));
        var template = templates.FirstOrDefault(t => t.Name == InvoiceTemplateName && SameLanguage(t.Language, language));
        if (template?.Status == "APPROVED") return new ResolvedTemplate(InvoiceTemplateName, WhatsAppInvoiceText.DefaultKeys);
        // الحالة المعروضة: أحدث نص مخصص بانتظار الاعتماد إن وُجد (هو ما ينتظره المستخدم)، وإلا حالة الافتراضي
        var waiting = custom.Where(t => SameLanguage(t.Language, language)).OrderByDescending(t => t.CreatedAt).FirstOrDefault();
        throw new ConflictException(string.Format(Messages.WhatsAppTemplateNotApproved, waiting?.Status ?? template?.Status ?? "—"));
    }

    /// <summary>hello_world معتمد في الحساب (يُنشئه Meta تلقائياً، وقد يُحذف من WhatsApp Manager)، بلغته لدى Meta (en_US عادةً).</summary>
    private async Task<ResolvedTemplate> ResolveHelloWorldAsync(TenantWhatsAppIntegration integration, WhatsAppAppCredentials app, string token, CancellationToken ct)
    {
        IReadOnlyList<WhatsAppTemplateInfo> templates;
        try { templates = await _provider.ListTemplatesAsync(app, token, integration.WabaId, ct); }
        catch (WhatsAppProviderException e)
        {
            await RecordProviderErrorAsync(integration, e, ct);
            throw new ConflictException(string.Format(Messages.WhatsAppProviderError, e.Message));
        }
        var hello = templates.FirstOrDefault(t => t.Name == HelloWorldTemplate && t.Status == "APPROVED")
                    ?? throw new ConflictException(Messages.WhatsAppHelloWorldMissing);
        return new ResolvedTemplate(hello.Name, Array.Empty<string>(), hello.Language, Document: false);
    }

    /// <summary>إرسال جارٍ يمنع، وإرسال ناجح سابق يتطلب تأكيداً صريحاً. «الجاري» القديم يُعدّ مجهول النتيجة.</summary>
    private async Task EnsureNotDuplicateAsync(SendPlan plan, bool confirmDuplicate, CancellationToken ct)
    {
        var previous = await _db.Set<WhatsAppMessage>()
            .Where(m => m.DocumentType == plan.DocumentType && m.DocumentId == plan.DocumentId && m.RecipientPhone == plan.Phone && m.Status != WhatsAppMessageStatus.Failed)
            .OrderByDescending(m => m.CreatedAt).ToListAsync(ct);
        foreach (var stale in previous.Where(m => m.Status == WhatsAppMessageStatus.Sending && m.CreatedAt < DateTime.UtcNow - StaleSending))
            stale.Status = WhatsAppMessageStatus.Unknown;
        if (previous.Any(m => m.Status == WhatsAppMessageStatus.Sending)) throw new ConflictException(Messages.WhatsAppSendInProgress);
        await _db.SaveChangesAsync(ct);
        var sent = previous.FirstOrDefault();
        if (sent != null && !confirmDuplicate)
            throw new ConflictException(string.Format(Messages.WhatsAppAlreadySent, SaudiTime.FromUtc(sent.SentAt ?? sent.CreatedAt).ToString("yyyy-MM-dd HH:mm")));
    }

    private async Task<WhatsAppMessageDto?> ReplayAsync(string key, CancellationToken ct)
        => await _db.Set<WhatsAppMessage>().AsNoTracking().FirstOrDefaultAsync(m => m.IdempotencyKey == key, ct) is { } m ? ToDto(m) : null;

    private static string Key(string? key)
    {
        var k = key?.Trim() ?? string.Empty;
        return k.Length is > 0 and <= 64 ? k : throw new ValidationFailedException(Messages.WhatsAppIdempotencyKeyRequired);
    }

    private static string Language(string? language) => language?.Trim().ToLowerInvariant() is "en" ? "en" : DefaultLanguage;

    /// <summary>متغير القالب: سطر واحد بلا مسافات متتالية (شرط Meta).</summary>
    private static string TemplateText(string value)
    {
        var text = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
        return text.Length == 0 ? "-" : text.Length > 200 ? text[..200] : text;
    }

    internal static WhatsAppMessageDto ToDto(WhatsAppMessage m) => new()
    {
        Id = m.Id, Kind = m.Kind, Status = m.Status, DocumentType = m.DocumentType, DocumentId = m.DocumentId, DocumentNumber = m.DocumentNumber,
        RecipientPhone = m.RecipientPhone, TemplateName = m.TemplateName, TemplateLanguage = m.TemplateLanguage, FileName = m.FileName,
        ProviderMessageId = m.ProviderMessageId, ErrorCode = m.ErrorCode, ErrorMessage = m.ErrorMessage, ResendOfId = m.ResendOfId,
        CreatedByName = m.CreatedByName, CreatedAt = m.CreatedAt, SentAt = m.SentAt, DeliveredAt = m.DeliveredAt, ReadAt = m.ReadAt,
        FailedAt = m.FailedAt, PricingCategory = m.PricingCategory,
        CanResend = m.Status is WhatsAppMessageStatus.Failed or WhatsAppMessageStatus.Unknown,
    };

    // ================= أدوات =================

    /// <summary>الإضافة ضمن الاشتراك وهو ساري (البوابة العامة تفحص الـ controller، وهذا فحص ثانٍ لكل عملية تتصل بالمزوّد).</summary>
    private async Task EnsureEntitledAsync(CancellationToken ct)
    {
        var info = await _entitlements.GetAsync(ct);
        if (!info.HasSubscription || !info.Allows(PlatformModules.WhatsApp)) throw new ForbiddenException(Messages.WhatsAppNotInPlan);
        if (info.TrialEnded || info.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Trial))
            throw new ForbiddenException(Messages.WhatsAppSubscriptionInactive);
    }

    private async Task<WhatsAppAppCredentials> AppAsync(CancellationToken ct) => (await PlatformWhatsApp.ResolveAsync(_db, _server, _secrets, ct)).App;

    /// <summary>الربط الفعّال ورمزه. TokenInvalid يُقبل للمزامنة وحدها (لكشف عودة الرمز صالحاً).</summary>
    private async Task<(TenantWhatsAppIntegration Integration, WhatsAppAppCredentials App, string Token)> ActiveAsync(CancellationToken ct, bool allowInvalidToken = false)
    {
        var integration = await _db.Set<TenantWhatsAppIntegration>().FirstOrDefaultAsync(ct);
        if (integration == null || integration.Status == WhatsAppIntegrationStatus.Disconnected || string.IsNullOrEmpty(integration.AccessTokenEncrypted))
            throw new ConflictException(Messages.WhatsAppNotConnected);
        if (integration.Status == WhatsAppIntegrationStatus.TokenInvalid && !allowInvalidToken)
            throw new ConflictException(Messages.WhatsAppReconnectRequired);
        var app = await AppAsync(ct);
        if (!app.IsConfigured) throw new ConflictException(Messages.WhatsAppPlatformNotConfigured);
        return (integration, app, _secrets.Unprotect(integration.AccessTokenEncrypted));
    }

    /// <summary>رفض الرمز يجعل الربط «يلزم إعادة الربط» ويُحفظ فوراً حتى لو رُمي الخطأ بعده.</summary>
    private async Task RecordProviderErrorAsync(TenantWhatsAppIntegration integration, WhatsAppProviderException e, CancellationToken ct)
    {
        if (e.IsAuth) integration.Status = WhatsAppIntegrationStatus.TokenInvalid;
        integration.LastError = e.Message.Length > 1000 ? e.Message[..1000] : e.Message;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>الرقم لا يُربط بمنشأتين: يُفحص في الدليل حيث تجتمع المنشآت كلها.</summary>
    private async Task EnsureNumberFreeAsync(string phoneNumberId, Guid tenantId, CancellationToken ct)
    {
        var taken = await _db.Catalog.Set<TenantWhatsAppIntegration>().IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(i => i.PhoneNumberId == phoneNumberId && i.TenantId != tenantId && i.Status != WhatsAppIntegrationStatus.Disconnected, ct);
        if (taken) throw new ConflictException(Messages.WhatsAppNumberLinkedElsewhere);
    }

    private static void Apply(TenantWhatsAppIntegration integration, WhatsAppPhoneInfo phone)
    {
        integration.DisplayPhoneNumber = phone.DisplayPhoneNumber;
        integration.VerifiedName = phone.VerifiedName;
        integration.QualityRating = phone.QualityRating;
        integration.MessagingLimit = phone.MessagingLimit;
        integration.LastSyncedAt = DateTime.UtcNow;
    }

    private async Task<string> CompanyNameAsync(CancellationToken ct)
        => await _db.Set<Tenant>().AsNoTracking().Select(t => t.NameAr).FirstOrDefaultAsync(ct) ?? string.Empty;

    /// <summary>اسم المنشأة بلغة الرسالة (الإنجليزي يرجع للعربي إن لم يُدخل).</summary>
    private async Task<string> CompanyNameAsync(string language, CancellationToken ct)
    {
        var names = await _db.Set<Tenant>().AsNoTracking().Select(t => new { t.NameAr, t.NameEn }).FirstOrDefaultAsync(ct);
        return (language == "en" && !string.IsNullOrWhiteSpace(names?.NameEn) ? names!.NameEn : names?.NameAr) ?? string.Empty;
    }

    private static string NewPin() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    internal static WhatsAppIntegrationDto ToDto(TenantWhatsAppIntegration? i, bool platformConfigured) => i == null
        ? new WhatsAppIntegrationDto { PlatformConfigured = platformConfigured }
        : new WhatsAppIntegrationDto
        {
            IsConnected = i.Status == WhatsAppIntegrationStatus.Connected,
            Status = i.Status, Provider = i.Provider, BusinessId = i.BusinessId, WabaId = i.WabaId, PhoneNumberId = i.PhoneNumberId,
            DisplayPhoneNumber = i.DisplayPhoneNumber, VerifiedName = i.VerifiedName, QualityRating = i.QualityRating, MessagingLimit = i.MessagingLimit,
            IsCoexistence = i.IsCoexistence, InvoiceTemplateName = i.InvoiceTemplateName, InvoiceTemplateLanguage = i.InvoiceTemplateLanguage,
            InvoiceTemplateStatus = i.InvoiceTemplateStatus, ConnectedAt = i.ConnectedAt, LastSyncedAt = i.LastSyncedAt,
            DisconnectedAt = i.DisconnectedAt, LastError = i.LastError, PlatformConfigured = platformConfigured,
        };
}
