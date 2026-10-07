namespace ERP.Core.Models.Accounting;

/// <summary>
/// تسوية بنكية: مطابقة حركات حساب نقدي/بنكي في الدفاتر مع كشف البنك حتى تاريخ. الحركات المطابَقة تُعلَّم
/// (JournalEntryLine.BankReconciliationId)، والتسوية لا تُعتمد إلا إذا ساوى رصيد المطابَق رصيد الكشف.
/// </summary>
public class BankReconciliation : BaseEntity
{
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public DateTime StatementDate { get; set; }
    /// <summary>رصيد كشف البنك في تاريخ الكشف.</summary>
    public decimal StatementBalance { get; set; }
    /// <summary>رصيد الحساب في الدفاتر في تاريخ الكشف.</summary>
    public decimal BookBalance { get; set; }
    /// <summary>إيداعات في الدفاتر لم تظهر في الكشف بعد.</summary>
    public decimal OutstandingDeposits { get; set; }
    /// <summary>مدفوعات في الدفاتر لم تُصرف من البنك بعد.</summary>
    public decimal OutstandingPayments { get; set; }
    public int ReconciledLines { get; set; }
    public string? Notes { get; set; }
    public string PerformedBy { get; set; } = string.Empty;
}
