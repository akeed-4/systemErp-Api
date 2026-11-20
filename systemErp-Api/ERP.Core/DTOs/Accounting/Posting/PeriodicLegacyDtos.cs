namespace ERP.Core.DTOs.Accounting;

/// <summary>
/// معاينة معالجة الجرد الدوري القديمة لمنشأة واحدة: قيود سابقة نقلت قيمة أصل المخزون إلى بضاعة أول المدة (مصروف) في الدورة الحالية
/// (بعد آخر قيد آخر مدة)، فيظهر المخزون صفراً في الميزانية. المعالجة قيد إعادة تصنيف جديد لكل قيد قديم (لا يُعدَّل القديم ولا يُعكس).
/// </summary>
public class PeriodicLegacyPreviewDto
{
    /// <summary>المنشأة على الجرد الدوري (وإلا لا معالجة).</summary>
    public bool Applicable { get; set; }
    /// <summary>تاريخ آخر قيد آخر مدة (بداية الدورة الحالية)؛ فارغ = لا جرد منذ البداية.</summary>
    public DateTime? CycleStart { get; set; }
    /// <summary>أول يوم مفتوح للترحيل (بعد تاريخ إقفال الدفاتر).</summary>
    public DateTime? FirstOpenDate { get; set; }
    public List<PeriodicLegacyEntryDto> Entries { get; set; } = new();
    public decimal ReadyAmount { get; set; }
    public decimal LockedAmount { get; set; }
    public decimal ClosedYearAmount { get; set; }
    /// <summary>بصمة القيود ومبالغها: يُرسلها التنفيذ ويُرفض إن تغيّرت البيانات منذ المعاينة.</summary>
    public string Fingerprint { get; set; } = string.Empty;
}

public class PeriodicLegacyEntryDto
{
    public Guid JournalEntryId { get; set; }
    public string EntryNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    /// <summary>period_opening (عكس آخر المدة في اليوم التالي) | method_opening (بدء الدوري بعد المستمر) | opening_balance (تسوية بحساب مقابل صريح)</summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>المبلغ المنقول إلى بضاعة أول المدة (السالب: نقص نُقل منها).</summary>
    public decimal Amount { get; set; }
    /// <summary>ready (فترة مفتوحة: يُعالج بتاريخه) | locked_period (فترة مقفلة يدوياً) | closed_year (سنة مقفلة: لا معالجة آلية)</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>للفترة المقفلة: أول يوم مفتوح إن كان في السنة المالية نفسها (المعالجة به اختيار صريح)؛ وإلا فارغ.</summary>
    public DateTime? FallbackDate { get; set; }
    /// <summary>سطور قيد إعادة التصنيف كما ستُرحَّل.</summary>
    public List<PeriodicLegacyLineDto> Lines { get; set; } = new();
}

public class PeriodicLegacyLineDto
{
    public string AccountCode { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
}

public class ApplyPeriodicLegacyDto
{
    public string Fingerprint { get; set; } = string.Empty;
    /// <summary>قيود الفترات المقفلة يدوياً تُعالج بأول يوم مفتوح (في سنتها المالية نفسها)؛ وإلا تبقى حتى تُفتح الفترة.</summary>
    public bool UseFirstOpenDateForLocked { get; set; }
}

public class PeriodicLegacyResultDto
{
    public List<PeriodicLegacyPostedDto> Posted { get; set; } = new();
    /// <summary>ما بقي بعد التنفيذ (الفترات المقفلة والسنوات المقفلة).</summary>
    public PeriodicLegacyPreviewDto Remaining { get; set; } = new();
}

public class PeriodicLegacyPostedDto
{
    public string LegacyEntryNumber { get; set; } = string.Empty;
    public Guid JournalEntryId { get; set; }
    public string EntryNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
}

public static class PeriodicLegacyStatus
{
    public const string Ready = "ready";
    public const string LockedPeriod = "locked_period";
    public const string ClosedYear = "closed_year";
}
