using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public class AccountLedgerEntryDto
{
    public DateTime Date { get; set; }
    public string EntryNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal RunningBalance { get; set; }
    public string? ReferenceNumber { get; set; }
}
