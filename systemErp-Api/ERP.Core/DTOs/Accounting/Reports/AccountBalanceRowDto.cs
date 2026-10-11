using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

/// <summary>صف ميزان الأرصدة الحالية: رصيد الحساب موزَّعاً على عمودي المدين/الدائن حسب طبيعته.</summary>
public class AccountBalanceRowDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public AccountCategory Type { get; set; }
    public int Level { get; set; }
    public decimal Balance { get; set; }
    public bool IsDebitNature { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
}
