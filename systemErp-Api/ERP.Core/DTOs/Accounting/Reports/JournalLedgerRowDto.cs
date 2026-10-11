namespace ERP.Core.DTOs.Accounting;

/// <summary>صف دفتر اليومية: سطر واحد لكل طرف مدين/دائن مع بيانات قيده.</summary>
public class JournalLedgerRowDto
{
    public Guid LineId { get; set; }
    public Guid EntryId { get; set; }
    public string EntryNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? ReferenceType { get; set; }
}
