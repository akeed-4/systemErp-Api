namespace ERP.Core.DTOs.Accounting;

/// <summary>
/// إجماليات القيود على كامل النتيجة المرشّحة (نفس مرشّحات القائمة)، محسوبة في SQL.
/// <list type="bullet">
/// <item>Count = عدد القيود المطابقة.</item>
/// <item>TotalDebit = Σ TotalDebit، TotalCredit = Σ TotalCredit (رؤوس القيود).</item>
/// <item>UnbalancedCount = عدد القيود التي TotalDebit ≠ TotalCredit (يجب أن يكون 0 للقيود المرحّلة).</item>
/// </list>
/// </summary>
public class JournalEntrySummaryDto
{
    public int Count { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public int UnbalancedCount { get; set; }
}
