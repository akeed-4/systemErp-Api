namespace ERP.Core.DTOs.Accounting;

/// <summary>حركة على حساب البنك لم تُطابَق بعد مع كشف.</summary>
public class BankLineDto
{
    public Guid LineId { get; set; }
    public DateTime Date { get; set; }
    public string EntryNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ReferenceNumber { get; set; }
    /// <summary>مدين الحساب = إيداع/وارد.</summary>
    public decimal Debit { get; set; }
    /// <summary>دائن الحساب = سحب/منصرف.</summary>
    public decimal Credit { get; set; }
}

/// <summary>ورقة عمل التسوية لحساب حتى تاريخ: رصيد الدفاتر، رصيد ما طوبق سابقاً، والحركات غير المطابَقة.</summary>
public class BankReconciliationWorksheetDto
{
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public DateTime StatementDate { get; set; }
    public decimal BookBalance { get; set; }
    /// <summary>مجموع الحركات المطابَقة في تسويات سابقة (= رصيد آخر كشف مطابَق).</summary>
    public decimal PreviouslyReconciledBalance { get; set; }
    public List<BankLineDto> UnreconciledLines { get; set; } = new();
}

public class CreateBankReconciliationDto
{
    public string AccountCode { get; set; } = string.Empty;
    public DateTime StatementDate { get; set; }
    public decimal StatementBalance { get; set; }
    /// <summary>الحركات التي ظهرت في كشف البنك.</summary>
    public List<Guid> LineIds { get; set; } = new();
    public string? Notes { get; set; }
}

public class BankReconciliationDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public DateTime StatementDate { get; set; }
    public decimal StatementBalance { get; set; }
    public decimal BookBalance { get; set; }
    public decimal OutstandingDeposits { get; set; }
    public decimal OutstandingPayments { get; set; }
    public int ReconciledLines { get; set; }
    public string? Notes { get; set; }
    public string PerformedBy { get; set; } = string.Empty;
}
