namespace ERP.Core.Models.CarShowroom;

public enum TammWalletTransactionType
{
    TopUp = 1,          // شحن المحفظة من البنك/الصندوق
    Charge = 2,         // خصم رسم عملية
    Adjustment = 3,     // تسوية فرق المطابقة
    Reconciliation = 4  // مطابقة مع كشف تم (سجل، بلا أثر مالي)
}

/// <summary>
/// حركة على محفظة «تم»: شحن وخصم ومطابقة. الرصيد الدفتري هو رصيد حساب المحفظة في شجرة الحسابات؛
/// هذا السجل تفصيل حركاته ومرجع مطابقته مع كشف تم.
/// </summary>
public class TammWalletTransaction : BaseEntity
{
    public DateTime Date { get; set; }
    public TammWalletTransactionType Type { get; set; }
    /// <summary>موجب = يزيد رصيد المحفظة، سالب = ينقصه. صفر لسجل المطابقة.</summary>
    public decimal Amount { get; set; }
    public string? TreasuryAccountCode { get; set; }
    public Guid? TammOperationId { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public Guid? JournalEntryId { get; set; }
    // للمطابقة
    public decimal? StatementBalance { get; set; }
    public decimal? BookBalance { get; set; }
    public decimal? Difference { get; set; }
}
