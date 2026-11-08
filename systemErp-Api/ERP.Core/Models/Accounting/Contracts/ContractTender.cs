namespace ERP.Core.Models.Accounting;

/// <summary>
/// حالة المناقصة على عقد المقاولات (المناقصة هي مسودة العقد نفسه قبل الترسية، فلا يُعاد إدخال جدول الكميات والأسعار):
/// جديدة ← قيد الدراسة ← قيد التسعير ← مقدَّمة ← مرساة أو خاسرة. المقدَّمة تعود للتسعير (مراجعة بعد التفاوض) فيُحفظ عرض جديد.
/// فارغة = عقد مباشر بلا مناقصة. ما لم تُرسَ لا يُرسل العقد للمراجعة ولا يُفعَّل.
/// </summary>
public static class ContractTenderStatuses
{
    public const string New = "new";
    public const string Studying = "studying";
    public const string Pricing = "pricing";
    public const string Submitted = "submitted";
    public const string Awarded = "awarded";
    public const string Lost = "lost";

    public static readonly string[] All = { New, Studying, Pricing, Submitted, Awarded, Lost };
    public static bool IsValid(string? status) => status != null && All.Contains(status);

    /// <summary>مناقصة لم تُرسَ (والخاسرة): العقد لا يتقدم في دورته.</summary>
    public static bool BlocksContract(string? status) => status != null && status != Awarded;

    /// <summary>مناقصة مفتوحة (تحتسب في المواعيد).</summary>
    public static bool IsOpen(string? status) => status is New or Studying or Pricing or Submitted;

    /// <summary>بنود العطاء وأسعاره لا تُعدَّل وهو مقدَّم أو خاسر (يُعاد للتسعير أولاً).</summary>
    public static bool LocksEditing(string? status) => status is Submitted or Lost;

    /// <summary>الانتقالات المسموحة.</summary>
    public static bool CanMove(string? from, string to) => (from, to) switch
    {
        (New, Studying or Pricing or Lost) => true,
        (Studying, Pricing or Lost) => true,
        (Pricing, Studying or Submitted or Lost) => true,
        (Submitted, Pricing or Awarded or Lost) => true,
        _ => false,
    };
}

/// <summary>
/// نسخة العرض المقدَّم (لقطة عند كل تقديم للمناقصة): القيم وأسطر جدول الكميات بأسعار العطاء والمتعاقد عليها والتكلفة التقديرية،
/// فيبقى السعر الأصلي والسعر بعد التفاوض وكل مراجعة محفوظة ولو عُدِّل العقد بعدها.
/// </summary>
public class ContractTenderRevision : BaseEntity
{
    public Guid ContractId { get; set; }
    public int RevisionNo { get; set; }
    public DateTime SubmittedAt { get; set; }
    public string? SubmittedBy { get; set; }
    public decimal TenderValue { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal ContractValue { get; set; }
    public decimal VatAmount { get; set; }
    public decimal EstimatedCost { get; set; }
    public string? Note { get; set; }
    /// <summary>أسطر العرض (JSON): الرقم والوصف والوحدة والكمية وسعر العطاء والسعر المتعاقد والتكلفة التقديرية.</summary>
    public string LinesJson { get; set; } = "[]";
}
