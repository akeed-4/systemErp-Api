using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public class AccountStatementDto
{
    public string AccountCode { get; set; } = string.Empty;
    public string AccountNameAr { get; set; } = string.Empty;
    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance { get; set; }
    public List<AccountLedgerEntryDto> Entries { get; set; } = new();
}
