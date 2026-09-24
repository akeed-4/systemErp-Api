namespace ERP.Core.DTOs.POS;

public partial class CreatePosInvoiceSettingsDto
{
    public string DefaultInvoiceType { get; set; } = "simplified";
    public bool ShowCompanyLogo { get; set; } = true;
    public string HeaderTextAr { get; set; } = string.Empty;
    public string HeaderTextEn { get; set; } = string.Empty;
    public string FooterTextAr { get; set; } = string.Empty;
    public string FooterTextEn { get; set; } = string.Empty;
    public bool AutoPrintOnCheckout { get; set; } = true;
    public string PaperSize { get; set; } = "80mm";
    public bool EnableThermalPrinter { get; set; }
    public bool ShowVatBreakdown { get; set; } = true;
    public bool ShowLoyaltyOnReceipt { get; set; } = true;
}
