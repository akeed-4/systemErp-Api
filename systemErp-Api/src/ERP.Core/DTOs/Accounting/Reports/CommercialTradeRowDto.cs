using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public class CommercialTradeRowDto
{
    public string DocNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public InvoiceKind Type { get; set; }
    public string PartyName { get; set; } = string.Empty;
    public string? VatNumber { get; set; }
    public int ItemsCount { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal CogsTotal { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal GrossProfitMarginPercent { get; set; }
    public ZatcaSubmissionStatus ZatcaStatus { get; set; }
}
