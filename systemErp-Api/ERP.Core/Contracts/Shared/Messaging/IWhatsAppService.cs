using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>
/// تكامل واتساب للمنشأة الحالية: الربط عبر Meta Embedded Signup، والمزامنة والفصل، والقوالب، والإرسال وسجله.
/// كل عملية تتحقق من استحقاق الاشتراك (إضافة whatsapp) قبل أي اتصال بالمزوّد.
/// </summary>
public interface IWhatsAppService
{
    Task<WhatsAppIntegrationDto> GetIntegrationAsync(CancellationToken ct = default);
    /// <summary>ما تحتاجه الواجهة لتشغيل Embedded Signup (معرّف التطبيق والإعداد؛ لا أسرار).</summary>
    Task<WhatsAppSignupConfigDto> GetSignupConfigAsync(CancellationToken ct = default);
    Task<WhatsAppIntegrationDto> ConnectAsync(ConnectWhatsAppDto request, CancellationToken ct = default);
    Task<WhatsAppIntegrationDto> SyncAsync(CancellationToken ct = default);
    Task<WhatsAppIntegrationDto> DisconnectAsync(CancellationToken ct = default);

    Task<List<WhatsAppTemplateDto>> ListTemplatesAsync(CancellationToken ct = default);
    /// <summary>ينشئ قالب الفاتورة (عربي وإنجليزي) لدى Meta إن لم يوجد.</summary>
    Task<WhatsAppIntegrationDto> EnsureInvoiceTemplateAsync(CancellationToken ct = default);
    /// <summary>نصوص رسالة الفاتورة التي كتبها المستخدم بحالاتها (تُحدَّث من Meta إن أمكن).</summary>
    Task<WhatsAppInvoiceTemplatesDto> ListInvoiceTemplatesAsync(CancellationToken ct = default);
    /// <summary>يرسل نصاً جديداً لرسالة الفاتورة إلى Meta للاعتماد؛ القالب المعتمد الحالي يبقى مستعملاً حتى يُعتمد.</summary>
    Task<WhatsAppInvoiceTemplateDto> CreateInvoiceTemplateAsync(CreateWhatsAppInvoiceTemplateDto request, CancellationToken ct = default);
    Task DeleteInvoiceTemplateAsync(Guid id, CancellationToken ct = default);

    Task<WhatsAppMessageDto> SendTestAsync(SendWhatsAppTestDto request, CancellationToken ct = default);
    /// <summary>فاتورة مبيعات مرحّلة PDF بقالب الفاتورة المعتمد. المفتاح نفسه يعيد السجل نفسه، والإرسال الثاني للرقم نفسه يتطلب تأكيداً.</summary>
    Task<WhatsAppMessageDto> SendSalesInvoiceAsync(Guid invoiceId, SendWhatsAppDocumentDto request, CancellationToken ct = default);
    /// <summary>إعادة رسالة فشلت أو لم تُعرف نتيجتها (سجل جديد يشير للأصل).</summary>
    Task<WhatsAppMessageDto> ResendAsync(Guid messageId, ResendWhatsAppDto request, CancellationToken ct = default);
    Task<List<WhatsAppMessageDto>> ListMessagesAsync(string? documentType, Guid? documentId, CancellationToken ct = default);
}

/// <summary>إشعارات المزوّد (بلا دخول): تُعرف المنشأة من رقمها في الدليل ثم يُعالج كل جزء ضمن منشأته.</summary>
public interface IWhatsAppWebhookService
{
    /// <summary>مصافحة الاشتراك: يعيد challenge إن طابق رمز التحقق، وإلا null.</summary>
    Task<string?> VerifySubscriptionAsync(string? mode, string? verifyToken, string? challenge, CancellationToken ct = default);
    /// <summary>false = توقيع غير صالح (لا أثر).</summary>
    Task<bool> HandleAsync(byte[] body, string? signature, CancellationToken ct = default);
}

/// <summary>إعداد تطبيق Meta الخاص بالمنصة (لمدير المنصة؛ الأسرار لا تُعاد).</summary>
public interface IWhatsAppPlatformSettingsService
{
    Task<PlatformWhatsAppSettingsDto> GetAsync(CancellationToken ct = default);
    Task<PlatformWhatsAppSettingsDto> UpdateAsync(UpdatePlatformWhatsAppSettingsDto request, CancellationToken ct = default);
}

/// <summary>توليد PDF للمستند من بيانات السرفر (لا من الواجهة).</summary>
public interface IInvoicePdfRenderer
{
    Task<RenderedDocument> RenderSalesInvoiceAsync(Guid invoiceId, CancellationToken ct = default);
    /// <summary>مستند قصير لرسالة الاختبار.</summary>
    RenderedDocument RenderTestDocument(string companyName);
}

public record RenderedDocument(byte[] Content, string FileName, string ContentType = "application/pdf");

