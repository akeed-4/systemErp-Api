namespace ERP.Core.DTOs.Accounting;

/// <summary>سطر قائمة مالية: حساب بمبلغه في الفترة ومبلغه في فترة المقارنة (إن طُلبت).</summary>
public class StatementLineDto
{
    public string AccountCode { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Comparative { get; set; }
}

/// <summary>قسم من قائمة: سطوره ومجموعه للفترة ولفترة المقارنة.</summary>
public class StatementSectionDto
{
    public List<StatementLineDto> Lines { get; set; } = new();
    public decimal Total { get; set; }
    public decimal Comparative { get; set; }
}

/// <summary>قائمة الدخل لفترة، من سطور القيود المرحّلة (دون قيود إقفال السنة)، مع فترة مقارنة اختيارية.</summary>
public class IncomeStatementDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public DateTime? CompareFrom { get; set; }
    public DateTime? CompareTo { get; set; }
    public StatementSectionDto Revenue { get; set; } = new();
    public StatementSectionDto CostOfSales { get; set; } = new();
    public StatementSectionDto GrossProfit { get; set; } = new();
    public StatementSectionDto OperatingExpenses { get; set; } = new();
    public StatementSectionDto NetProfit { get; set; } = new();
}

/// <summary>قائمة المركز المالي في تاريخ، مع تاريخ مقارنة اختياري: الأصول = الخصوم + حقوق الملكية + نتيجة غير مقفلة.</summary>
public class FinancialPositionDto
{
    public DateTime AsOf { get; set; }
    public DateTime? CompareAsOf { get; set; }
    public StatementSectionDto CurrentAssets { get; set; } = new();
    public StatementSectionDto NonCurrentAssets { get; set; } = new();
    public StatementSectionDto TotalAssets { get; set; } = new();
    public StatementSectionDto Liabilities { get; set; } = new();
    public StatementSectionDto Equity { get; set; } = new();
    /// <summary>صافي الإيرادات ناقص المصروفات التي لم تُقفل بعد في الأرباح المبقاة.</summary>
    public StatementSectionDto UnclosedProfit { get; set; } = new();
    public StatementSectionDto TotalLiabilitiesAndEquity { get; set; } = new();
    public bool IsBalanced { get; set; }
}

/// <summary>
/// قائمة التدفقات النقدية (الطريقة المباشرة): كل حركة على النقدية والبنوك تُنسب إلى الحساب المقابل لها في قيدها،
/// وتُصنَّف تشغيلية أو استثمارية (الأصول الثابتة) أو تمويلية (حقوق الملكية والقروض طويلة الأجل).
/// </summary>
public class CashFlowDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public DateTime? CompareFrom { get; set; }
    public DateTime? CompareTo { get; set; }
    public StatementSectionDto OpeningCash { get; set; } = new();
    public StatementSectionDto Operating { get; set; } = new();
    public StatementSectionDto Investing { get; set; } = new();
    public StatementSectionDto Financing { get; set; } = new();
    public StatementSectionDto NetChange { get; set; } = new();
    public StatementSectionDto ClosingCash { get; set; } = new();
}
