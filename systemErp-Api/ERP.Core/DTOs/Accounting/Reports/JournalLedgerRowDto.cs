namespace ERP.Core.DTOs.Accounting;

/// <summary>صف دفتر اليومية: سطر واحد لكل طرف مدين/دائن مع بيانات قيده.</summary>
public class JournalLedgerRowDto
{
    public Guid LineId { get; set; }
    public Guid EntryId { get; set; }
    public string EntryNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
/// <summary>
/// سطر في دفتر اليومية العام: سطر قيد مع بيانات رأس القيد. يُرقَّم ويُرشَّح ويُفرز ويُلخَّص في SQL عبر خيارات DevExtreme.
/// ملخص الجدول: Σ Debit و Σ Credit على كامل النتيجة المرشّحة.
/// </summary>
public class JournalLedgerRowDto
{
    public Guid Id { get; set; }
    public Guid JournalEntryId { get; set; }
    public DateTime Date { get; set; }
    public string EntryNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ReferenceType { get; set; }
    public string? ReferenceNumber { get; set; }
    public JournalEntryStatus Status { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? Notes { get; set; }
}
}