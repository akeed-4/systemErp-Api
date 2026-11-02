namespace ERP.Core.Models.Shared;

/// <summary>
/// عدّاد ترقيم المستندات لكل منشأة (فاتورة، قيد، سند، ...). يُزاد داخل معاملة قاعدة البيانات.
/// صف المفتاح العام (sales_invoice) يحمل أيضاً إعداد ترقيم نوع المستند؛ عدّادات النطاق والسنة (sales_invoice@warehouse:…@fy:2026)
/// صفوف عدّ فقط تأخذ إعدادها من الصف العام.
/// </summary>
public class NumberSequence : BaseEntity
{
    /// <summary>مثال: sales_invoice, journal_entry, receipt_voucher</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>البادئة؛ تدعم {YYYY} و{YY} (سنة السنة المالية لتاريخ المستند).</summary>
    public string Prefix { get; set; } = string.Empty;
    public long LastValue { get; set; }
    /// <summary>طول الرقم بعد البادئة (يُكمَّل بأصفار).</summary>
    public int Padding { get; set; } = 6;
    /// <summary>رمز تزامن متفائل: يتغيّر مع كل تعديل فيمنع إصدار رقمين متطابقين عند الطلبات المتزامنة.</summary>
    public Guid Version { get; set; } = Guid.NewGuid();

    // ---- إعداد نوع المستند (على صف المفتاح العام فقط) ----
    /// <summary>auto = يصدره النظام | manual = يدخله المستخدم (لأنواع تدعمه فقط).</summary>
    public string Mode { get; set; } = "auto";
    /// <summary>نطاق الترقيم التلقائي لهذا النوع: فارغ = إعداد المنشأة العام | tenant | warehouse (المستودع/المعرض).</summary>
    public string? Scope { get; set; }
    /// <summary>تسلسل جديد لكل سنة مالية (بتاريخ المستند وبداية السنة المالية للمنشأة).</summary>
    public bool ResetYearly { get; set; }
    /// <summary>معطّل = يعود للترقيم التلقائي الافتراضي (البادئة الافتراضية، 6 خانات، بلا تصفير سنوي) دون منع إنشاء المستند.</summary>
    public bool IsActive { get; set; } = true;
}
