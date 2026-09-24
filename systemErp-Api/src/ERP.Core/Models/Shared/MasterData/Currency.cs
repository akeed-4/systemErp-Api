
namespace ERP.Core.Models.Shared;

public class Currency : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public bool IsBaseCurrency { get; set; }
    public decimal ExchangeRate { get; set; } = 1.0m;
    public int DecimalPlaces { get; set; } = 2;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "active";
}
