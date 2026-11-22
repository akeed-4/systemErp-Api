namespace ERP.Core.DTOs.Accounting;

public partial class JournalEntryDto
{
    public bool IsBalanced => TotalDebit == TotalCredit;

    /// <summary>نوع القيد للعرض: manual (يدوي)، reversal (عكسي، مرجعه SourceReferenceId)، document (تلقائي من مستند أو إقفال).</summary>
    public string EntryKind => SourceType switch
    {
        null or "manual" => "manual",
        "reversal" => "reversal",
        _ => "document",
    };

    /// <summary>رقم القيد العكسي لقيد معكوس (يُملأ عند القراءة؛ فارغ = لم يُعكس).</summary>
    public string? ReversedByEntryNumber { get; set; }
}
