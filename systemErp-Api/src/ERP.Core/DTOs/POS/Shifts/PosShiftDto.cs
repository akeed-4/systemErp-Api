namespace ERP.Core.DTOs.POS;

public partial class PosShiftDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string ShiftNumber { get; set; } = string.Empty;
    public Guid CashierId { get; set; }
    public string CashierName { get; set; } = string.Empty;
    public string PosTerminalName { get; set; } = string.Empty;
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public PosShiftStatus Status { get; set; } = PosShiftStatus.Open;
    public decimal OpeningCash { get; set; }
    public decimal TotalCashSales { get; set; }
    public decimal TotalCardSales { get; set; }
    public decimal TotalMadaSales { get; set; }
    public decimal TotalApplePaySales { get; set; }
    public decimal TotalCreditSales { get; set; }
    public decimal TotalReturns { get; set; }
    public decimal TotalCashRefunds { get; set; }
    public decimal TotalDiscount { get; set; }
    public decimal TotalVat { get; set; }
    public decimal TotalGross { get; set; }
    public decimal? ClosingCashActual { get; set; }
    public decimal? CashVariance { get; set; }
    public string? ClosingNotes { get; set; }
}
