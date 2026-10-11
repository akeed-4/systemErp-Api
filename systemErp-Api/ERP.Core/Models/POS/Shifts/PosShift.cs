namespace ERP.Core.Models.POS;

/// <summary>وردية كاشير: تُفتح برصيد افتتاحي وتُغلق بجرد نقدي فعلي ويُحسب الفرق.</summary>
public class PosShift : BaseEntity
{
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
    /// <summary>إجمالي المرتجعات (كل الطرق).</summary>
    public decimal TotalReturns { get; set; }
    /// <summary>الجزء النقدي من المرتجعات فقط - يدخل في حساب فرق الصندوق.</summary>
    public decimal TotalCashRefunds { get; set; }
    public decimal TotalDiscount { get; set; }
    public decimal TotalVat { get; set; }
    public decimal TotalGross { get; set; }

    /// <summary>إيداعات وصرفيات نقدية على الدرج خارج البيع (PosCashMovement).</summary>
    public decimal TotalCashIn { get; set; }
    public decimal TotalCashOut { get; set; }

    public decimal? ClosingCashActual { get; set; }
    public decimal? CashVariance { get; set; }
    public string? ClosingNotes { get; set; }
    /// <summary>جرد الإغلاق بالفئات (JSON: [{value,count}]).</summary>
    public string? ClosingDenominations { get; set; }
    /// <summary>قيد عجز/زيادة الصندوق عند الإغلاق.</summary>
    public Guid? VarianceJournalEntryId { get; set; }
}
