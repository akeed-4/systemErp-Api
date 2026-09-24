using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public class FinancialSummaryDto
{
    public decimal TotalAssets { get; set; }
    public decimal TotalLiabilities { get; set; }
    public decimal TotalEquity { get; set; }
    public decimal TotalRevenues { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal NetProfitOrLoss { get; set; }
    public bool IsBalanceSheetBalanced { get; set; }
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}
