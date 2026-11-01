using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public class AccountStatementDto
{
    public string AccountCode { get; set; } = string.Empty;
    public string AccountNameAr { get; set; } = string.Empty;
    public string AccountNameEn { get; set; } = string.Empty;
    public bool IsDebitNature { get; set; }
    /// <summary>عملة الحساب (العملة الأساسية إن لم تُحدَّد) وسعر صرفها الحالي إلى العملة الأساسية.</summary>
    public string Currency { get; set; } = string.Empty;
    public decimal ExchangeRate { get; set; } = 1;
    public decimal OpeningBalance { get; set; }
    public decimal PeriodDebit { get; set; }
    public decimal PeriodCredit { get; set; }
    public decimal ClosingBalance { get; set; }
    public decimal ClosingBalanceInAccountCurrency { get; set; }
    /// <summary>مرشّح مركز التكلفة (يشمل فروعه)؛ الأرصدة عندئذٍ أرصدة الحساب في المركز.</summary>
    public Guid? CostCenterId { get; set; }
    public List<AccountLedgerEntryDto> Entries { get; set; } = new();
}
