namespace ERP.Core.Models.Shared;

public enum WhatsAppMessageKind
{
    Document = 1, // مستند بقالب رأسه DOCUMENT
    Test = 2,     // رسالة اختبار من شاشة التكامل
}

/// <summary>حالات Meta بترتيبها: لا تنزل الحالة إلى أقدم منها عند وصول إشعار متأخر أو مكرر.</summary>
public enum WhatsAppMessageStatus
{
    Sending = 1,
    /// <summary>انقطع الاتصال بعد الطلب فلا يُعرف هل قبلته Meta: لا يُعاد تلقائياً، ويحسمه المستخدم بإعادة إرسال صريحة.</summary>
    Unknown = 2,
    Sent = 3,
    Delivered = 4,
    Read = 5,
    Failed = 6,
}

/// <summary>
/// سجل إرسال عبر واتساب (قاعدة المنشأة وحدها). يُنشأ بحالة Sending قبل الاتصال بـ Meta، فالنقر المزدوج يصطدم بفهرس فريد،
/// وتحدّثه إشعارات Meta بالـ wamid. إعادة الإرسال سجل جديد يشير للأصل.
/// </summary>
public class WhatsAppMessage : BaseEntity
{
    public Guid IntegrationId { get; set; }
    public WhatsAppMessageKind Kind { get; set; }
    public WhatsAppMessageStatus Status { get; set; } = WhatsAppMessageStatus.Sending;

    /// <summary>نوع المستند (sales-invoice) ومعرّفه ورقمه؛ فارغ لرسالة الاختبار.</summary>
    public string? DocumentType { get; set; }
    public Guid? DocumentId { get; set; }
    public string? DocumentNumber { get; set; }
    public Guid? CustomerId { get; set; }
    /// <summary>بصيغة E.164 بلا علامة +.</summary>
    public string RecipientPhone { get; set; } = string.Empty;

    public string? TemplateName { get; set; }
    public string? TemplateLanguage { get; set; }
    public string? MediaId { get; set; }
    public string? FileName { get; set; }

    /// <summary>معرّف الرسالة لدى Meta (wamid).</summary>
    public string? ProviderMessageId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>مفتاح من الواجهة لكل محاولة إرسال: الطلب المكرر بالمفتاح نفسه يعيد السجل نفسه ولا يرسل ثانية.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid? ResendOfId { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime? FailedAt { get; set; }
    public string? PricingCategory { get; set; }
    public bool? Billable { get; set; }
}
