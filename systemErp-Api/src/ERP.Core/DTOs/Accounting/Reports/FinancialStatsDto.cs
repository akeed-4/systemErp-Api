using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

/// <summary>يطابق FinancialStats في erp.models.ts.</summary>
public class FinancialStatsDto
{
    public decimal TotalSales { get; set; }
    public decimal TotalPurchases { get; set; }
    public decimal TotalReceipts { get; set; }
    public decimal TotalPayments { get; set; }
    public decimal CogsTotal { get; set; }
    public decimal OperatingExpenses { get; set; }
    public decimal NetProfit { get; set; }
    public decimal InventoryValuation { get; set; }
    public decimal OutputVat { get; set; }
    public decimal InputVat { get; set; }
    public decimal NetVatPayable { get; set; }
    public decimal CashAndBankBalance { get; set; }
    public decimal ReceivablesBalance { get; set; }
    public decimal PayablesBalance { get; set; }
}
