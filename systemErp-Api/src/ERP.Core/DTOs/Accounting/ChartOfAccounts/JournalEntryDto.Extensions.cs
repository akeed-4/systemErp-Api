namespace ERP.Core.DTOs.Accounting;

public partial class JournalEntryDto
{
    public bool IsBalanced => TotalDebit == TotalCredit;
}
