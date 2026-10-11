namespace ERP.Core.DTOs.Accounting;

/// <summary>قائمة المركز المالي حتى تاريخ، من سطور القيود: الأصول = الخصوم + حقوق الملكية + نتيجة الفترة غير المقفلة.</summary>
public class BalanceSheetDto
{
    public DateTime AsOf { get; set; }
    public List<BalanceSheetLineDto> Assets { get; set; } = new();
    public List<BalanceSheetLineDto> Liabilities { get; set; } = new();
    public List<BalanceSheetLineDto> Equity { get; set; } = new();
    public decimal TotalAssets { get; set; }
    public decimal TotalLiabilities { get; set; }
    public decimal TotalEquity { get; set; }
    /// <summary>صافي الإيرادات ناقص المصروفات التي لم تُقفل بعد في الأرباح المبقاة.</summary>
    public decimal NetProfit { get; set; }
    public decimal TotalLiabilitiesAndEquity { get; set; }
    public bool IsBalanced { get; set; }
}

public class BalanceSheetLineDto
{
    public string AccountCode { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    /// <summary>بطبيعة القسم: الأصل بمدينه، والخصم وحق الملكية بدائنهما (الحساب المقابل يظهر سالباً).</summary>
    public decimal Amount { get; set; }
}
