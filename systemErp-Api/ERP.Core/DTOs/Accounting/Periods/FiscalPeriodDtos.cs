namespace ERP.Core.DTOs.Accounting;

public class FiscalPeriodStatusDto
{
    /// <summary>الدفاتر مقفلة حتى هذا التاريخ شاملاً؛ null = لا إقفال.</summary>
    public DateTime? BooksLockedThrough { get; set; }
    /// <summary>بداية ونهاية السنة المالية (MM-dd).</summary>
    public string FinancialYearStart { get; set; } = "01-01";
    public string FinancialYearEnd { get; set; } = "12-31";
    /// <summary>السنوات المالية المقفلة (سنة نهاية كل منها).</summary>
    public List<int> ClosedYears { get; set; } = new();
}

public class SetPeriodLockDto
{
    public DateTime? LockedThrough { get; set; }
}

public class CloseYearRequestDto
{
    /// <summary>السنة المالية المراد إقفالها (السنة التي تنتهي فيها).</summary>
    public int Year { get; set; }
}

public class YearClosingResultDto
{
    public int Year { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    /// <summary>قيد الإقفال؛ فارغ إن لم تكن للسنة إيرادات أو مصروفات.</summary>
    public Guid? JournalEntryId { get; set; }
    public string? JournalEntryNumber { get; set; }
    public decimal TotalRevenues { get; set; }
    public decimal TotalExpenses { get; set; }
    /// <summary>صافي الربح (موجب) أو الخسارة (سالب) المرحَّل إلى الأرباح المبقاة.</summary>
    public decimal NetProfit { get; set; }
    public DateTime BooksLockedThrough { get; set; }
}
