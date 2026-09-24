namespace ERP.Core.DTOs.Accounting;

public partial class CreateJournalEntryDto
{
    public string EntryNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? ReferenceNumber { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public JournalEntryStatus Status { get; set; } = JournalEntryStatus.Draft;
    public string? SourceType { get; set; }
    public Guid? SourceReferenceId { get; set; }
    public List<JournalEntryLineDto> Lines { get; set; } = new();
}