/// <summary>
/// مزوّد واتساب (Meta Cloud API اليوم). كل ما يخص المزوّد هنا، فإضافة مزوّد آخر = تنفيذ جديد لهذا العقد.
/// الأخطاء تُرمى <see cref="WhatsAppProviderException"/>. يُستبدل ببديل وهمي في الاختبارات.
/// </summary>
public interface IWhatsAppProvider
{
    string Name { get; }
    /// <summary><paramref name="redirectUri"/>: عنوان صفحة الربط؛ الرمز من FB.login قد يكون مربوطاً به أو بعنوان فارغ.</summary>
    Task<string> ExchangeCodeAsync(WhatsAppAppCredentials app, string code, string? redirectUri = null, CancellationToken ct = default);
    /// <summary>حسابات WhatsApp Business التي منحها العميل للتطبيق في Embedded Signup (من صلاحيات الرمز).</summary>
    Task<IReadOnlyList<string>> ListSharedWabaIdsAsync(WhatsAppAppCredentials app, string token, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListPhoneNumberIdsAsync(WhatsAppAppCredentials app, string token, string wabaId, CancellationToken ct = default);
    Task<WhatsAppPhoneInfo> GetPhoneNumberAsync(WhatsAppAppCredentials app, string token, string phoneNumberId, CancellationToken ct = default);
    Task SubscribeAppAsync(WhatsAppAppCredentials app, string token, string wabaId, CancellationToken ct = default);
    Task UnsubscribeAppAsync(WhatsAppAppCredentials app, string token, string wabaId, CancellationToken ct = default);
    Task RegisterPhoneAsync(WhatsAppAppCredentials app, string token, string phoneNumberId, string pin, CancellationToken ct = default);
    Task<IReadOnlyList<WhatsAppTemplateInfo>> ListTemplatesAsync(WhatsAppAppCredentials app, string token, string wabaId, CancellationToken ct = default);
    Task<WhatsAppTemplateInfo> CreateDocumentTemplateAsync(WhatsAppAppCredentials app, string token, string wabaId, WhatsAppTemplateDefinition template, CancellationToken ct = default);
    /// <summary>يحذف القالب بكل لغاته (بالاسم).</summary>
    Task DeleteTemplateAsync(WhatsAppAppCredentials app, string token, string wabaId, string name, CancellationToken ct = default);
    Task<string> UploadMediaAsync(WhatsAppAppCredentials app, string token, string phoneNumberId, RenderedDocument document, CancellationToken ct = default);
    /// <summary>يرسل قالباً رأسه مستند؛ يعيد معرّف الرسالة لدى المزوّد.</summary>
    Task<string> SendDocumentTemplateAsync(WhatsAppAppCredentials app, string token, string phoneNumberId, WhatsAppTemplateMessage message, CancellationToken ct = default);

    bool VerifySignature(WhatsAppAppCredentials app, byte[] body, string? signature);
    WhatsAppWebhookBatch ParseWebhook(byte[] body);
}

public record WhatsAppAppCredentials(string AppId, string AppSecret, string ConfigId, string VerifyToken, string GraphVersion)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(AppSecret) && !string.IsNullOrWhiteSpace(ConfigId)
        && !string.IsNullOrWhiteSpace(VerifyToken) && !string.IsNullOrWhiteSpace(GraphVersion);
}

public record WhatsAppPhoneInfo(string Id, string DisplayPhoneNumber, string VerifiedName, string? QualityRating, string? MessagingLimit);
public record WhatsAppTemplateInfo(string Name, string Language, string Status, string Category);
/// <summary>قالب Utility رأسه DOCUMENT ونصه بمتغيرات موضعية ({{1}}..)؛ SampleDocument مثال الرأس المطلوب لاعتماده، وFooter تذييل اختياري بلا متغيرات.</summary>
public record WhatsAppTemplateDefinition(string Name, string Language, string BodyText, IReadOnlyList<string> BodyExamples, RenderedDocument SampleDocument,
    string? Footer = null);
public record WhatsAppTemplateMessage(string To, string TemplateName, string Language, string MediaId, string FileName, IReadOnlyList<string> BodyParameters);

public record WhatsAppWebhookBatch(IReadOnlyList<WhatsAppStatusEvent> Statuses, IReadOnlyList<WhatsAppTemplateEvent> Templates);
public record WhatsAppStatusEvent(string PhoneNumberId, string MessageId, string Status, DateTime Timestamp, string? ErrorCode, string? ErrorMessage,
    string? PricingCategory, bool? Billable);
public record WhatsAppTemplateEvent(string WabaId, string TemplateName, string Language, string Status, string? Reason);

/// <summary>خطأ من المزوّد بكوده ورسالته. <see cref="IsAuth"/>: الرمز مرفوض فيلزم إعادة الربط.</summary>
public class WhatsAppProviderException : Exception
{
    public string Code { get; }
    public bool IsAuth { get; }
    public bool IsTransport { get; }
    public WhatsAppProviderException(string code, string message, bool isAuth = false, bool isTransport = false, Exception? inner = null)
        : base(message, inner)
    {
        Code = code; IsAuth = isAuth; IsTransport = isTransport;
    }
}
