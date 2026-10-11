namespace ERP.Core.DTOs.CarShowroom;

public class CarDailyMonthlySalesRowDto
{
    public string PeriodKey { get; set; } = string.Empty;
    public string PeriodType { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int CarsSoldCount { get; set; }
    public int CashSalesCount { get; set; }
    public int CreditSalesCount { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalCost { get; set; }
    public decimal TotalGrossProfit { get; set; }
    public decimal ProfitMarginPercent { get; set; }
    public decimal VatCollected { get; set; }
    public decimal AverageCarPrice { get; set; }
}
