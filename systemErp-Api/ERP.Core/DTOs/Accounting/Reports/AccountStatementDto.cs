using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public class AccountStatementDto
{
    public string AccountCode { get; set; } = string.Empty;
    public string AccountNameAr { get; set; } = string.Empty;
    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance { get; set; }
    /// <summary>مرشّح مركز التكلفة (يشمل فروعه)؛ الأرصدة عندئذٍ أرصدة الحساب في المركز.</summary>
    public Guid? CostCenterId { get; set; }
    public List<AccountLedgerEntryDto> Entries { get; set; } = new();
}
