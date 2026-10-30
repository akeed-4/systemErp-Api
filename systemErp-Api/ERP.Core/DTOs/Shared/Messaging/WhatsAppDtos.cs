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
}

public class WhatsAppTemplateDto
{
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsInvoiceTemplate { get; set; }
}

public class SendWhatsAppTestDto
{
    public string Phone { get; set; } = string.Empty;
    public string? Language { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
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
