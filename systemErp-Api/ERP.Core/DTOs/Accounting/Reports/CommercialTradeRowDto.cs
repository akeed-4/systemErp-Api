using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public class CommercialTradeRowDto
{
    public Guid Id { get; set; }
    public string DocNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public InvoiceKind Type { get; set; }
    public string PartyName { get; set; } = string.Empty;
    public string? VatNumber { get; set; }
    public int ItemsCount { get; set; }
    // المبالغ بإشارة المرتجع (سالبة للمرتجعات)
    /// <summary>الإجمالي قبل أي خصم.</summary>
    public decimal GrossTotal { get; set; }
    /// <summary>خصم الفاتورة (على المستند).</summary>
    public decimal InvoiceDiscount { get; set; }
    /// <summary>إجمالي الخصومات على الأصناف.</summary>
    public decimal ItemsDiscountTotal { get; set; }
    public decimal DiscountTotal { get; set; }
    /// <summary>الصافي قبل الضريبة (بعد كل الخصومات).</summary>
    public decimal Subtotal { get; set; }
    /// <summary>الضرائب الأخرى (الانتقائية).</summary>
    public decimal ExciseTaxTotal { get; set; }
    /// <summary>المبلغ بعد الضرائب الأخرى = الصافي قبل الضريبة + الضرائب الأخرى.</summary>
    public decimal AmountAfterOtherTaxes { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public string CurrencyCode { get; set; } = "SAR";
    public decimal ExchangeRate { get; set; } = 1m;
    public decimal CogsTotal { get; set; }
    public decimal GrossProfit { get; set; }
    /// <summary>مجمل الربح ÷ الصافي قبل الضريبة.</summary>
    public decimal GrossProfitMarginPercent { get; set; }
    /// <summary>نسبة الربح على التكلفة: الربح ÷ التكلفة.</summary>
    public decimal ProfitPercent { get; set; }
    public string? SalesRepName { get; set; }
    public ZatcaSubmissionStatus ZatcaStatus { get; set; }
}
