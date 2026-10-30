namespace ERP.Core.Models.Shared;

public enum WhatsAppIntegrationStatus
{
    Connected = 1,
    /// <summary>رفض Meta رمز الوصول (منتهٍ أو مسحوب): يلزم إعادة الربط.</summary>
    TokenInvalid = 2,
    Disconnected = 3,
}

/// <summary>
/// ربط المنشأة بحساب WhatsApp Business ورقمها عبر Meta Embedded Signup. صف واحد لكل منشأة يُحدَّث عند إعادة الربط، وسجل
/// الربط والفصل في سجل التدقيق. يوجد أيضاً في الدليل (TenantDirectoryMirror) لأن إشعار Meta يسبق معرفة المنشأة فيُعرف برقمها.
/// رمز الوصول مشفّر ولا يُعاد للواجهة أبداً.
/// </summary>
public class TenantWhatsAppIntegration : BaseEntity
{
    public string Provider { get; set; } = "meta";
    public WhatsAppIntegrationStatus Status { get; set; } = WhatsAppIntegrationStatus.Connected;

    public string BusinessId { get; set; } = string.Empty;
    public string WabaId { get; set; } = string.Empty;
    public string PhoneNumberId { get; set; } = string.Empty;

    // لقطة من Meta تُحدَّث عند الربط والمزامنة
    public string DisplayPhoneNumber { get; set; } = string.Empty;
    public string VerifiedName { get; set; } = string.Empty;
    public string? QualityRating { get; set; }
    public string? MessagingLimit { get; set; }
    /// <summary>الرقم مستخدم أيضاً في تطبيق WhatsApp Business (Coexistence).</summary>
    public bool IsCoexistence { get; set; }

    public string AccessTokenEncrypted { get; set; } = string.Empty;
    /// <summary>رمز التحقق بخطوتين الذي سُجِّل به الرقم (مشفّر)؛ فارغ في Coexistence.</summary>
    public string? RegistrationPinEncrypted { get; set; }

    /// <summary>قالب إرسال الفواتير (Utility برأس DOCUMENT) وحالته لدى Meta.</summary>
    public string? InvoiceTemplateName { get; set; }
    public string? InvoiceTemplateLanguage { get; set; }
    public string? InvoiceTemplateStatus { get; set; }

    public Guid? ConnectedByUserId { get; set; }
    public DateTime ConnectedAt { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public DateTime? DisconnectedAt { get; set; }
    public string? LastError { get; set; }
}
