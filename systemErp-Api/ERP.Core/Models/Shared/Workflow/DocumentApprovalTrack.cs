namespace ERP.Core.Models.Shared;

/// <summary>
/// متابعة مستند يُعتمد من شاشته (ليس عبر سياسات الموافقة): يُنشأ/يُحدَّث تلقائياً عند الحفظ حين تدخل حالة المستند «بانتظار الاعتماد»
/// ثم عند اعتماده أو رفضه أو إرجاعه. مصدر شاشة «مستنداتي بانتظار الاعتماد» و«بانتظار اعتمادي» وإشعاراتهما.
/// سجل واحد لكل مستند (نوعه + معرّفه)، يُعاد فتحه إن أُعيد تقديم المستند.
/// </summary>
public class DocumentApprovalTrack : BaseEntity
{
    /// <summary>مفتاح نوع المستند في سجل المستندات المتابَعة (مثل landed_cost_voucher).</summary>
    public string DocumentType { get; set; } = string.Empty;
    public Guid DocumentId { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    /// <summary>شاشة الصلاحيات التي يُطلب عليها «اعتماد» (تحدد من يُشعَر ومن يرى المستند في «بانتظار اعتمادي»).</summary>
    public string ScreenId { get; set; } = string.Empty;
    /// <summary>رابط فتح المستند في الواجهة (مسار + query) كما حُسب عند تقديمه.</summary>
    public string Url { get; set; } = string.Empty;

    public Guid? RequesterUserId { get; set; }
    public string RequesterName { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }

    /// <summary>pending | approved | rejected | returned | cancelled</summary>
    public string Status { get; set; } = DocumentApprovalTrackStatuses.Pending;
    public Guid? DecidedByUserId { get; set; }
    public string? DecidedByName { get; set; }
    public DateTime? DecidedAt { get; set; }
}

public static class DocumentApprovalTrackStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Returned = "returned";
    public const string Cancelled = "cancelled";
}
