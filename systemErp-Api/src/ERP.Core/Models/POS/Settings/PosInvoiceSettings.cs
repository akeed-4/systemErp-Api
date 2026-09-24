namespace ERP.Core.Models.POS;

/// <summary>إعدادات فاتورة نقاط البيع - سجل واحد لكل منشأة.</summary>
public class PosInvoiceSettings : BaseEntity
{
    /// <summary>simplified | standard</summary>
    public string DefaultInvoiceType { get; set; } = "simplified";
    public bool ShowCompanyLogo { get; set; } = true;
    public string HeaderTextAr { get; set; } = string.Empty;
    public string HeaderTextEn { get; set; } = string.Empty;
    public string FooterTextAr { get; set; } = string.Empty;
    public string FooterTextEn { get; set; } = string.Empty;
    public bool AutoPrintOnCheckout { get; set; } = true;
    /// <summary>80mm | 58mm | A4</summary>
    public string PaperSize { get; set; } = "80mm";
    public bool EnableThermalPrinter { get; set; }
    public bool ShowVatBreakdown { get; set; } = true;
    public bool ShowLoyaltyOnReceipt { get; set; } = true;
}
