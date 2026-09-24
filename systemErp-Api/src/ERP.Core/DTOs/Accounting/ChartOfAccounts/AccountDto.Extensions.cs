namespace ERP.Core.DTOs.Accounting;

public partial class AccountDto
{
    /// <summary>الأبناء المباشرون - تُملأ فقط عند طلب الشجرة.</summary>
    public List<AccountDto>? Children { get; set; }
}
