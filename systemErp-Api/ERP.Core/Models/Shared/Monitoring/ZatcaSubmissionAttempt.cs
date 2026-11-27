namespace ERP.Core.Models.Shared;

/// <summary>نتيجة محاولة إرسال فاتورة للهيئة كما حدثت فعلاً (لا تُستنتج من حالة الفاتورة).</summary>
public enum ZatcaAttemptOutcome
{
    /// <summary>رُفضت قبل الإرسال: الربط غير مفعّل، وحدة المستخدم، الشهادة، بيانات الفاتورة، أو قفل السلسلة.</summary>
    PreCheckFailed = 1,
    /// <summary>تعذّر تجهيز الفاتورة أو توقيعها محلياً قبل أي حكم من الهيئة.</summary>
    LocalError = 2,
    /// <summary>لم يصل حكم من الهيئة (انقطاع، مهلة، خطأ خادم الهيئة): السلسلة لم تتقدّم.</summary>
    Unreachable = 3,
    Cleared = 4,
    Reported = 5,
    AcceptedWithWarnings = 6,
    Rejected = 7,
}

/// <summary>
/// سجل كل محاولة إرسال فاتورة للهيئة (مستقل عن حالة الفاتورة الحالية التي تعكس آخر نتيجة فقط). في قاعدة المنشأة نفسها
/// (المشتركة أو المستقلة). لا يحفظ XML الفاتورة ولا أي سر أو شهادة أو رمز: الرموز والأخطاء المختصرة فقط.
/// </summary>
public class ZatcaSubmissionAttempt : BaseEntity
{
    public Guid InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    /// <summary>نوع المستند (sales | sales_return ...).</summary>
    public string DocumentKind { get; set; } = string.Empty;
    /// <summary>معرّف المستند لدى الهيئة في هذه المحاولة.</summary>
    public Guid? DocumentUuid { get; set; }
    /// <summary>عدّاد الفاتورة في سلسلة الوحدة (ICV) إن وصلت المحاولة للتوقيع.</summary>
    public long? Icv { get; set; }
    public Guid? UnitId { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public DateTime AttemptedAt { get; set; }
    public int DurationMs { get; set; }
    public ZatcaAttemptOutcome Outcome { get; set; }
    public int? HttpStatus { get; set; }
    /// <summary>رموز الأخطاء/التحذيرات مفصولة بفواصل (BR-KSA-..., ChainBusy...).</summary>
    public string? IssueCodes { get; set; }
    /// <summary>ملخص آمن للنتيجة للعرض.</summary>
    public string? Message { get; set; }
    /// <summary>إعادة إرسال فاتورة رُفضت سابقاً (بمعرّف جديد).</summary>
    public bool IsResubmission { get; set; }
}
