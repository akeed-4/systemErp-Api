using ERP.Core.DTOs.Accounting;

namespace ERP.Core.DTOs.Shared;

/// <summary>قيد فروق الجرد: المرحَّل فعلاً بعد الاعتماد، أو معاينة مطابقة لما سيُرحَّل قبل الاعتماد (IsPreview).</summary>
public class InventoryCountJournalDto
{
    public bool IsPreview { get; set; }
    /// <summary>لا يوجد قيد: لا فروق لها قيمة، أو المستند رُفض/سُحب.</summary>
    public bool HasJournal => Lines.Count > 0;
    public Guid? JournalEntryId { get; set; }
    public string? EntryNumber { get; set; }
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<InvoiceJournalLineDto> Lines { get; set; } = new();
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
}
