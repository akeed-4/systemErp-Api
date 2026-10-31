namespace ERP.Core.DTOs.Shared;

/// <summary>حالة تكامل واتساب للمنشأة. لا يحمل رمز الوصول ولا رمز التسجيل أبداً.</summary>
public class WhatsAppIntegrationDto
{
    public bool IsConnected { get; set; }
    public WhatsAppIntegrationStatus? Status { get; set; }
    public string Provider { get; set; } = "meta";
    public string? BusinessId { get; set; }
    public string? WabaId { get; set; }
    public string? PhoneNumberId { get; set; }
    public string? DisplayPhoneNumber { get; set; }
    public string? VerifiedName { get; set; }
    public string? QualityRating { get; set; }
    public string? MessagingLimit { get; set; }
    public bool IsCoexistence { get; set; }
    public string? InvoiceTemplateName { get; set; }
    public string? InvoiceTemplateLanguage { get; set; }
    public string? InvoiceTemplateStatus { get; set; }
    public DateTime? ConnectedAt { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public DateTime? DisconnectedAt { get; set; }
    public string? LastError { get; set; }
    /// <summary>هل أعدّ مدير المنصة تطبيق Meta؟ بدونه لا يمكن الربط.</summary>
    public bool PlatformConfigured { get; set; }
}

public class WhatsAppSignupConfigDto
{
    public bool IsConfigured { get; set; }
    public string AppId { get; set; } = string.Empty;
    public string ConfigId { get; set; } = string.Empty;
    public string GraphVersion { get; set; } = string.Empty;
}

/// <summary>ناتج Embedded Signup: الرمز المؤقت من FB.login ومعرّفات الحساب والرقم من حدث الجلسة.</summary>
public class ConnectWhatsAppDto
{
    public string Code { get; set; } = string.Empty;
    public string WabaId { get; set; } = string.Empty;
    public string PhoneNumberId { get; set; } = string.Empty;
    public string? BusinessId { get; set; }
    /// <summary>رقم تطبيق WhatsApp Business (Coexistence): لا يُعاد تسجيله.</summary>
    public bool IsCoexistence { get; set; }
    /// <summary>عنوان الصفحة التي فُتحت منها نافذة Meta (لاستبدال الرمز المربوط به).</summary>
    public string? RedirectUri { get; set; }
    /// <summary>رقم واتساب الذي يريد العميل ربطه: يحدّد الرقم حين يمنح Meta أكثر من رقم ولم يصل حدث الجلسة.</summary>
    public string? PhoneNumber { get; set; }
    /// <summary>رمز وصول من لوحة Meta بدل Code (تجربة الرقم الاختباري، في بيئة التطوير فقط): لا يُستبدل ولا يُسجَّل الرقم.</summary>
    public string? AccessToken { get; set; }
}

public class WhatsAppTemplateDto
{
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsInvoiceTemplate { get; set; }
}

/// <summary>نص رسالة الفاتورة كما كتبه المستخدم، وحالته لدى Meta.</summary>
public class WhatsAppInvoiceTemplateDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? Footer { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Reason { get; set; }
    /// <summary>هذا ما يُرسل الآن بهذه اللغة (أحدث قالب معتمد).</summary>
    public bool IsActive { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
}

public class WhatsAppInvoiceTemplatesDto
{
    public List<WhatsAppInvoiceTemplateDto> Items { get; set; } = new();
    /// <summary>المتغيرات المتاحة بترتيب عرضها.</summary>
    public List<string> Variables { get; set; } = new();
    /// <summary>نص القالب الافتراضي لكل لغة (نقطة بداية للتحرير).</summary>
    public Dictionary<string, string> DefaultBodies { get; set; } = new();
    /// <summary>حالة القالب الافتراضي لكل لغة (يُرسل به إن لم يُعتمد قالب مخصص).</summary>
    public Dictionary<string, string> DefaultStatuses { get; set; } = new();
    /// <summary>أمثلة قيم المتغيرات لكل لغة (للمعاينة).</summary>
    public Dictionary<string, Dictionary<string, string>> Samples { get; set; } = new();
}

public class CreateWhatsAppInvoiceTemplateDto
{
    public string Language { get; set; } = "ar";
    public string Body { get; set; } = string.Empty;
    public string? Footer { get; set; }
}

public class SendWhatsAppTestDto
{
    public string Phone { get; set; } = string.Empty;
    public string? Language { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    /// <summary>فارغ = قالب الفاتورة مع PDF تجريبي؛ hello_world = قالب Meta الجاهز (يختبر الربط قبل اعتماد قالب الفاتورة).</summary>
    public string? Template { get; set; }
}

public class SendWhatsAppDocumentDto
{
    /// <summary>فارغ = جوال العميل في الفاتورة ثم بطاقته.</summary>
    public string? Phone { get; set; }
    /// <summary>لغة القالب (ar / en)؛ فارغ = العربية.</summary>
    public string? Language { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    /// <summary>تأكيد الإرسال ثانيةً لمستند أُرسل للرقم نفسه من قبل.</summary>
    public bool ConfirmDuplicate { get; set; }
}

public class ResendWhatsAppDto
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class WhatsAppMessageDto
{
    public Guid Id { get; set; }
    public WhatsAppMessageKind Kind { get; set; }
    public WhatsAppMessageStatus Status { get; set; }
    public string? DocumentType { get; set; }
    public Guid? DocumentId { get; set; }
    public string? DocumentNumber { get; set; }
    public string RecipientPhone { get; set; } = string.Empty;
    public string? TemplateName { get; set; }
    public string? TemplateLanguage { get; set; }
    public string? FileName { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid? ResendOfId { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime? FailedAt { get; set; }
    public string? PricingCategory { get; set; }
    /// <summary>إعادة الإرسال متاحة (فشل أو حالة مجهولة).</summary>
    public bool CanResend { get; set; }
}

public class PlatformWhatsAppSettingsDto
{
    public string AppId { get; set; } = string.Empty;
    public string ConfigId { get; set; } = string.Empty;
    public string GraphVersion { get; set; } = string.Empty;
    public bool HasAppSecret { get; set; }
    public bool HasVerifyToken { get; set; }
    public bool IsConfigured { get; set; }
    /// <summary>رابط الـ Webhook الذي يُضبط في تطبيق Meta.</summary>
    public string WebhookUrl { get; set; } = string.Empty;
    public bool IsCustomized { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class UpdatePlatformWhatsAppSettingsDto
{
    public string AppId { get; set; } = string.Empty;
    public string ConfigId { get; set; } = string.Empty;
    public string GraphVersion { get; set; } = string.Empty;
    /// <summary>فارغ = يبقى المحفوظ.</summary>
    public string? AppSecret { get; set; }
    public string? VerifyToken { get; set; }
}
