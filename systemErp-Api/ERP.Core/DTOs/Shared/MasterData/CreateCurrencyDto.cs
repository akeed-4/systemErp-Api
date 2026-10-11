namespace ERP.Core.DTOs.Shared;

public partial class CreateCurrencyDto
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public bool IsBaseCurrency { get; set; }
    public decimal ExchangeRate { get; set; } = 1.0m;
    public int DecimalPlaces { get; set; } = 2;
    public DateTime LastUpdated { get; set; }
    public string Status { get; set; } = "active";
}
